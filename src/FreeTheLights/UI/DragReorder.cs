using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace FreeTheLights.UI
{
    /// <summary>Old and new position of an item reordered by drag &amp; drop.</summary>
    public sealed class MoveRequest
    {
        public MoveRequest(int oldIndex, int newIndex)
        {
            OldIndex = oldIndex;
            NewIndex = newIndex;
        }

        public int OldIndex { get; }

        public int NewIndex { get; }
    }

    /// <summary>
    /// Drag &amp; drop reordering for a vertical ItemsControl.
    /// <list type="bullet">
    /// <item><c>DragReorder.IsEnabled="True"</c> on the ItemsControl turns it on.</item>
    /// <item><c>DragReorder.Handle="True"</c> on an element of the item template marks the grip; dragging only starts there,
    /// so text boxes and combo boxes inside the items keep working.</item>
    /// <item><c>DragReorder.MoveCommand</c> receives a <see cref="MoveRequest"/>; without it the ItemsSource is reordered
    /// directly when it has a <c>Move(int, int)</c> method (ObservableCollection). Filtered or rebuilt lists need the command.</item>
    /// </list>
    /// While dragging, the item follows the mouse and the items it passes slide out of its way (render transforms only);
    /// the collection is reordered once, when the item is dropped into its slot.
    /// </summary>
    public static class DragReorder
    {
        private static readonly Duration ShiftDuration = TimeSpan.FromMilliseconds(160);
        private static readonly Duration SettleDuration = TimeSpan.FromMilliseconds(120);
        private static readonly IEasingFunction Ease = Freeze(new CubicEase { EasingMode = EasingMode.EaseOut });

        /// <summary>Page color under the lists (SimHub's dark theme); translucent row backgrounds are made opaque on it.</summary>
        private static readonly Color PageColor = Color.FromRgb(0x25, 0x25, 0x26);

        public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
            "IsEnabled", typeof(bool), typeof(DragReorder), new PropertyMetadata(false, OnIsEnabledChanged));

        public static readonly DependencyProperty HandleProperty = DependencyProperty.RegisterAttached(
            "Handle", typeof(bool), typeof(DragReorder), new FrameworkPropertyMetadata(false, OnHandleChanged));

        public static readonly DependencyProperty MoveCommandProperty = DependencyProperty.RegisterAttached(
            "MoveCommand", typeof(ICommand), typeof(DragReorder), new PropertyMetadata(null));

        private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
            "State", typeof(DragState), typeof(DragReorder), new PropertyMetadata(null));

        public static bool GetIsEnabled(DependencyObject d) => (bool)d.GetValue(IsEnabledProperty);

        public static void SetIsEnabled(DependencyObject d, bool value) => d.SetValue(IsEnabledProperty, value);

        public static bool GetHandle(DependencyObject d) => (bool)d.GetValue(HandleProperty);

        public static void SetHandle(DependencyObject d, bool value) => d.SetValue(HandleProperty, value);

        public static ICommand GetMoveCommand(DependencyObject d) => (ICommand)d.GetValue(MoveCommandProperty);

        public static void SetMoveCommand(DependencyObject d, ICommand value) => d.SetValue(MoveCommandProperty, value);

        /// <summary>
        /// Final index of an item dragged from <paramref name="oldIndex"/> and dropped in front of the item at
        /// <paramref name="insertionIndex"/> (0..count): the item itself is removed first, which shifts later positions.
        /// </summary>
        public static int TargetIndex(int oldIndex, int insertionIndex) => insertionIndex > oldIndex ? insertionIndex - 1 : insertionIndex;

        /// <summary>Moves an item through the MoveCommand or, without one, directly in the ItemsSource.</summary>
        public static bool MoveItem(ItemsControl itemsControl, int oldIndex, int newIndex)
        {
            if (oldIndex == newIndex || oldIndex < 0 || newIndex < 0 || newIndex >= itemsControl.Items.Count) return false;

            var request = new MoveRequest(oldIndex, newIndex);
            var command = GetMoveCommand(itemsControl);
            if (command != null)
            {
                if (!command.CanExecute(request)) return false;
                command.Execute(request);
                return true;
            }

            var source = itemsControl.ItemsSource;
            var move = source?.GetType().GetMethod("Move", new[] { typeof(int), typeof(int) });
            if (move == null) return false;
            move.Invoke(source, new object[] { oldIndex, newIndex });
            return true;
        }

        private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (!(d is ItemsControl itemsControl)) return;

            if ((bool)e.NewValue)
            {
                itemsControl.SetValue(StateProperty, new DragState());
                itemsControl.PreviewMouseLeftButtonDown += OnMouseDown;
                itemsControl.PreviewMouseMove += OnMouseMove;
                itemsControl.PreviewMouseLeftButtonUp += OnMouseUp;
                itemsControl.PreviewKeyDown += OnKeyDown;
                itemsControl.LostMouseCapture += OnLostMouseCapture;
            }
            else
            {
                itemsControl.PreviewMouseLeftButtonDown -= OnMouseDown;
                itemsControl.PreviewMouseMove -= OnMouseMove;
                itemsControl.PreviewMouseLeftButtonUp -= OnMouseUp;
                itemsControl.PreviewKeyDown -= OnKeyDown;
                itemsControl.LostMouseCapture -= OnLostMouseCapture;
                itemsControl.ClearValue(StateProperty);
            }
        }

        private static void OnHandleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is FrameworkElement element && (bool)e.NewValue)
            {
                element.Cursor = Cursors.SizeNS;
                if (element.ToolTip == null) element.ToolTip = "Drag to reorder";
            }
        }

        private static DragState StateOf(object sender) => (DragState)((ItemsControl)sender).GetValue(StateProperty);

        private static void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            var itemsControl = (ItemsControl)sender;
            var state = StateOf(sender);
            if (state.IsDragging || state.IsSettling) return;
            state.SourceIndex = -1;

            if (!IsOnHandle(e.OriginalSource as DependencyObject, itemsControl)) return;
            var container = ItemsControl.ContainerFromElement(itemsControl, (DependencyObject)e.OriginalSource);
            if (container == null) return;

            state.SourceIndex = itemsControl.ItemContainerGenerator.IndexFromContainer(container);
            state.StartPoint = e.GetPosition(itemsControl);
        }

        private static void OnMouseMove(object sender, MouseEventArgs e)
        {
            var itemsControl = (ItemsControl)sender;
            var state = StateOf(sender);
            if (state.SourceIndex < 0 || state.IsSettling || state.Capturing) return;
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                if (state.IsDragging) Cancel(itemsControl, state);
                state.SourceIndex = -1;
                return;
            }

            var position = e.GetPosition(itemsControl);
            if (!state.IsDragging)
            {
                var delta = position - state.StartPoint;
                if (Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance && Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance) return;
                if (!Begin(itemsControl, state)) return;
            }

            if (!state.IsDragging) return;
            Track(state, position.Y - state.StartPoint.Y);
            e.Handled = true;
        }

        private static void OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            var itemsControl = (ItemsControl)sender;
            var state = StateOf(sender);
            if (state.IsDragging)
            {
                Drop(itemsControl, state);
                e.Handled = true;
            }
            else if (!state.IsSettling)
            {
                state.SourceIndex = -1;
            }
        }

        private static void OnKeyDown(object sender, KeyEventArgs e)
        {
            var state = StateOf(sender);
            if (e.Key != Key.Escape || !state.IsDragging) return;
            Cancel((ItemsControl)sender, state);
            e.Handled = true;
        }

        private static void OnLostMouseCapture(object sender, MouseEventArgs e)
        {
            // The event bubbles: only the list itself losing the capture ends a drag, not an element inside it.
            if (e.OriginalSource != sender) return;
            var state = StateOf(sender);
            if (state != null && state.IsDragging && !state.ReleasingCapture) Cancel((ItemsControl)sender, state);
        }

        /// <summary>Snapshots the item slots and lifts the dragged item.</summary>
        private static bool Begin(ItemsControl itemsControl, DragState state)
        {
            state.Slots.Clear();
            for (var i = 0; i < itemsControl.Items.Count; i++)
            {
                if (!(itemsControl.ItemContainerGenerator.ContainerFromIndex(i) is FrameworkElement container) || !container.IsVisible)
                {
                    state.SourceIndex = -1;
                    return false;
                }

                var transform = new TranslateTransform();
                container.RenderTransform = transform;
                state.Slots.Add(new Slot
                {
                    Container = container,
                    Transform = transform,
                    Top = container.TranslatePoint(new Point(0, 0), itemsControl).Y,
                    Height = container.ActualHeight,
                });
            }

            if (state.SourceIndex >= state.Slots.Count)
            {
                Clear(state);
                return false;
            }

            var dragged = state.Slots[state.SourceIndex].Container;
            Panel.SetZIndex(dragged, 1);
            dragged.Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 3, Opacity = 0.55, Direction = 270 };
            state.Lifted = Lift(dragged);
            state.TargetIndex = state.SourceIndex;
            state.IsDragging = true;
            itemsControl.Focus();

            // Taking the capture raises a synthetic MouseMove; it is not a move of the dragged item.
            state.Capturing = true;
            try
            {
                itemsControl.CaptureMouse();
            }
            finally
            {
                state.Capturing = false;
            }

            return true;
        }

        /// <summary>Moves the dragged item with the mouse and slides the items it passes out of its way.</summary>
        private static void Track(DragState state, double offset)
        {
            var source = state.Slots[state.SourceIndex];
            var first = state.Slots[0];
            var last = state.Slots[state.Slots.Count - 1];

            // Stay within the list.
            offset = Math.Max(first.Top - source.Top, Math.Min(last.Top + last.Height - source.Height - source.Top, offset));
            source.Transform.BeginAnimation(TranslateTransform.YProperty, null);
            source.Transform.Y = offset;

            // The slot whose midpoint is nearest to the dragged item's center.
            var center = source.Top + offset + source.Height / 2;
            var target = 0;
            var best = double.MaxValue;
            for (var i = 0; i < state.Slots.Count; i++)
            {
                var distance = Math.Abs(state.Slots[i].Top + state.Slots[i].Height / 2 - center);
                if (distance < best)
                {
                    best = distance;
                    target = i;
                }
            }

            state.TargetIndex = TargetIndex(state.SourceIndex, target > state.SourceIndex ? target + 1 : target);
            for (var i = 0; i < state.Slots.Count; i++)
            {
                if (i == state.SourceIndex) continue;
                var shift = i > state.SourceIndex && i <= state.TargetIndex ? -source.Height
                    : i < state.SourceIndex && i >= state.TargetIndex ? source.Height
                    : 0;
                AnimateTo(state.Slots[i], shift, ShiftDuration);
            }
        }

        /// <summary>Slides the dragged item into its new slot, then reorders the collection.</summary>
        private static void Drop(ItemsControl itemsControl, DragState state)
        {
            var source = state.Slots[state.SourceIndex];
            var from = state.SourceIndex;
            var to = state.TargetIndex;

            // Top of the target slot once the other items have moved out of the way.
            var newTop = state.Slots[0].Top;
            var order = new List<int>();
            for (var i = 0; i < state.Slots.Count; i++) if (i != from) order.Add(i);
            order.Insert(to, from);
            foreach (var index in order)
            {
                if (index == from) break;
                newTop += state.Slots[index].Height;
            }

            EndDrag(itemsControl, state);
            state.IsSettling = true;
            AnimateTo(source, newTop - source.Top, SettleDuration, () =>
            {
                state.IsSettling = false;
                try
                {
                    MoveItem(itemsControl, from, to);
                }
                finally
                {
                    // The items now sit in their new slots: dropping the transforms doesn't move anything.
                    Clear(state);
                }
            });
        }

        /// <summary>Slides every item back to its original place.</summary>
        private static void Cancel(ItemsControl itemsControl, DragState state)
        {
            EndDrag(itemsControl, state);
            state.IsSettling = true;
            var slots = state.Slots.ToArray();
            for (var i = 0; i < slots.Length; i++)
            {
                AnimateTo(slots[i], 0, ShiftDuration, i == 0 ? () =>
                {
                    state.IsSettling = false;
                    Clear(state);
                } : (Action)null);
            }
        }

        private static void EndDrag(ItemsControl itemsControl, DragState state)
        {
            state.IsDragging = false;
            state.SourceIndex = -1;
            state.ReleasingCapture = true;
            try
            {
                if (itemsControl.IsMouseCaptured) itemsControl.ReleaseMouseCapture();
            }
            finally
            {
                state.ReleasingCapture = false;
            }
        }

        private static void Clear(DragState state)
        {
            foreach (var slot in state.Slots)
            {
                slot.Transform.BeginAnimation(TranslateTransform.YProperty, null);
                slot.Container.RenderTransform = null;
                slot.Container.ClearValue(Panel.ZIndexProperty);
                slot.Container.ClearValue(UIElement.EffectProperty);
            }

            state.Lifted?.Restore();
            state.Lifted = null;

            state.Slots.Clear();
        }

        private static void AnimateTo(Slot slot, double to, Duration duration, Action completed = null)
        {
            if (completed == null && slot.Target == to && slot.Animating) return;
            slot.Target = to;
            slot.Animating = true;
            var animation = new DoubleAnimation(to, duration) { EasingFunction = Ease };
            animation.Completed += (s, e) => completed?.Invoke();
            slot.Transform.BeginAnimation(TranslateTransform.YProperty, animation);
        }

        /// <summary>
        /// Rows usually have translucent backgrounds, which would let the rows below show through the dragged one.
        /// Gives the item's root border an opaque version of its background (slightly lighter, as it is lifted).
        /// </summary>
        private static LiftedBackground Lift(FrameworkElement container)
        {
            if (VisualTreeHelper.GetChildrenCount(container) == 0 || !(VisualTreeHelper.GetChild(container, 0) is Border border)) return null;

            var lifted = new LiftedBackground { Border = border, LocalValue = border.ReadLocalValue(Border.BackgroundProperty) };
            var color = border.Background is SolidColorBrush brush ? brush.Color : Colors.Transparent;
            byte Mix(byte page, byte own) => (byte)Math.Min(255, page + (own - page) * color.A / 255.0 + 8);
            var opaque = new SolidColorBrush(Color.FromRgb(Mix(PageColor.R, color.R), Mix(PageColor.G, color.G), Mix(PageColor.B, color.B)));
            opaque.Freeze();
            border.Background = opaque;
            return lifted;
        }

        private static bool IsOnHandle(DependencyObject element, ItemsControl root)
        {
            while (element != null && element != root)
            {
                if (GetHandle(element)) return true;
                element = element is Visual || element is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
            }

            return false;
        }

        private static T Freeze<T>(T freezable) where T : Freezable
        {
            freezable.Freeze();
            return freezable;
        }

        private sealed class DragState
        {
            public readonly List<Slot> Slots = new List<Slot>();
            public int SourceIndex = -1;
            public int TargetIndex;
            public Point StartPoint;
            public bool IsDragging;
            public LiftedBackground Lifted;

            /// <summary>Set while the drag takes the mouse capture.</summary>
            public bool Capturing;

            /// <summary>The dropped or cancelled item is sliding into place; new drags wait for it.</summary>
            public bool IsSettling;

            /// <summary>Set while the drag releases the mouse itself, so that is not taken for a lost capture.</summary>
            public bool ReleasingCapture;
        }

        /// <summary>The dragged item's root border and its background before the drag.</summary>
        private sealed class LiftedBackground
        {
            public Border Border;
            public object LocalValue;

            public void Restore()
            {
                if (LocalValue == DependencyProperty.UnsetValue) Border.ClearValue(Border.BackgroundProperty);
                else Border.SetValue(Border.BackgroundProperty, LocalValue);
            }
        }

        /// <summary>An item container and where it was when the drag started.</summary>
        private sealed class Slot
        {
            public FrameworkElement Container;
            public TranslateTransform Transform;
            public double Top;
            public double Height;
            public double Target;
            public bool Animating;
        }
    }
}
