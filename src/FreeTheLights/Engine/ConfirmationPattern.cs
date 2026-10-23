using System;
using FreeTheLights.Core;
using FreeTheLights.Settings;

namespace FreeTheLights.Engine
{
    /// <summary>
    /// Shown right after the wizard set up a device's SimHub profile, so the user sees at once that FreeTheLights drives
    /// it: every LED of the part breathes green.
    /// </summary>
    public static class ConfirmationPattern
    {
        public static readonly LedColor Color = LedColor.FromRgb(0x4C, 0xD9, 0x64);

        /// <summary>One breath, dim to bright and back.</summary>
        public const int PeriodMs = 1600;

        /// <summary>Brightness at the dimmest point of a breath.</summary>
        public const double Dim = 0.3;

        /// <summary>The pattern for <paramref name="part"/> at <paramref name="nowMs"/>, at the part's brightness.</summary>
        public static LedColor[] Render(DevicePart part, long nowMs)
        {
            var total = part.TotalLeds;
            var frame = new LedColor[total];
            var phase = (nowMs % PeriodMs) / (double)PeriodMs;
            var breath = Dim + (1 - Dim) * (0.5 - 0.5 * Math.Cos(2 * Math.PI * phase));
            var color = Color.Scale(breath * part.Brightness / 100.0);
            for (var i = 0; i < total; i++) frame[i] = color;
            return frame;
        }
    }
}
