using System;
using System.Linq;

namespace FreeTheLights.Settings
{
    /// <summary>
    /// Best guess of what kind of device a SimHub device is. SimHub has no device categories, and capabilities overlap
    /// between kinds (a wheel can have a display, RPM LEDs and button LEDs like a dashboard), so this is only the
    /// suggestion the wizard preselects; the user confirms or changes it.
    /// </summary>
    public static class DeviceKindGuess
    {
        private static readonly (string keyword, DeviceType type)[] NameKeywords =
        {
            ("flag", DeviceType.LedFlag),
            ("dashboard", DeviceType.Dashboard),
            ("ddu", DeviceType.Dashboard),
            ("dash", DeviceType.Dashboard),
            ("display", DeviceType.Dashboard),
            ("wheel", DeviceType.Wheel),
            ("button plate", DeviceType.Wheel),
            ("box", DeviceType.Custom),
            ("panel", DeviceType.Custom),
            ("brake light", DeviceType.Custom),
            ("bar", DeviceType.LedBar),
        };

        /// <summary>The suggested kind and why, e.g. (Wheel, "SimHub has a button layout for it").</summary>
        public static (DeviceType type, string reason) Guess(SimHubDeviceInfo device)
        {
            var name = " " + (device?.Name ?? string.Empty).ToLowerInvariant() + " ";
            foreach (var (keyword, type) in NameKeywords)
            {
                if (ContainsWord(name, keyword)) return (type, $"its name contains \"{keyword}\"");
            }

            if (device == null) return (DeviceType.Custom, "nothing is known about it");
            if (device.HasButtonLayout) return (DeviceType.Wheel, "SimHub has a button layout for it, like for wheels");
            if (device.Has(LedChannel.Matrix) && device.Channels.Keys.All(c => c == LedChannel.Matrix)) return (DeviceType.LedFlag, "its only LEDs are an LED matrix");
            if (device.HasDisplay && device.Has(LedChannel.Leds)) return (DeviceType.Dashboard, "it has a display with LEDs");
            if (device.Has(LedChannel.Leds) && !device.HasDisplay && device.Channels.Count == 1) return (DeviceType.LedBar, "it only has a strip of LEDs");
            return (DeviceType.Custom, "its LEDs don't match a typical wheel, dashboard or LED flag");
        }

        /// <summary>The keyword appears as a whole word (or words), so "bar" doesn't match "Barrel".</summary>
        private static bool ContainsWord(string text, string keyword)
        {
            var index = text.IndexOf(keyword, StringComparison.Ordinal);
            while (index >= 0)
            {
                var before = index == 0 ? ' ' : text[index - 1];
                var after = index + keyword.Length >= text.Length ? ' ' : text[index + keyword.Length];
                if (!char.IsLetterOrDigit(before) && !char.IsLetterOrDigit(after)) return true;
                index = text.IndexOf(keyword, index + 1, StringComparison.Ordinal);
            }

            return false;
        }
    }
}
