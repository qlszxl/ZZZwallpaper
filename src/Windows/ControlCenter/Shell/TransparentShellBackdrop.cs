using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace LumaWall.ControlCenter;

// The window keeps one native surface; transparent edge pixels are blended by
// DWM instead of being thresholded into a jagged, one-bit window silhouette.
public sealed class TransparentShellBackdrop : SystemBackdrop
{
    private Windows.UI.Composition.Compositor? compositor;
    private Windows.UI.Composition.CompositionColorBrush? brush;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target,XamlRoot root)
    {
        // WinUI's queue and Windows.System.DispatcherQueue are distinct. The
        // system compositor needs the latter on this same UI thread; let the
        // existing WinUI queue create it and own its shutdown lifetime.
        var dispatcher=Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("The transparent backdrop must connect on the UI thread.");
        dispatcher.EnsureSystemDispatcherQueue();
        base.OnTargetConnected(target,root);
        compositor=new Windows.UI.Composition.Compositor();
        brush=compositor.CreateColorBrush(Windows.UI.Color.FromArgb(0,0,0,0));
        target.SystemBackdrop=brush;
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target)
    {
        target.SystemBackdrop=null;
        brush?.Dispose();brush=null;compositor?.Dispose();compositor=null;
        base.OnTargetDisconnected(target);
    }
}
