using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FreeTheLights.Settings;

namespace FreeTheLights.UI
{
    /// <summary>
    /// A dashboard's product picture with its rows and columns of LEDs (<see cref="LedGroup.PhotoLeds"/>), which the
    /// user drags into place. Dragging the first or last LED of a group stretches the group (see
    /// <see cref="DashPhotoLayouts.Move"/>); any other LED moves alone. One color per group, in wiring order.
    /// </summary>
    public class PhotoLedPlacer : FrameworkElement
    {
        private static readonly Pen Outline = Freeze(new Pen(Brushes.White, 1.5));
        private static readonly Pen EndOutline = Freeze(new Pen(Brushes.White, 2.5));
        private static readonly Pen HoverOutline = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0xD5, 0x4F)), 2.5));
        private static readonly Brush Shade = Freeze(new SolidColorBrush(Color.FromArgb(0x90, 0x10, 0x10, 0x12)));

        private readonly ToolTip toolTip = new ToolTip();
        private BitmapSource picture;
        private IList<LedGroup> groups = new List<LedGroup>();
        private (LedGroup group, int index)? hover;
        private (LedGroup group, int index)? drag;
        private Point dragStart;
        private List<PhotoLed> dragLeds;

        public PhotoLedPlacer()
        {
            ToolTip = toolTip;
            ToolTipService.SetInitialShowDelay(this, 300);
            Focusable = false;
        }

        /// <summary>Raised after the user moved LEDs.</summary>
        public event EventHandler PositionsChanged;

        /// <summary>The largest size the picture is drawn at (it shrinks to the available space).</summary>
        public double MaxPictureWidth { get; set; } = 720;

        /// <summary>Shows <paramref name="newPicture"/> with the LEDs of the rows and columns in <paramref name="newGroups"/>.</summary>
        public void Show(BitmapSource newPicture, IEnumerable<LedGroup> newGroups)
        {
            picture = newPicture;
            groups = newGroups?.Where(g => g.Kind == LedGroupKind.HorizontalRow || g.Kind == LedGroupKind.VerticalColumn).ToList() ?? new List<LedGroup>();
            hover = null;
            drag = null;
            InvalidateMeasure();
            InvalidateVisual();
        }

        /// <summary>Pixels of the picture per screen unit.</summary>
        private double Scale => picture == null || picture.PixelWidth == 0 ? 1 : ActualWidth / picture.PixelWidth;

        protected override Size MeasureOverride(Size availableSize)
        {
            if (picture == null || picture.PixelWidth == 0) return new Size(0, 0);
            var width = Math.Min(MaxPictureWidth, double.IsInfinity(availableSize.Width) ? MaxPictureWidth : availableSize.Width);
            var height = width * picture.PixelHeight / picture.PixelWidth;
            if (!double.IsInfinity(availableSize.Height) && height > availableSize.Height)
            {
                height = availableSize.Height;
                width = height * picture.PixelWidth / picture.PixelHeight;
            }

            return new Size(width, height);
        }

        protected override Size ArrangeOverride(Size finalSize) => DesiredSize;

        protected override void OnRender(DrawingContext dc)
        {
            if (picture == null) return;
            dc.DrawImage(picture, new Rect(0, 0, ActualWidth, ActualHeight));
            if (!IsEnabled)
            {
                dc.DrawRectangle(Shade, null, new Rect(0, 0, ActualWidth, ActualHeight));
                return;
            }

            var scale = Scale;
            for (var g = 0; g < groups.Count; g++)
            {
                var group = groups[g];
                var color = Theme.WiringColors[g % Theme.WiringColors.Length];
                var fill = new SolidColorBrush(Color.FromArgb(0xC0, color.R, color.G, color.B));
                fill.Freeze();
                var leds = group.PhotoLeds;
                for (var i = 0; i < leds.Count; i++)
                {
                    var rect = Bounds(leds[i], scale);
                    var isEnd = i == 0 || i == leds.Count - 1;
                    var pen = hover.HasValue && hover.Value.group == group && hover.Value.index == i ? HoverOutline : isEnd ? EndOutline : Outline;
                    var radius = Math.Min(rect.Width, rect.Height) / 2;
                    dc.DrawRoundedRectangle(fill, pen, rect, radius, radius);
                }
            }
        }

        private static Rect Bounds(PhotoLed led, double scale)
        {
            var w = Math.Max(8, led.Width * scale);
            var h = Math.Max(8, led.Height * scale);
            return new Rect(led.X * scale - w / 2, led.Y * scale - h / 2, w, h);
        }

        /// <summary>The LED under <paramref name="p"/> (screen units), the closest one when several overlap.</summary>
        private (LedGroup group, int index)? HitTest(Point p)
        {
            var scale = Scale;
            (LedGroup, int)? best = null;
            var bestDistance = double.MaxValue;
            foreach (var group in groups)
            {
                for (var i = 0; i < group.PhotoLeds.Count; i++)
                {
                    var rect = Bounds(group.PhotoLeds[i], scale);
                    rect.Inflate(3, 3);
                    if (!rect.Contains(p)) continue;
                    var center = new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
                    var distance = (center - p).Length;
                    if (distance < bestDistance)
                    {
                        best = (group, i);
                        bestDistance = distance;
                    }
                }
            }

            return best;
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            var hit = HitTest(e.GetPosition(this));
            if (hit == null) return;
            drag = hit;
            dragStart = e.GetPosition(this);
            dragLeds = hit.Value.group.PhotoLeds.Select(l => l.Clone()).ToList();
            toolTip.IsOpen = false;
            CaptureMouse();
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var p = e.GetPosition(this);
            if (drag.HasValue && IsMouseCaptured)
            {
                var scale = Scale;
                var dx = Clamp(p.X, 0, ActualWidth) - dragStart.X;
                var dy = Clamp(p.Y, 0, ActualHeight) - dragStart.Y;
                drag.Value.group.PhotoLeds = DashPhotoLayouts.Move(dragLeds, drag.Value.index, dx / scale, dy / scale);
                InvalidateVisual();
                return;
            }

            var hit = HitTest(p);
            if (!Equals(hit, hover))
            {
                hover = hit;
                InvalidateVisual();
            }

            Cursor = hit.HasValue ? Cursors.SizeAll : null;
            if (hit.HasValue)
            {
                var (group, index) = hit.Value;
                var isEnd = index == 0 || index == group.PhotoLeds.Count - 1;
                toolTip.Content = $"{group.Name}: LED {index + 1} of {group.PhotoLeds.Count}" + (isEnd && group.PhotoLeds.Count > 2 ? " (drag to stretch the group)" : string.Empty);
                toolTip.IsOpen = true;
            }
            else
            {
                toolTip.IsOpen = false;
            }
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            if (!drag.HasValue) return;
            drag = null;
            ReleaseMouseCapture();
            PositionsChanged?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnLostMouseCapture(MouseEventArgs e)
        {
            base.OnLostMouseCapture(e);
            if (!drag.HasValue) return;
            drag = null;
            PositionsChanged?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            toolTip.IsOpen = false;
            if (hover.HasValue && !drag.HasValue)
            {
                hover = null;
                InvalidateVisual();
            }
        }

        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            if (e.Property == IsEnabledProperty) InvalidateVisual();
        }

        private static double Clamp(double value, double min, double max) => Math.Max(min, Math.Min(max, value));

        private static T Freeze<T>(T freezable) where T : Freezable
        {
            freezable.Freeze();
            return freezable;
        }
    }
}
