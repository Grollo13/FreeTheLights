using System;
using System.Collections.Generic;
using System.Linq;
using FreeTheLights.Core;
using Newtonsoft.Json;

namespace FreeTheLights.Settings
{
    /// <summary>Kinds of LED groups. Values are explicit because they are stored in the settings; never renumber them.</summary>
    public enum LedGroupKind
    {
        HorizontalRow = 0,
        VerticalColumn = 1,
        Matrix = 2,

        /// <summary>A single-LED element on a wheel's front plate (button, light or dial).</summary>
        Element = 3,
    }

    /// <summary>What a front-plate element is. Values are explicit because they are stored in the settings; never renumber them.</summary>
    public enum ElementType
    {
        Button = 0,

        /// <summary>A light that is not a button (indicator, logo, ...).</summary>
        Light = 1,

        Dial = 2,
    }

    /// <summary>
    /// Where an input presses an element: a button's input, or one of a dial's two directions. Not stored (each slot has
    /// its own property in <see cref="LedGroup"/>), so the values may change.
    /// </summary>
    public enum InputSlot
    {
        Button = 0,
        Clockwise = 1,
        CounterClockwise = 2,
    }

    /// <summary>Sides of a wheel or dash a group can sit on. Values are explicit because they are stored in the settings; never renumber them.</summary>
    public enum LedGroupSide
    {
        None = 0,
        Left = 1,
        Right = 2,
    }

    /// <summary>
    /// A contiguous block of LEDs on a device's physical LED strip.
    ///
    /// Logical coordinates used by the features:
    ///  - Row:    index 0 is the leftmost LED.
    ///  - Column: index 0 is the bottom LED.
    ///  - Matrix: (x, y) with (0, 0) at the top-left.
    ///  - Element: a single LED (index 0) on a wheel's front plate, placed by <see cref="Side"/> and <see cref="ElementSlot"/>.
    /// <see cref="PhysicalIndex(int)"/> / <see cref="PhysicalIndex(int,int)"/> translate them to the device's LED index.
    /// </summary>
    public class LedGroup : ObservableObject
    {
        private Guid id = Guid.NewGuid();
        private string name = "Group";
        private LedGroupKind kind;
        private int count = 10;
        private int rows = 8;
        private int columns = 8;
        private int startIndex;
        private bool reversed;
        private bool serpentine;
        private bool startsAtBottom;
        private LedGroupSide side;
        private ElementType elementType;
        private int elementSlot;
        private string staticColor = string.Empty;
        private string pressColor = string.Empty;
        private string buttonInput = string.Empty;
        private string clockwiseInput = string.Empty;
        private string counterClockwiseInput = string.Empty;
        private double photoX;
        private double photoY;
        private double photoSize;
        private List<PhotoLed> photoLeds = new List<PhotoLed>();

        public Guid Id { get => id; set => Set(ref id, value); }

        public string Name { get => name; set => Set(ref name, value); }

        public LedGroupKind Kind
        {
            get => kind;
            set
            {
                if (Set(ref kind, value))
                {
                    OnPropertyChanged(nameof(LedCount));
                    OnPropertyChanged(nameof(IsMatrix));
                    OnPropertyChanged(nameof(IsElement));
                }
            }
        }

        /// <summary>Number of LEDs for rows and columns.</summary>
        public int Count
        {
            get => count;
            set
            {
                if (Set(ref count, Math.Max(1, Math.Min(512, value)))) OnPropertyChanged(nameof(LedCount));
            }
        }

        public int Rows
        {
            get => rows;
            set
            {
                if (Set(ref rows, Math.Max(1, Math.Min(64, value)))) OnPropertyChanged(nameof(LedCount));
            }
        }

        public int Columns
        {
            get => columns;
            set
            {
                if (Set(ref columns, Math.Max(1, Math.Min(64, value)))) OnPropertyChanged(nameof(LedCount));
            }
        }

        /// <summary>
        /// Index of the group's first wired LED within the device's LED strip (0-based), at most
        /// <see cref="LedLimits.MaxLeds"/>: every frame allocates arrays up to the highest index used.
        /// </summary>
        public int StartIndex { get => startIndex; set => Set(ref startIndex, Math.Max(0, Math.Min(LedLimits.MaxLeds, value))); }

        /// <summary>Rows: wiring starts at the right. Columns: wiring starts at the top. Matrix: wiring starts at the right of each line.</summary>
        public bool Reversed { get => reversed; set => Set(ref reversed, value); }

        /// <summary>Matrix only: every other line is wired in the opposite direction ("zig-zag").</summary>
        public bool Serpentine { get => serpentine; set => Set(ref serpentine, value); }

        /// <summary>Matrix only: wiring starts at the bottom line.</summary>
        public bool StartsAtBottom { get => startsAtBottom; set => Set(ref startsAtBottom, value); }

        /// <summary>Which side of the wheel/dash this group sits on (used by car proximity).</summary>
        public LedGroupSide Side { get => side; set => Set(ref side, value); }

        /// <summary>Element only: button, light or dial.</summary>
        public ElementType ElementType { get => elementType; set => Set(ref elementType, value); }

        /// <summary>Element only: position within its side (or the center, for dials) in the device's front plate layout.</summary>
        public int ElementSlot { get => elementSlot; set => Set(ref elementSlot, Math.Max(0, value)); }

        /// <summary>Element only: color shown when no effect is active ("#AARRGGBB"); empty = not lit.</summary>
        public string StaticColor { get => staticColor; set => Set(ref staticColor, value ?? string.Empty); }

        /// <summary>
        /// Buttons and dials: color shown while the input of one of its <see cref="InputSlots"/> is held ("#AARRGGBB", opaque black =
        /// off); empty = the element doesn't react to its inputs. Lights are never pressed.
        /// </summary>
        public string PressColor { get => pressColor; set => Set(ref pressColor, value ?? string.Empty); }

        /// <summary>Buttons: SimHub's name of the button's input, for example "JoystickPlugin.Wheel_B05"; empty = none.</summary>
        public string ButtonInput { get => buttonInput; set => Set(ref buttonInput, value ?? string.Empty); }

        /// <summary>Dials: SimHub's name of the input of a clockwise step; empty = none.</summary>
        public string ClockwiseInput { get => clockwiseInput; set => Set(ref clockwiseInput, value ?? string.Empty); }

        /// <summary>Dials: SimHub's name of the input of a counter-clockwise step; empty = none.</summary>
        public string CounterClockwiseInput { get => counterClockwiseInput; set => Set(ref counterClockwiseInput, value ?? string.Empty); }

        /// <summary>
        /// The input slots of the element's type: a button's one input, a dial's two directions, none for lights and
        /// other groups. The one place that says which inputs press an element; the renderer and the inspector both use
        /// it. Shared array: never modify it.
        /// </summary>
        [JsonIgnore]
        public IReadOnlyList<InputSlot> InputSlots
        {
            get
            {
                if (!IsElement) return NoInputSlots;
                switch (ElementType)
                {
                    case ElementType.Button: return ButtonInputSlots;
                    case ElementType.Dial: return DialInputSlots;
                    default: return NoInputSlots;
                }
            }
        }

        /// <summary>The element can be pressed: a button, or a dial (turned). Lights can't.</summary>
        [JsonIgnore]
        public bool CanBePressed => InputSlots.Count > 0;

        /// <summary>The input assigned to <paramref name="slot"/> (empty = none).</summary>
        public string GetInput(InputSlot slot)
        {
            switch (slot)
            {
                case InputSlot.Clockwise: return ClockwiseInput;
                case InputSlot.CounterClockwise: return CounterClockwiseInput;
                default: return ButtonInput;
            }
        }

        public void SetInput(InputSlot slot, string input)
        {
            switch (slot)
            {
                case InputSlot.Clockwise: ClockwiseInput = input; break;
                case InputSlot.CounterClockwise: CounterClockwiseInput = input; break;
                default: ButtonInput = input; break;
            }
        }

        private static readonly InputSlot[] ButtonInputSlots = { InputSlot.Button };
        private static readonly InputSlot[] DialInputSlots = { InputSlot.Clockwise, InputSlot.CounterClockwise };
        private static readonly InputSlot[] NoInputSlots = new InputSlot[0];

        /// <summary>
        /// Elements copied from a SimHub wheel layout: center of the element on the wheel photo, in the canvas units of
        /// <see cref="SimHubPreset.CanvasWidth"/>. Ignored when <see cref="PhotoSize"/> is 0.
        /// </summary>
        public double PhotoX { get => photoX; set => Set(ref photoX, value); }

        public double PhotoY { get => photoY; set => Set(ref photoY, value); }

        /// <summary>Diameter of the element on the wheel photo (canvas units); 0 = not placed on a photo.</summary>
        public double PhotoSize { get => photoSize; set => Set(ref photoSize, Math.Max(0, value)); }

        /// <summary>
        /// Rows and columns of a dashboard shown on its product picture: each LED's place on the picture (pixels of
        /// <see cref="SimHubPreset.CanvasWidth"/>), in logical order (rows from the left, columns from the bottom).
        /// Empty = not placed. Treated as immutable: changes assign a new list.
        /// </summary>
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<PhotoLed> PhotoLeds { get => photoLeds; set => Set(ref photoLeds, value ?? new List<PhotoLed>()); }

        /// <summary>The element's position comes from a SimHub layout, not from the front plate presets.</summary>
        [JsonIgnore]
        public bool HasPhotoPosition => IsElement && PhotoSize > 0;

        [JsonIgnore]
        public bool IsMatrix => Kind == LedGroupKind.Matrix;

        [JsonIgnore]
        public bool IsElement => Kind == LedGroupKind.Element;

        [JsonIgnore]
        public int LedCount => Kind == LedGroupKind.Matrix ? Rows * Columns : Kind == LedGroupKind.Element ? 1 : Count;

        [JsonIgnore]
        public int EndIndexExclusive => StartIndex + LedCount;

        [JsonIgnore]
        public int Width => Kind == LedGroupKind.Matrix ? Columns : Kind == LedGroupKind.HorizontalRow ? Count : 1;

        [JsonIgnore]
        public int Height => Kind == LedGroupKind.Matrix ? Rows : Kind == LedGroupKind.VerticalColumn ? Count : 1;

        /// <summary>Physical LED index of logical LED <paramref name="index"/> of a row or column.</summary>
        public int PhysicalIndex(int index)
        {
            if (Kind == LedGroupKind.Matrix)
            {
                return PhysicalIndex(index % Columns, index / Columns);
            }

            if (Kind == LedGroupKind.Element) return StartIndex;

            return StartIndex + (Reversed ? Count - 1 - index : index);
        }

        /// <summary>Physical LED index of matrix pixel (<paramref name="x"/>, <paramref name="y"/>), (0, 0) being top-left.</summary>
        public int PhysicalIndex(int x, int y)
        {
            if (Kind == LedGroupKind.Element) return StartIndex;

            if (Kind != LedGroupKind.Matrix)
            {
                return Kind == LedGroupKind.HorizontalRow ? PhysicalIndex(x) : PhysicalIndex(Count - 1 - y);
            }

            var line = StartsAtBottom ? Rows - 1 - y : y;
            var flip = Reversed;
            if (Serpentine && line % 2 == 1) flip = !flip;
            var col = flip ? Columns - 1 - x : x;
            return StartIndex + line * Columns + col;
        }

        /// <summary>Copies the group under a new <see cref="Id"/> (without copying event subscribers).</summary>
        public LedGroup Clone()
        {
            return new LedGroup
            {
                Name = Name,
                Kind = Kind,
                Count = Count,
                Rows = Rows,
                Columns = Columns,
                StartIndex = StartIndex,
                Reversed = Reversed,
                Serpentine = Serpentine,
                StartsAtBottom = StartsAtBottom,
                Side = Side,
                ElementType = ElementType,
                ElementSlot = ElementSlot,
                StaticColor = StaticColor,
                PressColor = PressColor,
                ButtonInput = ButtonInput,
                ClockwiseInput = ClockwiseInput,
                CounterClockwiseInput = CounterClockwiseInput,
                PhotoX = PhotoX,
                PhotoY = PhotoY,
                PhotoSize = PhotoSize,
                PhotoLeds = PhotoLeds.Select(l => l.Clone()).ToList(),
            };
        }
    }
}
