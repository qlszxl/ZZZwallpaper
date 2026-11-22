using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace LumaWall.ControlCenter;

// The approved artwork is a source atlas, never a stretched screenshot of the UI.
// Only the label, molded relief and an empty patch of plastic are sampled from it.
public sealed class CassetteSlice : Grid
{
    private static BitmapImage? atlas;
    private readonly Canvas crop = new();
    private readonly Image image;
    private readonly Viewbox viewport;
    public static readonly DependencyProperty PartProperty = DependencyProperty.Register(
        nameof(Part), typeof(string), typeof(CassetteSlice), new PropertyMetadata("Label", Changed));
    public string Part { get => (string)GetValue(PartProperty); set => SetValue(PartProperty, value); }

    public CassetteSlice()
    {
        IsHitTestVisible = false;
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        atlas ??= new BitmapImage(new Uri("ms-appx:///Assets/Cassette/sidebar-surface.png"));
        image = new Image { Source = atlas, Width = 768, Height = 2048, Stretch = Stretch.Fill };
        crop.Children.Add(image);
        viewport = new Viewbox { Stretch = Stretch.Uniform, Child = crop };
        Children.Add(viewport);
        SizeChanged += (_, e) => Clip = new RectangleGeometry { Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height) };
        UpdateCrop();
    }

    private static void Changed(DependencyObject sender, DependencyPropertyChangedEventArgs args) => ((CassetteSlice)sender).UpdateCrop();
    private void UpdateCrop()
    {
        if (image == null) return;
        var source = Part switch {
            "Relief" => new Rect(158, 1246, 450, 432),
            "Grain" => new Rect(322, 1184, 128, 64),
            _ => new Rect(158, 79, 451, 133)
        };
        crop.Width = source.Width; crop.Height = source.Height;
        crop.Clip = new RectangleGeometry { Rect = new Rect(0, 0, source.Width, source.Height) };
        Canvas.SetLeft(image, -source.X); Canvas.SetTop(image, -source.Y);
        viewport.Stretch = Part == "Grain" ? Stretch.Fill : Stretch.Uniform;
        viewport.VerticalAlignment = Part == "Relief" ? VerticalAlignment.Bottom : VerticalAlignment.Center;
    }
}

// Tile at a constant size so resizing the casing does not stretch its grain.
public sealed class CassetteGrain : Canvas
{
    private int columns, rows;
    public CassetteGrain()
    {
        IsHitTestVisible = false;
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        SizeChanged += (_, e) => {
            Clip = new RectangleGeometry { Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height) };
            int nextColumns = (int)Math.Ceiling(e.NewSize.Width / 128), nextRows = (int)Math.Ceiling(e.NewSize.Height / 64);
            if (columns == nextColumns && rows == nextRows) return;
            columns = nextColumns; rows = nextRows; Children.Clear();
            for (int y = 0; y < rows; y++) for (int x = 0; x < columns; x++) {
                var tile = new CassetteSlice { Part = "Grain", Width = 128, Height = 64 };
                SetLeft(tile, x * 128); SetTop(tile, y * 64); Children.Add(tile);
            }
        };
    }
}

public sealed class CassetteClipPanel : Grid
{
    public CassetteClipPanel() => SizeChanged += (_, e) =>
        Clip = new RectangleGeometry { Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height) };
}

public sealed class CassetteNavButton : Button
{
    public static readonly DependencyProperty NumberProperty = DependencyProperty.Register(nameof(Number), typeof(string), typeof(CassetteNavButton), new PropertyMetadata(""));
    public static readonly DependencyProperty WatermarkVisibilityProperty = DependencyProperty.Register(nameof(WatermarkVisibility), typeof(Visibility), typeof(CassetteNavButton), new PropertyMetadata(Visibility.Visible));
    public static readonly DependencyProperty SelectedMarksVisibilityProperty = DependencyProperty.Register(nameof(SelectedMarksVisibility), typeof(Visibility), typeof(CassetteNavButton), new PropertyMetadata(Visibility.Collapsed));
    public string Number { get => (string)GetValue(NumberProperty); set => SetValue(NumberProperty, value); }
    public Visibility WatermarkVisibility { get => (Visibility)GetValue(WatermarkVisibilityProperty); set => SetValue(WatermarkVisibilityProperty, value); }
    public Visibility SelectedMarksVisibility { get => (Visibility)GetValue(SelectedMarksVisibilityProperty); set => SetValue(SelectedMarksVisibilityProperty, value); }
    public void SetNavigationState(bool selected, bool compact)
    {
        WatermarkVisibility = selected || compact ? Visibility.Collapsed : Visibility.Visible;
        SelectedMarksVisibility = selected ? Visibility.Visible : Visibility.Collapsed;
        Background = (Brush)Microsoft.UI.Xaml.Application.Current.Resources[selected ? "InteractionAccentBrush" : "CassetteButtonBrush"];
        Foreground = new SolidColorBrush(selected ? Color.FromArgb(255, 13, 15, 0) : Color.FromArgb(255, 239, 237, 229));
    }
}

public sealed class CassetteScrew : Grid
{
    public CassetteScrew()
    {
        Width = Height = 24; IsHitTestVisible = false;
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        var metal = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(.8, 1) };
        metal.GradientStops.Add(new GradientStop { Offset = 0, Color = Color.FromArgb(255, 104, 104, 101) });
        metal.GradientStops.Add(new GradientStop { Offset = .48, Color = Color.FromArgb(255, 47, 47, 46) });
        metal.GradientStops.Add(new GradientStop { Offset = 1, Color = Color.FromArgb(255, 19, 19, 19) });
        Children.Add(new Ellipse { Fill = new SolidColorBrush(Color.FromArgb(255, 2, 2, 2)), Stroke = new SolidColorBrush(Color.FromArgb(255, 49, 49, 49)), StrokeThickness = 1 });
        Children.Add(new Ellipse { Fill = metal, Stroke = new SolidColorBrush(Color.FromArgb(255, 114, 114, 110)), StrokeThickness = .75, Margin = new Thickness(2) });
        var slot = new SolidColorBrush(Color.FromArgb(255, 4, 4, 4));
        Children.Add(new Border { Width = 14, Height = 4, CornerRadius = new CornerRadius(1), Background = slot, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        Children.Add(new Border { Width = 4, Height = 14, CornerRadius = new CornerRadius(1), Background = slot, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
    }
}
