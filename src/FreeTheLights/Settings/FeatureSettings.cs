using System;
using System.Collections.ObjectModel;
using System.Linq;
using FreeTheLights.Core;
using Newtonsoft.Json;

namespace FreeTheLights.Settings
{
    /// <summary>
    /// Common part of every feature: on/off and the LED groups it drives.
    /// </summary>
    public abstract class FeatureSettingsBase : ObservableObject
    {
        private bool enabled = true;

        public bool Enabled { get => enabled; set => Set(ref enabled, value); }

        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public ObservableCollection<Guid> TargetGroupIds { get; set; } = new ObservableCollection<Guid>();

        protected static readonly LedGroupKind[] RowKinds = { LedGroupKind.HorizontalRow };
        protected static readonly LedGroupKind[] ColumnKinds = { LedGroupKind.VerticalColumn };
        protected static readonly LedGroupKind[] ElementKinds = { LedGroupKind.Element };

        /// <summary>Every group kind; front-plate elements included (an element shows at most one effect, see <see cref="DevicePart.SetElementEffect"/>).</summary>
        protected static readonly LedGroupKind[] AllKinds = { LedGroupKind.HorizontalRow, LedGroupKind.VerticalColumn, LedGroupKind.Matrix, LedGroupKind.Element };

        /// <summary>Group kinds this feature may drive. Shared array: never modify it.</summary>
        [JsonIgnore]
        public abstract LedGroupKind[] AllowedKinds { get; }

        /// <summary>Group kinds that are selected automatically when a group is added to the device. Shared array: never modify it.</summary>
        [JsonIgnore]
        public abstract LedGroupKind[] DefaultKinds { get; }

        public virtual bool Targets(LedGroup group) => TargetGroupIds.Contains(group.Id) && AllowedKinds.Contains(group.Kind);
    }

    /// <summary>What the RPM lights show without car data. Values are explicit because they are stored in the settings; never renumber them.</summary>
    public enum RpmFallbackMode
    {
        /// <summary>Generic green → yellow → red bar when no car data is available.</summary>
        Generic = 0,

        /// <summary>Leave the LEDs dark when no car data is available.</summary>
        Off = 1,
    }

    public class RpmSettings : FeatureSettingsBase
    {
        private RpmFallbackMode fallbackMode = RpmFallbackMode.Generic;
        private int genericStartPercent = 70;
        private int genericBlinkIntervalMs = 120;

        public override LedGroupKind[] AllowedKinds => RowKinds;

        public override LedGroupKind[] DefaultKinds => RowKinds;

        public RpmFallbackMode FallbackMode { get => fallbackMode; set => Set(ref fallbackMode, value); }

        /// <summary>Generic fallback: percentage of the redline at which the first LED lights up.</summary>
        public int GenericStartPercent { get => genericStartPercent; set => Set(ref genericStartPercent, Math.Max(0, Math.Min(99, value))); }

        /// <summary>Generic fallback: blink interval at redline, 0 = no blinking.</summary>
        public int GenericBlinkIntervalMs { get => genericBlinkIntervalMs; set => Set(ref genericBlinkIntervalMs, Math.Max(0, value)); }
    }

    /// <summary>Race control flags. Values are explicit because they are stored in the settings (3 was the removed black/white flag).</summary>
    public enum FlagKind
    {
        Yellow = 0,
        Blue = 1,
        White = 2,
        Black = 4,
        Checkered = 5,
        Green = 6,
        Orange = 7,

        /// <summary>Debris on track: yellow with red stripes. Only iRacing reports it.</summary>
        Debris = 8,
    }

    /// <summary>How the two colors of a two-color flag are arranged. Single-color flags are always solid.</summary>
    public enum FlagPattern
    {
        /// <summary>Rows/columns: LED i uses color 1 when i is even, color 2 otherwise. Matrices: 1-pixel checkerboard.</summary>
        Alternate = 1,

        /// <summary>Matrices: checkerboard with larger cells. Rows/columns: same as <see cref="Alternate"/>.</summary>
        Checker = 2,

        /// <summary>Matrices: color 1 in the upper-left triangle, color 2 in the lower-right. Rows/columns: first half / second half.</summary>
        Diagonal = 3,
    }

    /// <summary>How a flag animates. Values are explicit because they are stored in the settings; never renumber them.</summary>
    public enum FlagAnimation
    {
        /// <summary>Colors stay as they are.</summary>
        Steady = 0,

        /// <summary>The flag blinks on and off.</summary>
        Blink = 1,

        /// <summary>Two-color flags: every LED swaps between color 1 and color 2.</summary>
        SwapColors = 2,
    }

    public class FlagRule : ObservableObject
    {
        /// <summary>Opaque black: keeps the LED dark (lower layers don't show through).</summary>
        public const string OffColor = "#FF000000";

        private FlagKind kind;
        private bool enabled = true;
        private string color = "#FFFFFF00";
        private string color2 = string.Empty;
        private FlagPattern pattern = FlagPattern.Alternate;
        private FlagAnimation animation;

        public FlagKind Kind { get => kind; set => Set(ref kind, value); }

        public bool Enabled { get => enabled; set => Set(ref enabled, value); }

        /// <summary>Color 1.</summary>
        public string Color { get => color; set => Set(ref color, value); }

        /// <summary>Optional color 2; empty for a single-color flag.</summary>
        public string Color2
        {
            get => color2;
            set
            {
                if (!Set(ref color2, value ?? string.Empty)) return;
                if (!HasSecondColor && Animation == FlagAnimation.SwapColors) Animation = FlagAnimation.Blink;
                OnPropertyChanged(nameof(HasSecondColor));
                OnPropertyChanged(nameof(AvailableAnimations));
            }
        }

        public FlagPattern Pattern { get => pattern; set => Set(ref pattern, value); }

        public FlagAnimation Animation { get => animation; set => Set(ref animation, value); }

        [JsonIgnore]
        public bool HasSecondColor => !string.IsNullOrWhiteSpace(Color2);

        /// <summary>Animations that make sense for this flag: swapping colors needs a second color.</summary>
        [JsonIgnore]
        public FlagAnimation[] AvailableAnimations => HasSecondColor
            ? new[] { FlagAnimation.Steady, FlagAnimation.Blink, FlagAnimation.SwapColors }
            : new[] { FlagAnimation.Steady, FlagAnimation.Blink };

        [JsonIgnore]
        public string DisplayName
        {
            get
            {
                switch (Kind)
                {
                    case FlagKind.Black: return "Black";
                    case FlagKind.Orange: return "Meatball";
                    case FlagKind.Checkered: return "Checkered";
                    case FlagKind.White: return "White";
                    case FlagKind.Blue: return "Blue";
                    case FlagKind.Green: return "Green";
                    case FlagKind.Debris: return "Debris";
                    default: return "Yellow";
                }
            }
        }

        public static FlagRule CreateDefault(FlagKind kind)
        {
            switch (kind)
            {
                case FlagKind.Yellow: return new FlagRule { Kind = kind, Color = "#FFFFD000", Animation = FlagAnimation.Blink };
                case FlagKind.Blue: return new FlagRule { Kind = kind, Color = "#FF0040FF", Animation = FlagAnimation.Blink };
                case FlagKind.White: return new FlagRule { Kind = kind, Color = "#FFFFFFFF" };
                case FlagKind.Black: return new FlagRule { Kind = kind, Enabled = false, Color = "#FFFF2000", Color2 = OffColor, Animation = FlagAnimation.Blink };
                case FlagKind.Checkered: return new FlagRule { Kind = kind, Color = "#FFFFFFFF", Color2 = OffColor };
                case FlagKind.Green: return new FlagRule { Kind = kind, Enabled = false, Color = "#FF00FF00" };
                case FlagKind.Orange: return new FlagRule { Kind = kind, Enabled = false, Color = "#FFFF6000", Color2 = OffColor };
                case FlagKind.Debris: return new FlagRule { Kind = kind, Color = "#FFFFD000", Color2 = "#FFFF0000", Pattern = FlagPattern.Alternate, Animation = FlagAnimation.Steady };
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }
    }

    public class FlagSettings : FeatureSettingsBase
    {
        private static readonly LedGroupKind[] FlagDefaultKinds = { LedGroupKind.VerticalColumn, LedGroupKind.Matrix };

        private int blinkIntervalMs = 400;

        public override LedGroupKind[] AllowedKinds => AllKinds;

        public override LedGroupKind[] DefaultKinds => FlagDefaultKinds;

        /// <summary>Interval for blinking and for swapping the colors of two-color flags.</summary>
        public int BlinkIntervalMs { get => blinkIntervalMs; set => Set(ref blinkIntervalMs, Math.Max(50, value)); }

        /// <summary>Flag rules in priority order: the first active and enabled rule is shown.</summary>
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public ObservableCollection<FlagRule> Rules { get; set; } = new ObservableCollection<FlagRule>();

        /// <summary>Default priority: penalties first, then the end of the race, then track condition flags, then informational ones.</summary>
        public static readonly FlagKind[] DefaultOrder =
        {
            FlagKind.Black,
            FlagKind.Checkered,
            FlagKind.Orange,
            FlagKind.Yellow,
            FlagKind.Debris,
            FlagKind.Blue,
            FlagKind.White,
            FlagKind.Green,
        };

        public void EnsureDefaults()
        {
            if (Rules == null) Rules = new ObservableCollection<FlagRule>();

            foreach (var obsolete in Rules.Where(r => r == null || !Enum.IsDefined(typeof(FlagKind), r.Kind)).ToList()) Rules.Remove(obsolete);
            foreach (var duplicate in Rules.GroupBy(r => r.Kind).SelectMany(g => g.Skip(1)).ToList()) Rules.Remove(duplicate);

            foreach (var rule in Rules)
            {
                if (!Enum.IsDefined(typeof(FlagPattern), rule.Pattern)) rule.Pattern = FlagPattern.Alternate;
                if (!Enum.IsDefined(typeof(FlagAnimation), rule.Animation)) rule.Animation = FlagAnimation.Steady;
                if (!rule.HasSecondColor && rule.Animation == FlagAnimation.SwapColors) rule.Animation = FlagAnimation.Blink;
            }

            foreach (var kind in DefaultOrder)
            {
                if (Rules.All(r => r.Kind != kind)) Rules.Add(FlagRule.CreateDefault(kind));
            }
        }
    }

    /// <summary>Where the car proximity LEDs start filling. Values are explicit because they are stored in the settings; never renumber them.</summary>
    public enum FillOrigin
    {
        /// <summary>Columns fill bottom-up, rows fill from the side of the other car.</summary>
        Start = 0,

        /// <summary>Columns fill top-down, rows fill towards the side of the other car.</summary>
        End = 1,
    }

    public class ProximitySettings : FeatureSettingsBase
    {
        private string color = "#FFFFD000";
        private bool progressive = true;
        private double maxDistance = 5.0;
        private FillOrigin fillOrigin = FillOrigin.Start;

        public override LedGroupKind[] AllowedKinds => AllKinds;

        public override LedGroupKind[] DefaultKinds => ColumnKinds;

        public string Color { get => color; set => Set(ref color, value); }

        /// <summary>Light more LEDs the closer the other car is (only when the sim provides the distance).</summary>
        public bool Progressive { get => progressive; set => Set(ref progressive, value); }

        /// <summary>Distance (meters) at which a single LED is lit; at 0 m all LEDs are lit.</summary>
        public double MaxDistance { get => maxDistance; set => Set(ref maxDistance, Math.Max(0.5, value)); }

        public FillOrigin FillOrigin { get => fillOrigin; set => Set(ref fillOrigin, value); }
    }

    /// <summary>Settings for the ABS and traction control indicators.</summary>
    public class IndicatorSettings : FeatureSettingsBase
    {
        /// <summary>Default ABS color: yellow, as on race car dashboards.</summary>
        public const string AbsDefaultColor = "#FFFFD000";

        /// <summary>Default traction control color: blue, as on race car dashboards.</summary>
        public const string TractionControlDefaultColor = "#FF0060FF";

        private string color = AbsDefaultColor;

        public IndicatorSettings()
        {
        }

        public IndicatorSettings(string defaultColor)
        {
            color = defaultColor;
        }

        private bool blink = true;
        private int blinkIntervalMs = 80;

        public override LedGroupKind[] AllowedKinds => AllKinds;

        public override LedGroupKind[] DefaultKinds => ColumnKinds;

        public string Color { get => color; set => Set(ref color, value); }

        public bool Blink { get => blink; set => Set(ref blink, value); }

        public int BlinkIntervalMs { get => blinkIntervalMs; set => Set(ref blinkIntervalMs, Math.Max(20, value)); }
    }

    /// <summary>
    /// The lowest layer: front-plate elements in their own static color (<see cref="LedGroup.StaticColor"/>)
    /// whenever no higher layer lights them. Every element is a target, so this is only on/off.
    /// </summary>
    public class StaticColorSettings : FeatureSettingsBase
    {
        public override LedGroupKind[] AllowedKinds => ElementKinds;

        public override LedGroupKind[] DefaultKinds => ElementKinds;
    }

    /// <summary>
    /// Front-plate buttons and dials in their press color (<see cref="LedGroup.PressColor"/>) while one of their inputs
    /// (<see cref="LedGroup.InputSlots"/>) is held: a button exactly while it is pressed, a dial while SimHub reports
    /// a step as pressed and at least <see cref="DialMinimumOnMs"/> after it. Every element is a target; lights, and
    /// elements without a press color or inputs, don't react.
    /// </summary>
    public class PressSettings : FeatureSettingsBase
    {
        /// <summary>
        /// How long a dial step shows at least: an encoder step can be pressed and released within one game loop frame,
        /// and would otherwise not show at all.
        /// </summary>
        public const int DialMinimumOnMs = 50;

        public override LedGroupKind[] AllowedKinds => ElementKinds;

        public override LedGroupKind[] DefaultKinds => ElementKinds;
    }

    /// <summary>
    /// Common part of the animations (idle, engine, pit limiter). They drive the rows, columns and matrices in their
    /// targets and, with <see cref="IncludeElements"/>, every front plate element. They aren't one of an element's
    /// effects (<see cref="DevicePart.ElementEffects"/>): like the press colors, they only show at certain moments.
    /// </summary>
    public abstract class AnimationSettingsBase : FeatureSettingsBase
    {
        private static readonly LedGroupKind[] LedKinds = { LedGroupKind.HorizontalRow, LedGroupKind.VerticalColumn, LedGroupKind.Matrix };

        private bool includeElements = true;
        private bool mirror;

        public override LedGroupKind[] AllowedKinds => LedKinds;

        public override LedGroupKind[] DefaultKinds => LedKinds;

        /// <summary>The front plate elements (buttons, lights, dials) show the animation too.</summary>
        public bool IncludeElements { get => includeElements; set => Set(ref includeElements, value); }

        /// <summary>Animations run from the center outwards, the same on both sides.</summary>
        public bool Mirror { get => mirror; set => Set(ref mirror, value); }

        public override bool Targets(LedGroup group) => group.IsElement ? IncludeElements : base.Targets(group);
    }

    /// <summary>What the LEDs show while no game runs. Values are explicit because they are stored in the settings; never renumber them.</summary>
    public enum IdleAnimation
    {
        /// <summary>All LEDs fade in and out together.</summary>
        Breathing = 0,

        /// <summary>A light with a fading tail runs along the LEDs and starts over.</summary>
        Running = 1,

        /// <summary>A light with a fading tail runs back and forth.</summary>
        Oscillating = 2,
    }

    /// <summary>How an animation's color changes. Values are explicit because they are stored in the settings; never renumber them.</summary>
    public enum AnimationColorMode
    {
        /// <summary>The chosen color.</summary>
        Fixed = 0,

        /// <summary>All LEDs change color together, through the rainbow.</summary>
        Cycle = 1,

        /// <summary>A rainbow along the LEDs that moves.</summary>
        Rainbow = 2,
    }

    /// <summary>The idle animation, shown while no game runs.</summary>
    public class IdleSettings : AnimationSettingsBase
    {
        private IdleAnimation animation = IdleAnimation.Running;
        private string color = "#FF0080FF";
        private AnimationColorMode colorMode = AnimationColorMode.Fixed;
        private int periodMs = 3000;
        private int trailPercent = 30;

        public IdleAnimation Animation { get => animation; set => Set(ref animation, value); }

        public string Color { get => color; set => Set(ref color, value); }

        public AnimationColorMode ColorMode { get => colorMode; set => Set(ref colorMode, value); }

        /// <summary>Duration of one breath, or of one run along the LEDs (oscillating: one way).</summary>
        public int PeriodMs { get => periodMs; set => Set(ref periodMs, Math.Max(300, Math.Min(20000, value))); }

        /// <summary>Length of the running light's tail, in percent of the LEDs.</summary>
        public int TrailPercent { get => trailPercent; set => Set(ref trailPercent, Math.Max(1, Math.Min(100, value))); }
    }

    /// <summary>How the LEDs greet an engine start or stop. Values are explicit because they are stored in the settings; never renumber them.</summary>
    public enum EngineAnimation
    {
        /// <summary>No animation.</summary>
        Off = 0,

        /// <summary>The LEDs fill up and empty again, like a gauge sweep.</summary>
        Sweep = 1,

        /// <summary>The LEDs fill up, then fade out.</summary>
        Fill = 2,

        /// <summary>All LEDs flash three times.</summary>
        Flash = 3,

        /// <summary>All LEDs light up and empty towards the start.</summary>
        Drain = 4,

        /// <summary>All LEDs light up and fade out.</summary>
        Fade = 5,
    }

    /// <summary>The animations when the car's engine starts or stops.</summary>
    public class EngineSettings : AnimationSettingsBase
    {
        private EngineAnimation startAnimation = EngineAnimation.Sweep;
        private string startColor = "#FF00E060";
        private EngineAnimation stopAnimation = EngineAnimation.Drain;
        private string stopColor = "#FFFF3000";
        private int durationMs = 1500;

        public EngineAnimation StartAnimation { get => startAnimation; set => Set(ref startAnimation, value); }

        public string StartColor { get => startColor; set => Set(ref startColor, value); }

        public EngineAnimation StopAnimation { get => stopAnimation; set => Set(ref stopAnimation, value); }

        public string StopColor { get => stopColor; set => Set(ref stopColor, value); }

        /// <summary>How long each animation plays.</summary>
        public int DurationMs { get => durationMs; set => Set(ref durationMs, Math.Max(300, Math.Min(5000, value))); }
    }

    /// <summary>What the LEDs show while the pit limiter is on. Values are explicit because they are stored in the settings; never renumber them.</summary>
    public enum PitLimiterAnimation
    {
        /// <summary>All LEDs switch between color 1 and color 2.</summary>
        Blink = 0,

        /// <summary>Groups of neighboring LEDs (or the two halves) swap color 1 and color 2.</summary>
        Alternate = 1,

        /// <summary>Stripes of color 1 and color 2 march along the LEDs.</summary>
        Scroll = 2,
    }

    /// <summary>The animation while the pit limiter is on.</summary>
    public class PitLimiterSettings : AnimationSettingsBase
    {
        private PitLimiterAnimation animation = PitLimiterAnimation.Alternate;
        private string color = "#FF0060FF";
        private string color2 = string.Empty;
        private int intervalMs = 250;
        private int alternateGroupSize;

        public PitLimiterAnimation Animation { get => animation; set => Set(ref animation, value); }

        /// <summary>
        /// <see cref="PitLimiterAnimation.Alternate"/>: how many neighboring LEDs light up together, the next ones in the
        /// other color; 0 = the two halves.
        /// </summary>
        public int AlternateGroupSize { get => alternateGroupSize; set => Set(ref alternateGroupSize, Math.Max(0, Math.Min(LedLimits.MaxLeds, value))); }

        /// <summary>Color 1.</summary>
        public string Color { get => color; set => Set(ref color, value); }

        /// <summary>Color 2; empty = dark.</summary>
        public string Color2 { get => color2; set => Set(ref color2, value ?? string.Empty); }

        /// <summary>How long each step lasts: a blink phase, a swap of the halves, a stripe moving one LED.</summary>
        public int IntervalMs { get => intervalMs; set => Set(ref intervalMs, Math.Max(50, Math.Min(2000, value))); }
    }
}
