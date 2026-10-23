using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FreeTheLights.Core;

namespace FreeTheLights.UI
{
    /// <summary>
    /// Small color picker: a swatch button that opens presets, RGB sliders and a hex field.
    /// Bound through <see cref="ColorHex"/> ("#AARRGGBB").
    /// </summary>
    public partial class ColorPickerButton : UserControl
    {
        public static readonly DependencyProperty ColorHexProperty = DependencyProperty.Register(
            nameof(ColorHex),
            typeof(string),
            typeof(ColorPickerButton),
            new FrameworkPropertyMetadata("#FFFFFFFF", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, e) => ((ColorPickerButton)d).OnColorChanged()));

        public static readonly DependencyProperty AllowNoneProperty = DependencyProperty.Register(
            nameof(AllowNone),
            typeof(bool),
            typeof(ColorPickerButton),
            new PropertyMetadata(false, (d, e) => ((ColorPickerButton)d).OnColorChanged()));

        private static readonly PresetColor[] PresetColors =
        {
            new PresetColor("Off (dark)", "#FF000000"),
            new PresetColor("Red", "#FFFF0000"),
            new PresetColor("Orange", "#FFFF6000"),
            new PresetColor("Amber", "#FFFFA000"),
            new PresetColor("Yellow", "#FFFFD000"),
            new PresetColor("Lime", "#FF80FF00"),
            new PresetColor("Green", "#FF00FF00"),
            new PresetColor("Cyan", "#FF00FFFF"),
            new PresetColor("Blue", "#FF0040FF"),
            new PresetColor("Purple", "#FF8000FF"),
            new PresetColor("Magenta", "#FFFF00FF"),
            new PresetColor("Pink", "#FFFF60A0"),
            new PresetColor("White", "#FFFFFFFF"),
        };

        private bool updating;

        public ColorPickerButton()
        {
            InitializeComponent();
            Presets.ItemsSource = PresetColors;
            OnColorChanged();
        }

        public string ColorHex
        {
            get => (string)GetValue(ColorHexProperty);
            set => SetValue(ColorHexProperty, value);
        }

        /// <summary>Offers "no color" (an empty <see cref="ColorHex"/>), used for the optional second flag color.</summary>
        public bool AllowNone
        {
            get => (bool)GetValue(AllowNoneProperty);
            set => SetValue(AllowNoneProperty, value);
        }

        private void OnColorChanged()
        {
            if (Swatch == null) return;
            var none = string.IsNullOrWhiteSpace(ColorHex);
            NoneButton.Visibility = AllowNone ? Visibility.Visible : Visibility.Collapsed;
            NoneLabel.Visibility = none ? Visibility.Visible : Visibility.Collapsed;
            Swatch.Visibility = none ? Visibility.Collapsed : Visibility.Visible;

            var c = none ? LedColor.Off : LedColor.Parse(ColorHex);
            Swatch.Background = new SolidColorBrush(Color.FromArgb(c.A, c.R, c.G, c.B));

            updating = true;
            RedSlider.Value = c.R;
            GreenSlider.Value = c.G;
            BlueSlider.Value = c.B;
            HexBox.Text = none ? string.Empty : c.ToHex();
            updating = false;
        }

        private void Preset_Click(object sender, RoutedEventArgs e)
        {
            ColorHex = (string)((FrameworkElement)sender).Tag;
        }

        private void None_Click(object sender, RoutedEventArgs e)
        {
            ColorHex = string.Empty;
            PickerPopup.IsOpen = false;
        }

        private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (updating || RedSlider == null || GreenSlider == null || BlueSlider == null) return;
            ColorHex = LedColor.FromRgb((byte)RedSlider.Value, (byte)GreenSlider.Value, (byte)BlueSlider.Value).ToHex();
        }

        private void HexBox_Commit(object sender, RoutedEventArgs e)
        {
            if (updating) return;
            var text = HexBox.Text.Trim();

            // An empty box means "no color" where that's allowed; it must never turn into black.
            if (text.Length == 0 && AllowNone) ColorHex = string.Empty;
            else if (LedColor.TryParse(text, out var color)) ColorHex = color.ToHex();
            else OnColorChanged();
        }

        private void HexBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) HexBox_Commit(sender, e);
        }

        private sealed class PresetColor
        {
            public PresetColor(string name, string hex)
            {
                Name = name;
                Hex = hex;
                var c = LedColor.Parse(hex);
                Brush = new SolidColorBrush(Color.FromArgb(c.A, c.R, c.G, c.B));
                Brush.Freeze();
            }

            public string Name { get; }
            public string Hex { get; }
            public SolidColorBrush Brush { get; }
        }
    }
}
