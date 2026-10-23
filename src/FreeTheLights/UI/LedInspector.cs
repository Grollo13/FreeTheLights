using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using FreeTheLights.Core;
using FreeTheLights.Settings;

namespace FreeTheLights.UI
{
    /// <summary>What the LED inspector shows.</summary>
    public enum InspectorMode
    {
        /// <summary>Nothing is selected.</summary>
        None = 0,

        /// <summary>One or more front plate elements (buttons, lights, dials).</summary>
        Elements = 1,

        /// <summary>A whole row, column or matrix.</summary>
        Group = 2,
    }

    /// <summary>
    /// What is selected in the device preview and what the inspector panel next to it edits. Clicking a front plate
    /// element selects it (Ctrl/Shift-click adds or removes elements); clicking an LED of a row, column or matrix
    /// selects that whole group. Edits write through to every selected element.
    /// </summary>
    public class LedInspector : ObservableObject, IDisposable
    {
        private static readonly ReadOnlyCollection<LedGroup> NoElements = new ReadOnlyCollection<LedGroup>(new LedGroup[0]);
        private static readonly ReadOnlyCollection<GroupFeatureOption> NoFeatures = new ReadOnlyCollection<GroupFeatureOption>(new GroupFeatureOption[0]);
        private static readonly ElementType[] ButtonOrLight = { ElementType.Button, ElementType.Light };

        private DevicePart part;
        private InspectorMode mode;
        private ReadOnlyCollection<LedGroup> elements = NoElements;
        private LedGroup group;
        private int clickedLogical = -1;
        private ReadOnlyCollection<GroupFeatureOption> features = NoFeatures;
        private bool editing;
        private bool attached = true;
        private InputState inputSource;
        private LedGroup capturing;
        private InputSlot capturingSlot;

        // The running capture: its token, and its handler of the input source's presses.
        private object captureSession;
        private Action<string> captureHandler;

        /// <summary>Raised when the selection (not just an edited value) changed.</summary>
        public event EventHandler SelectionChanged;

        // ---------- Selection ----------

        /// <summary>
        /// A click in the preview. <paramref name="group"/> is the group under the mouse (null = empty space),
        /// <paramref name="logical"/> the logical LED index within it, <paramref name="add"/> whether Ctrl or Shift was held.
        /// </summary>
        public void Select(DevicePart part, LedGroup group, int logical, bool add)
        {
            if (part == null || group == null)
            {
                // Adding "nothing" to the selection changes nothing.
                if (!add) Apply(part ?? this.part, null, null, -1);
                return;
            }

            if (!part.Groups.Contains(group)) return;

            if (!group.IsElement)
            {
                Apply(part, null, group, logical >= 0 && logical < group.LedCount ? logical : -1);
                return;
            }

            if (add && mode == InspectorMode.Elements && part == this.part)
            {
                var toggled = elements.ToList();
                if (!toggled.Remove(group)) toggled.Add(group);
                Apply(part, toggled, null, -1);
                return;
            }

            Apply(part, new[] { group }, null, -1);
        }

        /// <summary>Clears the selection (the part stays, so the quick select buttons keep working).</summary>
        public void Clear() => Apply(part, null, null, -1);

        /// <summary>Selects all elements of the part, or all of one type.</summary>
        public void SelectAll(DevicePart part, ElementType? type)
        {
            var selected = part?.ElementsInWiringOrder().Where(e => !type.HasValue || e.ElementType == type.Value).ToList();
            Apply(part ?? this.part, selected, null, -1);
        }

        /// <summary>Switches to another part (for example when the part switcher changes); the selection is cleared.</summary>
        public void SetPart(DevicePart part) => Apply(part, null, null, -1);

        /// <summary>Raises PropertyChanged for everything, for changes made outside the inspector (for example an effect set on another tab).</summary>
        public void Refresh() => RaiseAll();

        // ---------- State ----------

        public InspectorMode Mode => mode;

        public bool IsNone => mode == InspectorMode.None;

        public bool IsElements => mode == InspectorMode.Elements;

        public bool IsGroup => mode == InspectorMode.Group;

        /// <summary>The part of the last click or <see cref="SelectAll"/>; kept while nothing is selected.</summary>
        public DevicePart Part => part;

        /// <summary>The selected elements (empty unless <see cref="Mode"/> is Elements).</summary>
        public IReadOnlyList<LedGroup> Elements => elements;

        /// <summary>The selected row, column or matrix (null unless <see cref="Mode"/> is Group).</summary>
        public LedGroup Group => group;

        /// <summary>Logical index of the LED clicked in <see cref="Group"/>, or -1.</summary>
        public int ClickedLogical => mode == InspectorMode.Group && group != null && clickedLogical >= 0 && clickedLogical < group.LedCount ? clickedLogical : -1;

        /// <summary>One element: its name; several: "6 elements"; a group: its name.</summary>
        public string Title
        {
            get
            {
                switch (mode)
                {
                    case InspectorMode.Elements: return elements.Count == 1 ? elements[0].Name : elements.Count + " elements";
                    case InspectorMode.Group: return group.Name;
                    default: return string.Empty;
                }
            }
        }

        /// <summary>One element: "button · left side · LED #4"; several: "4 buttons, 2 lights"; a group: "LED 5 of 15 · LED #9 on the strip".</summary>
        public string Subtitle
        {
            get
            {
                switch (mode)
                {
                    case InspectorMode.Elements: return elements.Count == 1 ? SingleSubtitle(elements[0]) : CountSummary(elements);
                    case InspectorMode.Group: return GroupSubtitle();
                    default: return string.Empty;
                }
            }
        }

        private static string SingleSubtitle(LedGroup element)
        {
            return $"{element.ElementType.ToString().ToLowerInvariant()} · {SideText(element.Side)} · LED #{element.StartIndex + 1}";
        }

        private static string SideText(LedGroupSide side) => side == LedGroupSide.Left ? "left side" : side == LedGroupSide.Right ? "right side" : "center";

        private static string CountSummary(IEnumerable<LedGroup> selected)
        {
            var parts = new List<string>();
            foreach (var type in new[] { ElementType.Button, ElementType.Light, ElementType.Dial })
            {
                var count = selected.Count(e => e.ElementType == type);
                if (count > 0) parts.Add($"{count} {type.ToString().ToLowerInvariant()}{(count == 1 ? string.Empty : "s")}");
            }

            return string.Join(", ", parts);
        }

        private string GroupSubtitle()
        {
            var clicked = ClickedLogical;
            if (clicked >= 0) return $"LED {clicked + 1} of {group.LedCount} · device LED #{group.PhysicalIndex(clicked) + 1}";
            if (group.LedCount == 1) return $"1 LED · LED #{group.StartIndex + 1}";
            return $"{group.LedCount} LEDs · LEDs #{group.StartIndex + 1}-#{group.EndIndexExclusive}";
        }

        // ---------- Element editing ----------

        public bool IsSingle => mode == InspectorMode.Elements && elements.Count == 1;

        /// <summary>The name of the single selected element (null when several are selected).</summary>
        public string Name
        {
            get => IsSingle ? elements[0].Name : null;
            set
            {
                if (!IsSingle) return;
                Edit(() => elements[0].Name = value ?? string.Empty);
            }
        }

        /// <summary>Button or light; null when the selected elements differ. Setting it changes every selected element that is not a dial.</summary>
        public ElementType? Type
        {
            get
            {
                if (mode != InspectorMode.Elements) return null;
                var first = elements[0].ElementType;
                return elements.All(e => e.ElementType == first) ? first : (ElementType?)null;
            }
            set
            {
                if (mode != InspectorMode.Elements || !value.HasValue || value.Value == ElementType.Dial) return;

                // The input slots change with the type: a running capture would assign to a slot that is gone.
                CancelCapture();
                Edit(() =>
                {
                    foreach (var element in elements)
                    {
                        if (element.ElementType != ElementType.Dial) element.ElementType = value.Value;
                    }
                });
            }
        }

        /// <summary>Button or light; dials stay dials.</summary>
        public ElementType[] TypeChoices => ButtonOrLight;

        public bool CanChangeType => mode == InspectorMode.Elements && elements.All(e => e.ElementType != ElementType.Dial);

        /// <summary>The type field is shown unless only dials are selected (they have no type to choose); with some dials among them it is read-only.</summary>
        public bool ShowsType => mode == InspectorMode.Elements && elements.Any(e => e.ElementType != ElementType.Dial);

        /// <summary>The LED number (0-based start index) of the single selected element; 0 when several are selected.</summary>
        public int LedIndex
        {
            get => IsSingle ? elements[0].StartIndex : 0;
            set
            {
                if (!CanChangeIndex) return;
                Edit(() => elements[0].StartIndex = value);
            }
        }

        /// <summary>One element that is not fixed by a SimHub layout.</summary>
        public bool CanChangeIndex => IsSingle && !elements[0].HasPhotoPosition;

        /// <summary>Where the single selected element sits, e.g. "outer column, row 2" (empty when several are selected).</summary>
        public string Position => IsSingle ? ElementPosition(part, elements[0]) : string.Empty;

        /// <summary>Color shown when no effect is active ("#AARRGGBB", empty = none); null when the selected elements differ.</summary>
        public string StaticColor
        {
            get
            {
                if (mode != InspectorMode.Elements) return null;
                return IsColorMixed ? null : elements[0].StaticColor ?? string.Empty;
            }
            set
            {
                if (mode != InspectorMode.Elements || value == null) return;
                Edit(() =>
                {
                    foreach (var element in elements) element.StaticColor = value;
                });
            }
        }

        public bool IsColorMixed => mode == InspectorMode.Elements && elements.Any(e => !SameColor(e.StaticColor, elements[0].StaticColor));

        private static bool SameColor(string a, string b) => string.Equals(a ?? string.Empty, b ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        /// <summary>The effect of the selected elements; null when they differ. Setting it gives every selected element exactly that effect.</summary>
        public EffectChoice Effect
        {
            get
            {
                if (mode != InspectorMode.Elements || IsEffectMixed) return null;
                return EffectChoice.For(part.GetElementEffect(elements[0]));
            }
            set
            {
                if (mode != InspectorMode.Elements || value == null) return;
                Edit(() =>
                {
                    foreach (var element in elements)
                    {
                        if (part.GetElementEffect(element) != value.Kind) part.SetElementEffect(element, value.Kind);
                    }
                });
            }
        }

        public EffectChoice[] EffectChoices => EffectChoice.All;

        public bool IsEffectMixed
        {
            get
            {
                if (mode != InspectorMode.Elements) return false;
                var first = part.GetElementEffect(elements[0]);
                return elements.Any(e => part.GetElementEffect(e) != first);
            }
        }

        // ---------- Press ----------

        /// <summary>
        /// Color shown while the element is pressed ("#AARRGGBB", empty = no reaction); null when the selected elements
        /// differ or can't be pressed.
        /// </summary>
        public string PressColor
        {
            get
            {
                if (!CanPress) return null;
                return IsPressColorMixed ? null : elements[0].PressColor ?? string.Empty;
            }
            set
            {
                if (!CanPress || value == null) return;
                Edit(() =>
                {
                    foreach (var element in elements) element.PressColor = value;
                });
            }
        }

        public bool IsPressColorMixed => CanPress && elements.Any(e => !SameColor(e.PressColor, elements[0].PressColor));

        /// <summary>Every selected element can be pressed (buttons and dials; lights can't): the press settings apply.</summary>
        public bool CanPress => mode == InspectorMode.Elements && elements.All(e => e.CanBePressed);

        /// <summary>Where inputs are captured from (SimHub's inputs, see <see cref="FreeTheLightsPlugin.Inputs"/>); null = inputs can't be assigned.</summary>
        public InputState InputSource
        {
            get => inputSource;
            set
            {
                if (inputSource == value) return;
                CancelCapture();
                inputSource = value;
                OnPropertyChanged(nameof(CanAssignInput));
                OnPropertyChanged(nameof(InputSlots));
            }
        }

        /// <summary>
        /// Runs a captured input on the UI thread (inputs arrive on SimHub's threads). The settings page sets it to its
        /// dispatcher; by default the input is handled where it arrives.
        /// </summary>
        public Action<Action> Post { get; set; } = action => action();

        /// <summary>
        /// The input slots of the single selected element: one for a button, clockwise and counter-clockwise for a dial,
        /// none for a light or when several elements are selected.
        /// </summary>
        public IReadOnlyList<InputSlotItem> InputSlots
        {
            get
            {
                if (!IsSingle) return new InputSlotItem[0];
                var element = elements[0];
                return element.InputSlots.Select(slot => new InputSlotItem(slot, element.GetInput(slot), capturing == element && capturingSlot == slot, CanAssignInput)).ToList();
            }
        }

        public bool HasInputSlots => IsSingle && elements[0].CanBePressed;

        /// <summary>Inputs are assigned to one button or dial at a time, and only with SimHub's inputs available.</summary>
        public bool CanAssignInput => IsSingle && CanPress && inputSource != null;

        /// <summary>Waiting for the input to assign to one of the selected element's slots.</summary>
        public bool IsCapturing => capturing != null;

        /// <summary>The next input SimHub reports becomes the selected element's input for <paramref name="slot"/> (replacing the one it had).</summary>
        public void StartCapture(InputSlot slot)
        {
            if (!CanAssignInput || !elements[0].InputSlots.Contains(slot)) return;
            CancelCapture();
            capturing = elements[0];
            capturingSlot = slot;

            // The input reaches the UI thread later (Post): by then this capture may have ended and another started, which
            // must not get it. The session token tells them apart.
            var session = new object();
            captureSession = session;
            captureHandler = input => Post(() => Captured(session, input));
            inputSource.Pressed += captureHandler;
            RaiseCapture();
        }

        public void CancelCapture()
        {
            if (capturing == null) return;
            if (inputSource != null) inputSource.Pressed -= captureHandler;
            capturing = null;
            captureSession = null;
            captureHandler = null;
            RaiseCapture();
        }

        /// <summary>Removes the selected element's input for <paramref name="slot"/>.</summary>
        public void ClearInput(InputSlot slot)
        {
            if (!IsSingle) return;
            var element = elements[0];
            if (capturing == element && capturingSlot == slot) CancelCapture();
            if (string.IsNullOrEmpty(element.GetInput(slot))) return;
            Edit(() => element.SetInput(slot, string.Empty));
        }

        private void Captured(object session, string input)
        {
            var element = capturing;
            var slot = capturingSlot;
            if (element == null || session != captureSession) return;
            CancelCapture();

            // The element may have become a light (or a button) meanwhile.
            if (string.IsNullOrEmpty(input) || !element.InputSlots.Contains(slot)) return;
            Edit(() =>
            {
                element.SetInput(slot, input);

                // One input turns a dial one way only.
                foreach (var other in element.InputSlots)
                {
                    if (other != slot && string.Equals(element.GetInput(other), input, StringComparison.OrdinalIgnoreCase)) element.SetInput(other, string.Empty);
                }
            });
        }

        private void RaiseCapture()
        {
            OnPropertyChanged(nameof(IsCapturing));
            OnPropertyChanged(nameof(InputSlots));
        }

        // ---------- Group editing ----------

        /// <summary>The features that can drive the selected group (RPM only offers rows), each with a "drives this group" check box.</summary>
        public IReadOnlyList<GroupFeatureOption> Features => features;

        // ---------- Quick select ----------

        public bool HasElements => part != null && part.Elements.Any();

        public bool HasButtons => HasElementsOfType(ElementType.Button);

        public bool HasLights => HasElementsOfType(ElementType.Light);

        public bool HasDials => HasElementsOfType(ElementType.Dial);

        private bool HasElementsOfType(ElementType type) => part != null && part.Elements.Any(e => e.ElementType == type);

        // ---------- Element position ----------

        /// <summary>Where an element sits, e.g. "outer column, row 2", "row 1, dial 2" or (SimHub layouts) "left side".</summary>
        public static string ElementPosition(DevicePart part, LedGroup element)
        {
            if (element == null) return "-";
            if (element.HasPhotoPosition) return SideText(element.Side);
            if (part == null) return "-";

            try
            {
                if (element.Side == LedGroupSide.None)
                {
                    var (row, index, length) = FrontPlatePresets.DialCell(FrontPlatePresets.FindDials(part.FrontPlate.DialPresetId), element.ElementSlot);
                    return length == 1 ? $"row {row + 1}" : $"row {row + 1}, {index + 1} of {length}";
                }

                var sides = FrontPlatePresets.FindSides(part.FrontPlate.SidePresetId);
                var (column, sideRow) = FrontPlatePresets.SideCell(sides, element.ElementSlot);
                var columnName = sides.Columns == 1 ? "column" : column == 0 ? "outer column" : column == sides.Columns - 1 ? "inner column" : $"column {column + 1}";
                return $"{columnName}, row {sideRow + 1}";
            }
            catch (ArgumentOutOfRangeException)
            {
                return "-";
            }
        }

        public void Dispose() => Detach();

        /// <summary>
        /// Stops following the part and the selected LEDs (the settings page was unloaded); they live as long as the plugin.
        /// The selection is kept for <see cref="Attach"/>.
        /// </summary>
        public void Detach()
        {
            if (!attached) return;
            attached = false;
            CancelCapture();
            foreach (var selected in SelectedGroups()) selected.PropertyChanged -= Selected_PropertyChanged;
            if (part != null) part.Groups.CollectionChanged -= Groups_CollectionChanged;
        }

        /// <summary>Follows the part and the selected LEDs again, and refreshes everything shown.</summary>
        public void Attach()
        {
            if (attached) return;
            attached = true;
            foreach (var selected in SelectedGroups()) selected.PropertyChanged += Selected_PropertyChanged;
            if (part != null)
            {
                part.Groups.CollectionChanged += Groups_CollectionChanged;

                // LEDs removed meanwhile leave the selection.
                Groups_CollectionChanged(part.Groups, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
            }

            RebuildFeatures();
            RaiseAll();
        }

        // ---------- Internals ----------

        private IEnumerable<LedGroup> SelectedGroups()
        {
            if (mode == InspectorMode.Group) return new[] { group };
            return elements;
        }

        /// <summary>Replaces the part and the selection (elements or a group, not both); raises the change notifications.</summary>
        private void Apply(DevicePart newPart, IEnumerable<LedGroup> newElements, LedGroup newGroup, int newLogical)
        {
            var list = newGroup != null || newElements == null ? new List<LedGroup>() : newElements.ToList();
            var newMode = newGroup != null ? InspectorMode.Group : list.Count > 0 ? InspectorMode.Elements : InspectorMode.None;

            var partChanged = newPart != part;
            var selectionChanged = newMode != mode || newGroup != group || (newMode == InspectorMode.Group && newLogical != clickedLogical) || !list.SequenceEqual(elements);
            if (!partChanged && !selectionChanged) return;

            CancelCapture();
            foreach (var selected in SelectedGroups()) selected.PropertyChanged -= Selected_PropertyChanged;
            if (partChanged)
            {
                if (part != null) part.Groups.CollectionChanged -= Groups_CollectionChanged;
                part = newPart;
                if (part != null && attached) part.Groups.CollectionChanged += Groups_CollectionChanged;
            }

            mode = newMode;
            elements = list.Count == 0 ? NoElements : new ReadOnlyCollection<LedGroup>(list);
            group = newGroup;
            clickedLogical = newGroup != null ? newLogical : -1;
            if (attached)
            {
                foreach (var selected in SelectedGroups()) selected.PropertyChanged += Selected_PropertyChanged;
            }

            RebuildFeatures();
            RaiseAll();
            if (selectionChanged) SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        private void RebuildFeatures()
        {
            if (mode != InspectorMode.Group || part == null)
            {
                features = NoFeatures;
                return;
            }

            var options = new List<GroupFeatureOption>();
            foreach (var info in DevicePart.AllFeatures)
            {
                var settings = info.SettingsOf(part);
                if (settings != null && settings.AllowedKinds.Contains(group.Kind)) options.Add(new GroupFeatureOption(info.Kind, settings, group));
            }

            features = new ReadOnlyCollection<GroupFeatureOption>(options);
        }

        /// <summary>Runs an edit of the selected elements without reacting to their own change notifications, then refreshes every element property.</summary>
        private void Edit(Action edit)
        {
            editing = true;
            try
            {
                edit();
            }
            finally
            {
                editing = false;
            }

            RaiseElementProperties();
            RaiseQuickSelect();
        }

        private void Groups_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            // Removed groups leave the selection (also covers Replace and Reset).
            if (mode == InspectorMode.Group)
            {
                if (!part.Groups.Contains(group))
                {
                    Apply(part, null, null, -1);
                    return;
                }
            }
            else if (mode == InspectorMode.Elements)
            {
                var kept = elements.Where(part.Groups.Contains).ToList();
                if (kept.Count != elements.Count)
                {
                    Apply(part, kept, null, -1);
                    return;
                }
            }

            RaiseQuickSelect();
        }

        private void Selected_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (editing) return;

            if (mode == InspectorMode.Group)
            {
                OnPropertyChanged(nameof(Title));
                OnPropertyChanged(nameof(Subtitle));
                OnPropertyChanged(nameof(ClickedLogical));
                if (e.PropertyName == nameof(LedGroup.Kind))
                {
                    RebuildFeatures();
                    OnPropertyChanged(nameof(Features));
                }

                return;
            }

            RaiseElementProperties();
            RaiseQuickSelect();
        }

        private void RaiseElementProperties()
        {
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(Subtitle));
            OnPropertyChanged(nameof(IsSingle));
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(Type));
            OnPropertyChanged(nameof(TypeChoices));
            OnPropertyChanged(nameof(CanChangeType));
            OnPropertyChanged(nameof(ShowsType));
            OnPropertyChanged(nameof(LedIndex));
            OnPropertyChanged(nameof(CanChangeIndex));
            OnPropertyChanged(nameof(Position));
            OnPropertyChanged(nameof(StaticColor));
            OnPropertyChanged(nameof(IsColorMixed));
            OnPropertyChanged(nameof(Effect));
            OnPropertyChanged(nameof(EffectChoices));
            OnPropertyChanged(nameof(IsEffectMixed));
            OnPropertyChanged(nameof(PressColor));
            OnPropertyChanged(nameof(IsPressColorMixed));
            OnPropertyChanged(nameof(CanPress));
            OnPropertyChanged(nameof(InputSlots));
            OnPropertyChanged(nameof(HasInputSlots));
            OnPropertyChanged(nameof(CanAssignInput));
        }

        private void RaiseQuickSelect()
        {
            OnPropertyChanged(nameof(HasElements));
            OnPropertyChanged(nameof(HasButtons));
            OnPropertyChanged(nameof(HasLights));
            OnPropertyChanged(nameof(HasDials));
        }

        private void RaiseAll()
        {
            OnPropertyChanged(nameof(Mode));
            OnPropertyChanged(nameof(IsNone));
            OnPropertyChanged(nameof(IsElements));
            OnPropertyChanged(nameof(IsGroup));
            OnPropertyChanged(nameof(Part));
            OnPropertyChanged(nameof(Elements));
            OnPropertyChanged(nameof(Group));
            OnPropertyChanged(nameof(ClickedLogical));
            OnPropertyChanged(nameof(Features));
            RaiseElementProperties();
            RaiseQuickSelect();
        }
    }

    /// <summary>One input slot of the selected element, as the inspector shows it.</summary>
    public sealed class InputSlotItem
    {
        public InputSlotItem(InputSlot slot, string name, bool isCapturing, bool canAssign)
        {
            Slot = slot;
            Name = name ?? string.Empty;
            IsCapturing = isCapturing;
            CanAssign = canAssign;
        }

        public InputSlot Slot { get; }

        /// <summary>SimHub's input name, e.g. "JoystickPlugin.Wheel_B05"; empty = not assigned.</summary>
        public string Name { get; }

        public bool IsAssigned => Name.Length > 0;

        public bool IsCapturing { get; }

        /// <summary>SimHub's inputs are available, so the slot can be assigned.</summary>
        public bool CanAssign { get; }

        public string Label => Slot == InputSlot.Clockwise ? "Turn clockwise" : Slot == InputSlot.CounterClockwise ? "Turn counter-clockwise" : "Button";

        /// <summary>The input without its plugin's prefix (e.g. "Wheel_B05"), "Not assigned", or what to do while capturing.</summary>
        public string Text
        {
            get
            {
                if (IsCapturing) return Slot == InputSlot.Button ? "Press the button now…" : Slot == InputSlot.Clockwise ? "Turn the dial one step clockwise now…" : "Turn the dial one step counter-clockwise now…";
                if (!IsAssigned) return "Not assigned";
                return InputState.DisplayName(Name);
            }
        }

        public string AssignText => IsAssigned ? "Change" : "Assign";
    }

    /// <summary>One check box of the group inspector: does this feature drive the selected group?</summary>
    public class GroupFeatureOption : ObservableObject
    {
        private readonly FeatureSettingsBase feature;
        private readonly LedGroup group;

        public GroupFeatureOption(LayerKind kind, FeatureSettingsBase feature, LedGroup group)
        {
            Kind = kind;
            this.feature = feature;
            this.group = group;
        }

        public LayerKind Kind { get; }

        public string Name => DevicePart.FindFeature(Kind)?.DisplayName ?? Kind.ToString();

        public bool IsChecked
        {
            get => feature.TargetGroupIds.Contains(group.Id);
            set
            {
                if (value == IsChecked) return;
                if (value) feature.TargetGroupIds.Add(group.Id);
                else feature.TargetGroupIds.Remove(group.Id);
                OnPropertyChanged();
            }
        }
    }
}
