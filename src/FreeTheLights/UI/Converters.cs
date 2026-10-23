using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using FreeTheLights.Core;
using FreeTheLights.Settings;
using MahApps.Metro.IconPacks;

namespace FreeTheLights.UI
{
    /// <summary>bool → Visibility; pass "Inverse" as parameter to invert.</summary>
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var visible = value is bool b && b;
            if (string.Equals(parameter as string, "Inverse", StringComparison.OrdinalIgnoreCase)) visible = !visible;
            return visible ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    /// <summary>Enum value ↔ bool for radio-button style bindings (parameter = enum member name).</summary>
    public class EnumEqualsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value != null && string.Equals(value.ToString(), parameter as string, StringComparison.Ordinal);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool b && b ? Enum.Parse(targetType, (string)parameter) : Binding.DoNothing;
        }
    }

    /// <summary>int == parameter → true (used for wizard steps).</summary>
    public class IntEqualsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // The parameter is a number in the XAML ("3") or a constant ({x:Static ...}).
            var equals = value is int i && (parameter is int p ? i == p : int.TryParse(parameter as string, NumberStyles.Integer, CultureInfo.InvariantCulture, out p) && i == p);
            return targetType == typeof(Visibility) ? (object)(equals ? Visibility.Visible : Visibility.Collapsed) : equals;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    /// <summary>bool → opacity: full when true, dimmed when false (parameter: the dimmed opacity, default 0.45).</summary>
    public class BoolToOpacityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b && b) return 1.0;
            return double.TryParse(parameter as string, NumberStyles.Float, CultureInfo.InvariantCulture, out var dimmed) ? dimmed : 0.45;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    /// <summary>"#AARRGGBB" → brush. Multi-binding form: the first non-empty color wins (an optional second color falls back to the first).</summary>
    public class HexToBrushConverter : IValueConverter, IMultiValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => ToBrush(value as string);

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            foreach (var value in values)
            {
                if (value is string hex && !string.IsNullOrWhiteSpace(hex)) return ToBrush(hex);
            }

            return Brushes.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();

        private static Brush ToBrush(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex) || !LedColor.TryParse(hex, out var c)) return Brushes.Transparent;
            var brush = new SolidColorBrush(Color.FromArgb(c.A, c.R, c.G, c.B));
            brush.Freeze();
            return brush;
        }
    }

    /// <summary>(StartIndex, LedCount) → "LEDs 1–15" in the 1-based numbering of SimHub's LED editor.</summary>
    public class LedRangeConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 2 || !(values[0] is int start) || !(values[1] is int count)) return string.Empty;
            return count <= 1 ? $"LED {start + 1}" : $"LEDs {start + 1}–{start + count}";
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    /// <summary>Feature layer → its icon.</summary>
    public class LayerIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            switch (value is LayerKind kind ? kind : (LayerKind?)null)
            {
                case LayerKind.Rpm: return PackIconMaterialKind.Speedometer;
                case LayerKind.Flags: return PackIconMaterialKind.Flag;
                case LayerKind.Proximity: return PackIconMaterialKind.CarSide;
                case LayerKind.Abs: return PackIconMaterialKind.CarBrakeAbs;
                case LayerKind.TractionControl: return PackIconMaterialKind.CarTractionControl;
                case LayerKind.StaticColor: return PackIconMaterialKind.Palette;
                case LayerKind.Press: return PackIconMaterialKind.GestureTapButton;
                case LayerKind.Idle: return PackIconMaterialKind.Sleep;
                case LayerKind.Engine: return PackIconMaterialKind.Engine;
                case LayerKind.PitLimiter: return PackIconMaterialKind.SpeedometerSlow;
                default: return PackIconMaterialKind.Layers;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    /// <summary>Feature layer → its accent brush.</summary>
    public class LayerAccentConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is LayerKind kind ? Theme.Accent(kind) : Theme.Neutral;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    /// <summary>Device → icon of its type. Multi-binding form: the first value is the device (a second one only triggers updates).</summary>
    public class DeviceIconConverter : IValueConverter, IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) => Convert(values.FirstOrDefault(), targetType, parameter, culture);

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            switch ((value as DeviceConfig)?.Type)
            {
                case DeviceType.Wheel: return PackIconMaterialKind.Steering;
                case DeviceType.Dashboard: return PackIconMaterialKind.Speedometer;
                case DeviceType.LedBar: return PackIconMaterialKind.LedStripVariant;
                case DeviceType.LedFlag: return PackIconMaterialKind.Flag;
                default: return PackIconMaterialKind.Tune;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    /// <summary>
    /// (Device, revision) → "31 LEDs · 4 features". The revision is any value that changes when the device's layout
    /// or features change, so the text is rebuilt.
    /// </summary>
    public class DeviceSummaryConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length == 0 || !(values[0] is DeviceConfig device)) return string.Empty;
            var features = device.Parts.SelectMany(p => p.Features).Count(f => f.Enabled && !(f is StaticColorSettings) && !(f is PressSettings));
            var leds = device.TotalLeds;
            return (leds == 1 ? "1 LED" : $"{leds} LEDs") + " · " + (features == 1 ? "1 feature" : $"{features} features");
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    /// <summary>Item container → its 1-based position in the list (bind to ItemsControl.AlternationIndex, with a large AlternationCount).</summary>
    public class IndexToRankConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is int i ? (i + 1).ToString(CultureInfo.InvariantCulture) : string.Empty;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    /// <summary>null or empty string → Collapsed, anything else → Visible; pass "Inverse" as parameter to invert.</summary>
    public class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var visible = value != null && !(value is string text && string.IsNullOrWhiteSpace(text));
            if (string.Equals(parameter as string, "Inverse", StringComparison.OrdinalIgnoreCase)) visible = !visible;
            return visible ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    /// <summary>Path relative to the SimHub folder (a product picture or photo) → image; null when it can't be loaded.</summary>
    public class SimHubImageConverter : IValueConverter
    {
        private static readonly System.Collections.Generic.Dictionary<string, ImageSource> Cache = new System.Collections.Generic.Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var path = SimHubData.SimHubPaths.Resolve(value as string);
            if (path == null) return null;
            if (Cache.TryGetValue(path, out var cached)) return cached;

            ImageSource image = null;
            try
            {
                if (System.IO.File.Exists(path))
                {
                    var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bitmap.DecodePixelWidth = 240;
                    bitmap.UriSource = new Uri(path);
                    bitmap.EndInit();
                    bitmap.Freeze();
                    image = bitmap;
                }
            }
            catch (Exception)
            {
                // A broken picture just isn't shown.
            }

            Cache[path] = image;
            return image;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    /// <summary>Position in the wizard's wiring order → the group's preview color.</summary>
    public class WiringColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var index = value is int i ? i : 0;
            var brush = new SolidColorBrush(Theme.WiringColors[index % Theme.WiringColors.Length]);
            brush.Freeze();
            return brush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    /// <summary>Enum value → readable label ("HorizontalRow" → "Horizontal row").</summary>
    public class EnumDisplayConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return string.Empty;

            // A group without a side sits in the middle (and shows cars on both sides).
            if (value is LedGroupSide side && side == LedGroupSide.None) return "Center";
            var name = value.ToString();
            var builder = new System.Text.StringBuilder(name.Length + 4);
            for (var i = 0; i < name.Length; i++)
            {
                var c = name[i];
                if (i > 0 && char.IsUpper(c))
                {
                    builder.Append(' ');
                    c = char.ToLowerInvariant(c);
                }

                builder.Append(c);
            }

            var text = builder.ToString();
            return text.StartsWith("Led ", StringComparison.Ordinal) ? "LED" + text.Substring(3) : text;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    /// <summary>
    /// Name or (with <see cref="Description"/>) explanation of a flag animation, for the flags' drop-down.
    /// </summary>
    public class FlagOptionConverter : IValueConverter
    {
        /// <summary>Gives the explanation (for a tooltip) instead of the name.</summary>
        public bool Description { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            switch (value)
            {
                case FlagAnimation.Steady: return Description ? "A steady light" : "None";
                case FlagAnimation.Blink: return Description ? "Blinks on and off" : "Blink";
                case FlagAnimation.SwapColors: return Description ? "The two colors keep swapping places" : "Swap colors";
                default: return Description ? null : value?.ToString();
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
