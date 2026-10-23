using System.Collections.Generic;
using System.Linq;
using FreeTheLights.Core;
using FreeTheLights.Settings;

namespace FreeTheLights.Engine
{
    /// <summary>
    /// A test pattern for checking how a part's LEDs are wired, shown on the device and in the preview at the same time:
    /// <list type="bullet">
    /// <item>every row, column or matrix lights dimly in its own color (in wiring order, the colors of
    /// <see cref="Colors"/>), so the device shows which LEDs belong to which group;</item>
    /// <item>a bright white dot runs through each group from its first to its last wired LED, so the device shows the
    /// direction;</item>
    /// <item>front plate elements light dimly white.</item>
    /// </list>
    /// The frame is in physical LED order like any other frame. The preview maps it through the configured layout, so
    /// where device and preview differ, the layout's order or direction is wrong.
    /// </summary>
    public static class WiringPattern
    {
        /// <summary>
        /// Colors of the groups in wiring order (repeating), also the dots of the wiring lists. Plain red, green and blue
        /// first, then the other colors that LEDs show clearly apart, also at low brightness.
        /// </summary>
        public static readonly LedColor[] Colors =
        {
            LedColor.FromRgb(0xFF, 0x00, 0x00),
            LedColor.FromRgb(0x00, 0xFF, 0x00),
            LedColor.FromRgb(0x00, 0x00, 0xFF),
            LedColor.FromRgb(0xFF, 0xFF, 0x00),
            LedColor.FromRgb(0xFF, 0x00, 0xFF),
            LedColor.FromRgb(0x00, 0xFF, 0xFF),
        };

        /// <summary>The running dot: white stands out on every group color.</summary>
        public static readonly LedColor DotColor = LedColor.FromRgb(0xFF, 0xFF, 0xFF);

        /// <summary>How long the running dot stays on one LED.</summary>
        public const int StepMs = 400;

        private const double Dim = 0.25;

        private static readonly LedColor ElementColor = LedColor.FromRgb(0xFF, 0xFF, 0xFF);

        /// <summary>The rows, columns and matrices of a part in wiring order (by start index).</summary>
        public static List<LedGroup> GroupsInWiringOrder(DevicePart part) => part.GroupsInWiringOrder();

        /// <summary>The pattern for <paramref name="part"/> at <paramref name="nowMs"/>, at the part's brightness.</summary>
        public static LedColor[] Render(DevicePart part, long nowMs)
        {
            var total = part.TotalLeds;
            var frame = new LedColor[total];
            var brightness = part.Brightness / 100.0;

            foreach (var element in part.Groups.Where(g => g != null && g.IsElement))
            {
                if (element.StartIndex < total) frame[element.StartIndex] = ElementColor.Scale(Dim * brightness);
            }

            var groups = GroupsInWiringOrder(part);
            for (var g = 0; g < groups.Count; g++)
            {
                var group = groups[g];
                var color = Colors[g % Colors.Length];
                var count = group.LedCount;
                if (count <= 0) continue;

                // The dot walks the group's physical LEDs, i.e. the wiring.
                var dot = (int)(nowMs / StepMs % count);
                for (var i = 0; i < count; i++)
                {
                    var index = group.StartIndex + i;
                    if (index < total) frame[index] = i == dot ? DotColor.Scale(brightness) : color.Scale(Dim * brightness);
                }
            }

            return frame;
        }
    }
}
