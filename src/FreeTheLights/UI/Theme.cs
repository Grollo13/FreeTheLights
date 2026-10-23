using System.Linq;
using System.Windows.Media;
using FreeTheLights.Engine;
using FreeTheLights.Settings;

namespace FreeTheLights.UI
{
    /// <summary>
    /// Accent colors of the settings UI, one per feature, so a feature looks the same on its tab, in the
    /// priority list and on the test chips. XAML reads them with <c>{x:Static ui:Theme.Rpm}</c>.
    /// </summary>
    public static class Theme
    {
        public static readonly SolidColorBrush Layout = Make(0x29, 0xB6, 0xF6);
        public static readonly SolidColorBrush FrontPlate = Make(0x26, 0xC6, 0xDA);
        public static readonly SolidColorBrush Rpm = Make(0x66, 0xBB, 0x6A);
        public static readonly SolidColorBrush Flags = Make(0xFF, 0xCA, 0x28);
        public static readonly SolidColorBrush Proximity = Make(0xFF, 0x8A, 0x3D);
        public static readonly SolidColorBrush Abs = Make(0xEF, 0x53, 0x50);
        public static readonly SolidColorBrush TractionControl = Make(0xBA, 0x68, 0xC8);
        public static readonly SolidColorBrush StaticColor = Make(0xB0, 0xBE, 0xC5);
        public static readonly SolidColorBrush Press = Make(0x4D, 0xD0, 0xE1);
        public static readonly SolidColorBrush IdleLights = Make(0x95, 0x75, 0xCD);
        public static readonly SolidColorBrush Priority = Make(0x90, 0xA4, 0xAE);
        public static readonly SolidColorBrush Neutral = Make(0x90, 0xA4, 0xAE);

        public static readonly SolidColorBrush Ok = Make(0x4C, 0xD9, 0x64);
        public static readonly SolidColorBrush Busy = Make(0xFF, 0xC1, 0x07);
        public static readonly SolidColorBrush Warning = Make(0xFF, 0x8A, 0x3D);
        public static readonly SolidColorBrush Idle = Make(0x80, 0x80, 0x80);

        /// <summary>Colors of the LED groups in the previews and wiring lists (by position, repeating): those of the wiring test pattern.</summary>
        public static readonly Color[] WiringColors = WiringPattern.Colors.Select(c => Color.FromRgb(c.R, c.G, c.B)).ToArray();

        public static Brush Accent(LayerKind kind)
        {
            switch (kind)
            {
                case LayerKind.Rpm: return Rpm;
                case LayerKind.Flags: return Flags;
                case LayerKind.Proximity: return Proximity;
                case LayerKind.Abs: return Abs;
                case LayerKind.TractionControl: return TractionControl;
                case LayerKind.StaticColor: return StaticColor;
                case LayerKind.Press: return Press;
                case LayerKind.Idle: return IdleLights;
                default: return Neutral;
            }
        }

        private static SolidColorBrush Make(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }
    }
}
