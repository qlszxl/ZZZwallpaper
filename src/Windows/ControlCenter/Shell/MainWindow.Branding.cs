using LumaWall.DesktopIntegration;
using Microsoft.UI.Xaml;
using Windows.Foundation;
using PixelRect = System.Drawing.Rectangle;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private bool shellBrandingLayout;
    private WordmarkWindowRegion? shellWindowRegion;
    private bool shellWindowRegionUnavailable;
    private (Size Viewport, Size Root, Size Body, Size Logo, double DragWidth, double Inset, double Dpi)? shellBrandingMetrics;
    private double shellHeaderHeadroom = double.NaN;
    private const double ShellWordmarkWidth = 670;
    private const double ShellWordmarkAspect = 336.0 / 2071.0;

    private void UpdateShellWordmark()
    {
        if(shellChromeClosed || shellBrandingLayout || !Root.IsLoaded) return;
        shellBrandingLayout = true;
        try
        {
            double width = Math.Min(ShellWordmarkWidth, Math.Max(0, ShellTitleDragRegion.ActualWidth));
            double height = width * ShellWordmarkAspect;
            // Keep an 80-DIP toolbar below the protrusion. The extra headroom is
            // inside this window, so the complete image shares its DWM animation.
            double headroom = Math.Max(0, height - 78);
            if (shellHeaderHeadroom == headroom) return;
            shellHeaderHeadroom = headroom;
            ShellHeaderRow.Height = new GridLength(80 + headroom);
            var inset = new Thickness(0, headroom, 0, 0);
            ShellHeaderBorder.Margin = inset;
            ShellBackground.Margin = inset;
            ShellScreenCapsule.Margin = inset;
            ShellCaptionButtons.Margin = inset;
        }
        finally { shellBrandingLayout = false; }
    }

    private void ShellBranding_LayoutUpdated(object? sender, object e)
    {
        if(shellChromeClosed || shellWindowRegionUnavailable || !Root.IsLoaded || Root.XamlRoot is not { } root ||
            ShellWordmarkVisual.ActualWidth <= 0 || ShellWordmarkVisual.ActualHeight <= 0 ||
            ShellBackground.ActualWidth <= 0 || ShellBackground.ActualHeight <= 0) return;
        static Size SizeOf(FrameworkElement element) => new(element.ActualWidth, element.ActualHeight);
        var metrics = (SizeOf(ShellViewport), SizeOf(Root), SizeOf(ShellBackground), SizeOf(ShellWordmarkVisual),
            ShellTitleDragRegion.ActualWidth, ShellBackground.Margin.Top, root.RasterizationScale);
        if(shellBrandingMetrics==metrics)return;
        shellBrandingMetrics=metrics;
        try
        {
            double dpi = root.RasterizationScale;
            var logo = ShellWordmarkVisual.TransformToVisual(ShellViewport).TransformBounds(
                new Rect(0, 0, ShellWordmarkVisual.ActualWidth, ShellWordmarkVisual.ActualHeight));
            var body = ShellBackground.TransformToVisual(ShellViewport).TransformBounds(
                new Rect(0, 0, ShellBackground.ActualWidth, ShellBackground.ActualHeight));
            // The opaque background fills every pixel inside the native rounded region.
            // Round inward at fractional DPI so the region never exposes the window backing.
            var bodyPixels = PixelRect.FromLTRB((int)Math.Ceiling(body.Left * dpi), (int)Math.Ceiling(body.Top * dpi),
                (int)Math.Floor(body.Right * dpi), (int)Math.Floor(body.Bottom * dpi));
            var logoPixels = PixelRect.FromLTRB((int)Math.Round(logo.Left * dpi), (int)Math.Round(logo.Top * dpi),
                (int)Math.Round(logo.Right * dpi), (int)Math.Round(logo.Bottom * dpi));
            shellWindowRegion ??= new WordmarkWindowRegion(WinRT.Interop.WindowNative.GetWindowHandle(this),
                Path.Combine(AppContext.BaseDirectory, "Assets", "Branding", "wordmark.png"));
            shellWindowRegion.Update(bodyPixels, logoPixels, dpi * body.Width / ShellBackground.ActualWidth);
        }
        catch(Exception ex)
        {
            shellWindowRegionUnavailable = true;
            shellWindowRegion?.Dispose(); shellWindowRegion = null;
            Program.WriteRuntimeError(ex);
        }
    }
}
