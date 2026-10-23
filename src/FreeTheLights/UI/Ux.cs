using System.Windows;
using System.Windows.Media;
using MahApps.Metro.IconPacks;

namespace FreeTheLights.UI
{
    /// <summary>
    /// Attached properties read by the templates in Theme.xaml: the icon and accent color of chips, tabs,
    /// icon buttons and feature headers, an optional on/off status dot and a description line.
    /// </summary>
    public static class Ux
    {
        public static readonly DependencyProperty IconProperty = DependencyProperty.RegisterAttached(
            "Icon", typeof(PackIconMaterialKind), typeof(Ux), new FrameworkPropertyMetadata(PackIconMaterialKind.None));

        /// <summary>Inherited, so the chips on a feature's tab take that feature's color unless they set their own.</summary>
        public static readonly DependencyProperty AccentProperty = DependencyProperty.RegisterAttached(
            "Accent", typeof(Brush), typeof(Ux), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits));

        /// <summary>null = no status dot, true = feature on, false = feature off.</summary>
        public static readonly DependencyProperty StatusProperty = DependencyProperty.RegisterAttached(
            "Status", typeof(bool?), typeof(Ux), new FrameworkPropertyMetadata(null));

        public static readonly DependencyProperty DescriptionProperty = DependencyProperty.RegisterAttached(
            "Description", typeof(string), typeof(Ux), new FrameworkPropertyMetadata(null));

        public static PackIconMaterialKind GetIcon(DependencyObject d) => (PackIconMaterialKind)d.GetValue(IconProperty);

        public static void SetIcon(DependencyObject d, PackIconMaterialKind value) => d.SetValue(IconProperty, value);

        public static Brush GetAccent(DependencyObject d) => (Brush)d.GetValue(AccentProperty);

        public static void SetAccent(DependencyObject d, Brush value) => d.SetValue(AccentProperty, value);

        public static bool? GetStatus(DependencyObject d) => (bool?)d.GetValue(StatusProperty);

        public static void SetStatus(DependencyObject d, bool? value) => d.SetValue(StatusProperty, value);

        public static string GetDescription(DependencyObject d) => (string)d.GetValue(DescriptionProperty);

        public static void SetDescription(DependencyObject d, string value) => d.SetValue(DescriptionProperty, value);
    }
}
