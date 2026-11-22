using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace LumaWall.ControlCenter;

// A realized card requests just one size bucket. Layout/DPI changes are
// coalesced; recycled and hidden cards release their image references.
public sealed class LibraryPosterImage : Grid
{
    public static readonly DependencyProperty PathProperty = DependencyProperty.Register(
        nameof(Path), typeof(string), typeof(LibraryPosterImage), new PropertyMetadata(null, Changed));
    public static readonly DependencyProperty RevisionProperty = DependencyProperty.Register(
        nameof(Revision), typeof(int), typeof(LibraryPosterImage), new PropertyMetadata(0, Changed));
    public static readonly DependencyProperty AspectRatioProperty = DependencyProperty.Register(
        nameof(AspectRatio), typeof(double), typeof(LibraryPosterImage), new PropertyMetadata(1.0, Changed));
    public string? Path { get => (string?)GetValue(PathProperty); set => SetValue(PathProperty, value); }
    public int Revision { get => (int)GetValue(RevisionProperty); set => SetValue(RevisionProperty, value); }
    public double AspectRatio { get => (double)GetValue(AspectRatioProperty); set => SetValue(AspectRatioProperty, value); }
    private readonly Image image = new() { Stretch = Stretch.UniformToFill };
    private XamlRoot? subscribedRoot;
    private bool active = true, queued;
    private (string?, int, int) loaded;

    public LibraryPosterImage()
    {
        IsHitTestVisible = false;
        Children.Add(image);
        Loaded += (_, _) => {
            subscribedRoot = XamlRoot;
            if (subscribedRoot != null) subscribedRoot.Changed += RootChanged;
            for (DependencyObject? parent = this; parent != null; parent = VisualTreeHelper.GetParent(parent))
                if (parent is LibraryPage page) { active = page.IsActive; break; }
            QueueLoad();
        };
        Unloaded += (_, _) => {
            if (subscribedRoot != null) subscribedRoot.Changed -= RootChanged;
            subscribedRoot = null; image.Source = null; loaded = default;
        };
        SizeChanged += (_, _) => QueueLoad();
    }
    private static void Changed(DependencyObject sender, DependencyPropertyChangedEventArgs args) => ((LibraryPosterImage)sender).QueueLoad();
    private void RootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => QueueLoad();
    internal void SetActive(bool value)
    {
        active = value;
        if (!value) { image.Source = null; loaded = default; }
        else QueueLoad();
    }
    private void QueueLoad()
    {
        if (queued || !active || !IsLoaded) return;
        queued = DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => {
            queued = false;
            if (!active || !IsLoaded || ActualWidth <= 0 || ActualHeight <= 0) return;
            double aspect = double.IsFinite(AspectRatio) && AspectRatio > 0 ? AspectRatio : 1;
            int width = PosterImageCache.SizeBucket(Math.Max(ActualWidth, ActualHeight * aspect) * (XamlRoot?.RasterizationScale ?? 1));
            var key = (Path, width, Revision);
            if (key == loaded) return;
            loaded = key;
            image.Source = PosterImageCache.Load(Path, width, aspect);
        });
    }
}
