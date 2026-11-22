using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using LumaWall.Contracts;

namespace ZZZwallpaper.InstallerPreview;

public static class ThemeProperties
{
    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.RegisterAttached(
        "CornerRadius", typeof(CornerRadius), typeof(ThemeProperties), new PropertyMetadata(new CornerRadius(24)));
    public static void SetCornerRadius(DependencyObject target, CornerRadius value) => target.SetValue(CornerRadiusProperty, value);
    public static CornerRadius GetCornerRadius(DependencyObject target) => (CornerRadius)target.GetValue(CornerRadiusProperty);
}

// Same concentric-corner rule as ControlCenter/InsetCornerConverter.cs.
public sealed class InsetCornerConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var corner = (CornerRadius)value;
        var inset = double.Parse((string)parameter, CultureInfo.InvariantCulture);
        return new CornerRadius(Math.Max(0, corner.TopLeft - inset), Math.Max(0, corner.TopRight - inset),
            Math.Max(0, corner.BottomRight - inset), Math.Max(0, corner.BottomLeft - inset));
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

internal static class SoftwareTheme
{
    private sealed record PaletteColor(Color Color);
    public static SkinPalette Palette => SkinPalette.Find("lime");
    public static Color ColorOf(string hex) => (Color)ColorConverter.ConvertFromString(hex);
    public static SolidColorBrush Selection => (SolidColorBrush)Application.Current.Resources["HomeSelectionBrush"];

    // Port of the default palette application in MainWindow.Appearance.cs.
    // Uses the shared palette source; never reads or modifies the app's settings.
    public static void ApplyDefaultPalette()
    {
        var skin = Palette;
        var accent = ColorOf(skin.Accent);
        var surface = ColorOf(skin.Surface);
        static Color Blend(Color color, Color other, double amount) => Color.FromRgb(
            (byte)(color.R * (1 - amount) + other.R * amount),
            (byte)(color.G * (1 - amount) + other.G * amount),
            (byte)(color.B * (1 - amount) + other.B * amount));
        void Set(string key, Color color) => Application.Current.Resources[key] = new SolidColorBrush(color);
        foreach (var key in new[] { "HomeSelectionBrush", "HomeControlAccent", "EffectsAccent", "Accent", "ShellCaptionAccent" }) Set(key, accent);
        // A bound color keeps WPF templates from freezing this shared brush.
        // The main app animates the selection color; WPF requires a mutable brush.
        var selection = new SolidColorBrush();
        BindingOperations.SetBinding(selection, SolidColorBrush.ColorProperty, new Binding(nameof(PaletteColor.Color))
        {
            Source = new PaletteColor(accent), Mode = BindingMode.OneWay
        });
        Application.Current.Resources["HomeSelectionBrush"] = selection;
        Set("HomeControlAccentHover", Blend(accent, Colors.White, .28));
        Set("HomeControlAccentPressed", Blend(accent, Colors.Black, .18));
        Set("ShellCaptionAccentPressed", Blend(accent, Colors.Black, .18));
        Set("HomeControlSurface", surface);
        Set("HomeControlSurfaceHover", Blend(surface, Colors.White, .05));
        Set("HomeControlSurfacePressed", Blend(surface, Colors.Black, .25));
        foreach (var key in new[] { "HomeWellBrush", "ShellCaptionWell" })
        {
            var brush = ((LinearGradientBrush)Application.Current.Resources[key]).Clone();
            brush.GradientStops[0].Color = Blend(surface, Colors.Black, .72);
            brush.GradientStops[1].Color = Blend(surface, Colors.Black, .25);
            brush.GradientStops[2].Color = Blend(surface, Colors.White, .025);
            Application.Current.Resources[key] = brush;
        }
    }
}
