using System;
using System.Collections.Generic;
using System.Linq;
using FreeTheLights.CarData;
using FreeTheLights.Core;
using FreeTheLights.Settings;

namespace FreeTheLights.Engine
{
    /// <summary>
    /// Renders one frame for a device: every feature draws into its own layer, then the layers are
    /// composed in the device's priority order (first non-transparent color wins per LED).
    /// Opaque black (<see cref="LedColor.Off"/>) is used where a feature wants an LED dark (blink off-phase,
    /// pattern gaps) so lower layers don't flicker through; transparent LEDs are left to lower layers and,
    /// at the end, to the other effects of the SimHub LED profile.
    /// </summary>
    public static class FrameRenderer
    {
        public static LedColor[] Render(DevicePart device, TelemetrySnapshot telemetry, CarLedProfile profile, long nowMs)
        {
            var groups = device.Groups.ToArray();
            var total = device.TotalLeds;
            var result = new LedColor[total];
            if (total == 0) return result;

            var priority = device.LayerPriority.ToArray();
            var layers = new LedColor[priority.Length][];
            for (var l = 0; l < priority.Length; l++)
            {
                var feature = device.GetFeature(priority[l]);
                if (feature == null || !feature.Enabled || !Renderers.TryGetValue(priority[l], out var render)) continue;

                var layer = new LedColor[total];
                foreach (var group in groups)
                {
                    if (!feature.Targets(group)) continue;
                    render(device, group, layer, telemetry, profile, nowMs);
                }

                layers[l] = layer;
            }

            var brightness = device.Brightness / 100.0;
            for (var i = 0; i < total; i++)
            {
                foreach (var layer in layers)
                {
                    if (layer == null || layer[i].IsTransparent) continue;
                    result[i] = layer[i].Scale(brightness);
                    break;
                }
            }

            return result;
        }

        /// <summary>Draws one feature's layer for one of the groups it targets.</summary>
        private delegate void LayerRenderer(DevicePart device, LedGroup group, LedColor[] layer, TelemetrySnapshot t, CarLedProfile profile, long nowMs);

        /// <summary>The renderer of every feature in <see cref="DevicePart.AllFeatures"/> (a test checks that none is missing).</summary>
        private static readonly Dictionary<LayerKind, LayerRenderer> Renderers = new Dictionary<LayerKind, LayerRenderer>
        {
            [LayerKind.Rpm] = (d, g, l, t, p, now) => RenderRpm(d.Rpm, g, l, t, p, now),
            [LayerKind.Flags] = (d, g, l, t, p, now) => RenderFlags(d.Flags, g, l, t, now),
            [LayerKind.Proximity] = (d, g, l, t, p, now) => RenderProximity(d.Proximity, g, l, t),
            [LayerKind.Abs] = (d, g, l, t, p, now) => RenderIndicator(d.Abs, t.AbsActive, g, l, now),
            [LayerKind.TractionControl] = (d, g, l, t, p, now) => RenderIndicator(d.TractionControl, t.TcActive, g, l, now),
            [LayerKind.StaticColor] = (d, g, l, t, p, now) => RenderStaticColor(g, l),
            [LayerKind.Press] = (d, g, l, t, p, now) => RenderPress(g, l, t, now),
            [LayerKind.Idle] = (d, g, l, t, p, now) => RenderIdle(d, d.Idle, g, l, t, now),
            [LayerKind.Engine] = (d, g, l, t, p, now) => RenderEngine(d, d.Engine, g, l, t, now),
            [LayerKind.PitLimiter] = (d, g, l, t, p, now) => RenderPitLimiter(d, d.PitLimiter, g, l, t, now),
        };

        /// <summary>Whether the renderer draws <paramref name="kind"/>'s layer; a feature without a renderer stays dark.</summary>
        public static bool HasRenderer(LayerKind kind) => Renderers.ContainsKey(kind);

        // ---------- Feature 1: RPM ----------

        public static void RenderRpm(RpmSettings settings, LedGroup group, LedColor[] layer, TelemetrySnapshot t, CarLedProfile profile, long nowMs)
        {
            if (group.Kind != LedGroupKind.HorizontalRow) return;

            LedColor[] states;
            if (profile != null)
            {
                states = LedResampler.ResampleLeds(profile.ComputeLeds(t.Rpm, t.Gear, nowMs), group.Count);
            }
            else if (settings.FallbackMode == RpmFallbackMode.Generic)
            {
                states = GenericShiftLights(settings, group.Count, t, nowMs);
            }
            else
            {
                return;
            }

            for (var i = 0; i < group.Count; i++)
            {
                if (!states[i].IsTransparent) layer[group.PhysicalIndex(i)] = states[i];
            }
        }

        private static readonly LedColor GenericGreen = LedColor.FromRgb(0, 255, 0);
        private static readonly LedColor GenericYellow = LedColor.FromRgb(255, 200, 0);
        private static readonly LedColor GenericRed = LedColor.FromRgb(255, 0, 0);

        public static LedColor[] GenericShiftLights(RpmSettings settings, int count, TelemetrySnapshot t, long nowMs)
        {
            var states = new LedColor[count];
            var redline = t.Redline > 0 ? t.Redline : t.MaxRpm;
            if (redline <= 0 || t.Rpm <= 0) return states;

            var percent = t.Rpm / redline;
            if (percent >= 1)
            {
                // Blink off-phase: opaque black keeps the row owned, so lower layers don't flicker through.
                var color = Blink.IsOffPhase(nowMs, settings.GenericBlinkIntervalMs) ? LedColor.Off : GenericRed;
                for (var i = 0; i < count; i++) states[i] = color;
                return states;
            }

            var start = settings.GenericStartPercent / 100.0;
            var fraction = (percent - start) / (1 - start);
            var lit = fraction <= 0 ? 0 : Math.Min(count, (int)Math.Ceiling(fraction * count));
            for (var i = 0; i < lit; i++)
            {
                var position = (i + 0.5) / count;
                states[i] = position < 0.5 ? GenericGreen : position < 0.8 ? GenericYellow : GenericRed;
            }

            return states;
        }

        // ---------- Feature 2: flags ----------

        public static FlagRule ActiveFlag(FlagSettings settings, TelemetrySnapshot t)
        {
            foreach (var rule in settings.Rules)
            {
                if (rule.Enabled && IsFlagActive(rule.Kind, t)) return rule;
            }

            return null;
        }

        public static bool IsFlagActive(FlagKind kind, TelemetrySnapshot t)
        {
            switch (kind)
            {
                case FlagKind.Yellow: return t.FlagYellow;
                case FlagKind.Blue: return t.FlagBlue;
                case FlagKind.White: return t.FlagWhite;
                case FlagKind.Black: return t.FlagBlack;
                case FlagKind.Checkered: return t.FlagCheckered;
                case FlagKind.Green: return t.FlagGreen;
                case FlagKind.Orange: return t.FlagOrange;
                case FlagKind.Debris: return t.FlagDebris;
                default: return false;
            }
        }

        public static void RenderFlags(FlagSettings settings, LedGroup group, LedColor[] layer, TelemetrySnapshot t, long nowMs)
        {
            var rule = ActiveFlag(settings, t);
            if (rule == null) return;

            var oddPhase = Blink.IsOffPhase(nowMs, settings.BlinkIntervalMs);
            if (rule.Animation == FlagAnimation.Blink && oddPhase)
            {
                for (var i = 0; i < group.LedCount; i++) layer[group.PhysicalIndex(i)] = LedColor.Off;
                return;
            }

            var color1 = LedColor.ParseCached(rule.Color);
            var color2 = rule.HasSecondColor ? LedColor.ParseCached(rule.Color2) : color1;
            var swap = rule.HasSecondColor && rule.Animation == FlagAnimation.SwapColors && oddPhase;

            for (var y = 0; y < group.Height; y++)
            {
                for (var x = 0; x < group.Width; x++)
                {
                    var first = !rule.HasSecondColor || IsFirstColor(rule.Pattern, group, x, y);
                    if (swap) first = !first;
                    var color = first ? color1 : color2;
                    layer[group.PhysicalIndex(x, y)] = color.IsTransparent ? LedColor.Off : color;
                }
            }
        }

        /// <summary>Whether pixel (x, y) of the group shows color 1 (otherwise color 2) for a two-color flag.</summary>
        public static bool IsFirstColor(FlagPattern pattern, LedGroup group, int x, int y)
        {
            if (group.Kind == LedGroupKind.Element) return true;

            if (group.Kind != LedGroupKind.Matrix)
            {
                // Logical LED index: rows from the left, columns from the bottom.
                var index = group.Kind == LedGroupKind.HorizontalRow ? x : group.Count - 1 - y;
                return pattern == FlagPattern.Diagonal ? index < (group.Count + 1) / 2 : index % 2 == 0;
            }

            int w = group.Columns, h = group.Rows;
            switch (pattern)
            {
                case FlagPattern.Checker:
                    var cell = Math.Max(1, Math.Min(w, h) / 4);
                    return ((x / cell) + (y / cell)) % 2 == 0;
                case FlagPattern.Diagonal:
                    // Upper-left triangle color 1, lower-right color 2.
                    return (x + 0.5) / w + (y + 0.5) / h < 1.0;
                default:
                    return (x + y) % 2 == 0;
            }
        }

        // ---------- Feature 3: car proximity ----------

        /// <summary>
        /// Number of LEDs to light for a car at <paramref name="distance"/> meters: all LEDs when the
        /// distance is unknown (0/NaN) or progressive mode is off, otherwise more LEDs the closer it gets.
        /// </summary>
        public static int ProximityLitCount(ProximitySettings settings, int count, double distance)
        {
            if (count <= 1 || !settings.Progressive || double.IsNaN(distance) || double.IsInfinity(distance) || distance <= 0) return count;
            var closeness = Math.Max(0, Math.Min(1, 1 - distance / settings.MaxDistance));
            return Math.Max(1, Math.Min(count, (int)Math.Ceiling(count * closeness)));
        }

        public static void RenderProximity(ProximitySettings settings, LedGroup group, LedColor[] layer, TelemetrySnapshot t)
        {
            var left = t.SpotterCarLeft;
            var right = t.SpotterCarRight;
            if (!left && !right) return;

            var color = LedColor.ParseCached(settings.Color);
            var fromEnd = settings.FillOrigin == FillOrigin.End;

            switch (group.Kind)
            {
                case LedGroupKind.VerticalColumn:
                {
                    var lit = 0;
                    if (left && group.Side != LedGroupSide.Right) lit = Math.Max(lit, ProximityLitCount(settings, group.Count, t.SpotterCarLeftDistance));
                    if (right && group.Side != LedGroupSide.Left) lit = Math.Max(lit, ProximityLitCount(settings, group.Count, t.SpotterCarRightDistance));
                    for (var i = 0; i < lit; i++) layer[group.PhysicalIndex(fromEnd ? group.Count - 1 - i : i)] = color;
                    break;
                }

                case LedGroupKind.HorizontalRow:
                {
                    // Left car fills from the left edge, right car from the right edge ("End" fills from the center outwards).
                    if (left && group.Side != LedGroupSide.Right)
                    {
                        var span = group.Side == LedGroupSide.Left ? group.Count : (group.Count + 1) / 2;
                        var lit = ProximityLitCount(settings, span, t.SpotterCarLeftDistance);
                        for (var i = 0; i < lit; i++) layer[group.PhysicalIndex(fromEnd ? span - 1 - i : i)] = color;
                    }

                    if (right && group.Side != LedGroupSide.Left)
                    {
                        var span = group.Side == LedGroupSide.Right ? group.Count : (group.Count + 1) / 2;
                        var lit = ProximityLitCount(settings, span, t.SpotterCarRightDistance);
                        for (var i = 0; i < lit; i++) layer[group.PhysicalIndex(fromEnd ? group.Count - span + i : group.Count - 1 - i)] = color;
                    }

                    break;
                }

                case LedGroupKind.Element:
                    // Left/right elements react to their side's car, center dials to both.
                    if ((left && group.Side != LedGroupSide.Right) || (right && group.Side != LedGroupSide.Left)) layer[group.PhysicalIndex(0)] = color;
                    break;

                case LedGroupKind.Matrix:
                {
                    var w = group.Columns;
                    if (left && group.Side != LedGroupSide.Right)
                    {
                        var span = group.Side == LedGroupSide.Left ? w : (w + 1) / 2;
                        var lit = ProximityLitCount(settings, span, t.SpotterCarLeftDistance);
                        for (var i = 0; i < lit; i++) FillMatrixColumn(group, layer, fromEnd ? span - 1 - i : i, color);
                    }

                    if (right && group.Side != LedGroupSide.Left)
                    {
                        var span = group.Side == LedGroupSide.Right ? w : (w + 1) / 2;
                        var lit = ProximityLitCount(settings, span, t.SpotterCarRightDistance);
                        for (var i = 0; i < lit; i++) FillMatrixColumn(group, layer, fromEnd ? w - span + i : w - 1 - i, color);
                    }

                    break;
                }
            }
        }

        private static void FillMatrixColumn(LedGroup group, LedColor[] layer, int x, LedColor color)
        {
            for (var y = 0; y < group.Rows; y++) layer[group.PhysicalIndex(x, y)] = color;
        }

        // ---------- Features 4 & 5: ABS / TC ----------

        public static void RenderIndicator(IndicatorSettings settings, bool active, LedGroup group, LedColor[] layer, long nowMs)
        {
            if (!active) return;

            var color = settings.Blink && Blink.IsOffPhase(nowMs, settings.BlinkIntervalMs) ? LedColor.Off : LedColor.ParseCached(settings.Color);
            for (var i = 0; i < group.LedCount; i++) layer[group.PhysicalIndex(i)] = color;
        }

        // ---------- Front plate: static colors ----------

        public static void RenderStaticColor(LedGroup group, LedColor[] layer)
        {
            if (group.Kind != LedGroupKind.Element) return;
            var color = LedColor.ParseCached(group.StaticColor);
            if (!color.IsTransparent) layer[group.PhysicalIndex(0)] = color;
        }

        // ---------- Front plate: button presses ----------

        /// <summary>
        /// An element in its press color while one of its inputs is held; a dial also for
        /// <see cref="PressSettings.DialMinimumOnMs"/> after each step.
        /// </summary>
        public static void RenderPress(LedGroup group, LedColor[] layer, TelemetrySnapshot t, long nowMs)
        {
            if (!group.CanBePressed) return;
            var color = LedColor.ParseCached(group.PressColor);
            if (color.IsTransparent) return;

            if (t.SimulatePress || IsPressed(group, t.Inputs, nowMs)) layer[group.PhysicalIndex(0)] = color;
        }

        /// <summary>One of the element's inputs is held (or, for a dial, was just pressed). Runs every frame: no allocations.</summary>
        private static bool IsPressed(LedGroup group, InputState inputs, long nowMs)
        {
            if (inputs == null) return false;
            var dial = group.ElementType == ElementType.Dial;
            var slots = group.InputSlots;
            for (var i = 0; i < slots.Count; i++)
            {
                var input = group.GetInput(slots[i]);
                if (dial ? inputs.IsHeldOrPressedWithin(input, nowMs, PressSettings.DialMinimumOnMs) : inputs.IsHeld(input)) return true;
            }

            return false;
        }

        // ---------- Idle animation ----------

        /// <summary>Darkest point of a breath.</summary>
        public const double BreathDim = 0.05;

        public static void RenderIdle(DevicePart part, IdleSettings settings, LedGroup group, LedColor[] layer, TelemetrySnapshot t, long nowMs)
        {
            if (!t.Idle) return;
            var sampler = new IdleSampler
            {
                Animation = settings.Animation,
                Color = LedColor.ParseCached(settings.Color),
                ColorMode = settings.ColorMode,
                NowMs = nowMs,
                PeriodMs = settings.PeriodMs,
                Trail = settings.TrailPercent / 100.0,
            };
            Animations.Paint(part, group, layer, settings.Mirror, ref sampler);
        }

        private struct IdleSampler : Animations.ISampler
        {
            public IdleAnimation Animation;
            public LedColor Color;
            public AnimationColorMode ColorMode;
            public long NowMs;
            public int PeriodMs;
            public double Trail;

            public LedColor Sample(double position, double step)
            {
                double brightness;
                switch (Animation)
                {
                    case IdleAnimation.Running:
                    {
                        // The light enters before the first LED and runs on until its tail has left the last one.
                        var head = -step / 2 + Animations.Phase(NowMs, PeriodMs) * (1 + Math.Max(Trail, step) + step);
                        brightness = Animations.Tail(head - position, Trail, step);
                        break;
                    }

                    case IdleAnimation.Oscillating:
                    {
                        // One way per period, then back; the tail follows the light.
                        var phase = Animations.Phase(NowMs, 2 * PeriodMs) * 2;
                        var forward = phase < 1;
                        var head = forward ? phase : 2 - phase;
                        brightness = Animations.Tail(forward ? head - position : position - head, Trail, step);
                        break;
                    }

                    default:
                        brightness = Animations.Breath(Animations.Phase(NowMs, PeriodMs), BreathDim);
                        break;
                }

                return Animations.Shade(Animations.ColorAt(Color, ColorMode, position, NowMs), brightness);
            }
        }

        // ---------- Engine start and stop ----------

        /// <summary>The start or stop animation for <see cref="EngineSettings.DurationMs"/> after the latest of the two.</summary>
        public static void RenderEngine(DevicePart part, EngineSettings settings, LedGroup group, LedColor[] layer, TelemetrySnapshot t, long nowMs)
        {
            var start = t.EngineStartedAtMs != EngineStateTracker.Never
                        && (t.EngineStoppedAtMs == EngineStateTracker.Never || t.EngineStartedAtMs >= t.EngineStoppedAtMs);
            var at = start ? t.EngineStartedAtMs : t.EngineStoppedAtMs;
            if (at == EngineStateTracker.Never) return;

            var elapsed = nowMs - at;
            if (elapsed < 0 || elapsed >= settings.DurationMs) return;

            var sampler = new EngineSampler
            {
                Animation = start ? settings.StartAnimation : settings.StopAnimation,
                Color = LedColor.ParseCached(start ? settings.StartColor : settings.StopColor),
                Progress = (double)elapsed / settings.DurationMs,
            };
            if (sampler.Animation == EngineAnimation.Off) return;
            Animations.Paint(part, group, layer, settings.Mirror, ref sampler);
        }

        /// <summary>Share of the <see cref="EngineAnimation.Fill"/> animation that fills; the rest fades.</summary>
        private const double FillShare = 0.6;

        private struct EngineSampler : Animations.ISampler
        {
            public EngineAnimation Animation;
            public LedColor Color;

            /// <summary>0..1 through the animation.</summary>
            public double Progress;

            public LedColor Sample(double position, double step)
            {
                double brightness;
                switch (Animation)
                {
                    case EngineAnimation.Sweep:
                        brightness = position <= (Progress < 0.5 ? 2 * Progress : 2 - 2 * Progress) ? 1 : 0;
                        break;
                    case EngineAnimation.Fill:
                        brightness = Progress >= FillShare ? 1 - (Progress - FillShare) / (1 - FillShare)
                            : position <= Progress / FillShare ? 1 : 0;
                        break;
                    case EngineAnimation.Flash:
                        brightness = Progress * 3 % 1 < 0.5 ? 1 : 0;
                        break;
                    case EngineAnimation.Drain:
                        brightness = position <= 1 - Progress ? 1 : 0;
                        break;
                    default:
                        brightness = 1 - Progress;
                        break;
                }

                return Animations.Shade(Color, brightness);
            }
        }

        // ---------- Pit limiter ----------

        /// <summary>LEDs per stripe of the <see cref="PitLimiterAnimation.Scroll"/> animation.</summary>
        public const int PitStripeLeds = 2;

        public static void RenderPitLimiter(DevicePart part, PitLimiterSettings settings, LedGroup group, LedColor[] layer, TelemetrySnapshot t, long nowMs)
        {
            if (!t.PitLimiter) return;
            var color2 = LedColor.ParseCached(settings.Color2);
            var sampler = new PitLimiterSampler
            {
                Animation = settings.Animation,
                Color = LedColor.ParseCached(settings.Color),
                Color2 = color2.IsTransparent ? LedColor.Off : color2,
                Steps = nowMs / settings.IntervalMs,
                GroupSize = settings.AlternateGroupSize,
            };
            Animations.Paint(part, group, layer, settings.Mirror, ref sampler);
        }

        private struct PitLimiterSampler : Animations.ISampler
        {
            public PitLimiterAnimation Animation;
            public LedColor Color;
            public LedColor Color2;

            /// <summary>Intervals passed so far.</summary>
            public long Steps;

            /// <summary><see cref="PitLimiterSettings.AlternateGroupSize"/>: neighboring LEDs that alternate together, 0 = halves.</summary>
            public int GroupSize;

            public LedColor Sample(double position, double step)
            {
                bool first;
                switch (Animation)
                {
                    case PitLimiterAnimation.Alternate:
                    {
                        var firstGroup = GroupSize <= 0 ? position < 0.5 : (long)Math.Floor(position / step) / GroupSize % 2 == 0;
                        first = firstGroup == (Steps % 2 == 0);
                        break;
                    }
                    case PitLimiterAnimation.Scroll:
                    {
                        // The stripes move one LED per interval, towards the end.
                        var index = (long)Math.Floor(position / step) - Steps;
                        var stripe = (index >= 0 ? index : index - PitStripeLeds + 1) / PitStripeLeds;
                        first = stripe % 2 == 0;
                        break;
                    }

                    default:
                        first = Steps % 2 == 0;
                        break;
                }

                var color = first ? Color : Color2;
                return color.IsTransparent ? LedColor.Off : color;
            }
        }
    }
}
