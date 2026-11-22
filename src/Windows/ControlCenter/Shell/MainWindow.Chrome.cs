using LumaWall.Contracts;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using System.Runtime.InteropServices;
using Windows.Foundation;
using Windows.Graphics;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private InputNonClientPointerSource? shellNonClientInput;
    private bool shellChromeClosed;
    private bool shellChromeLayoutQueued;
    private double shellRasterizationScale = double.NaN;
    private RectInt32[]? shellAppliedInputRects;
    private (bool Maximized, bool Minimizable, bool Maximizable)? shellCaptionState;
    [DllImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostShellCommand(nint window, uint message, nuint command, nint data);

    private void InitializeWindowChrome()
    {
        InitializeShellIcon();
        if (!AppWindowTitleBar.IsCustomizationSupported()) return;

        ExtendsContentIntoTitleBar = true;
        SystemBackdrop=new TransparentShellBackdrop();
        SetTitleBar(ShellTitleDragRegion);
        // Hide the native caption buttons themselves, not just their paint.
        // Keeping the overlapped presenter preserves resize borders and dragging.
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Collapsed;
        ApplyShellIcon();
        ShellCaptionButtons.Visibility = Visibility.Visible;
        shellNonClientInput = InputNonClientPointerSource.GetForWindowId(AppWindow.Id);
        InitializeShellResizeGrips();
        ShellHeaderBorder.SizeChanged += (_, _) => QueueShellChromeLayout();
        ShellScreenCapsule.SizeChanged += (_, _) => QueueShellChromeLayout();
        ShellTitleDragRegion.SizeChanged += (_, _) => QueueShellChromeLayout();
        Root.LayoutUpdated += ShellBranding_LayoutUpdated;
        Root.Loaded += (_, _) => {
            if (Root.XamlRoot is { } root) {
                shellRasterizationScale = root.RasterizationScale;
                root.Changed += ShellXamlRoot_Changed;
            }
            QueueShellChromeLayout();
            UpdateShellCaptionState();
        };
        AppWindow.Changed += ShellWindow_Changed;
        Closed += (_, _) => {
            shellChromeClosed = true;
            shellChromeLayoutQueued = false;
            shellAppliedInputRects = null;
            Root.LayoutUpdated -= ShellBranding_LayoutUpdated;
            shellWindowRegion?.Dispose(); shellWindowRegion = null;
            if (Root.XamlRoot is { } root) root.Changed -= ShellXamlRoot_Changed;
            AppWindow.Changed -= ShellWindow_Changed;
            shellNonClientInput = null;
        };
        UpdateShellCaptionState();
    }

    private void ShellXamlRoot_Changed(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        if (shellChromeClosed || shellRasterizationScale == sender.RasterizationScale) return;
        shellRasterizationScale = sender.RasterizationScale;DismissHelp();
        shellAppliedInputRects = null;
        ApplyShellIcon();
        QueueShellChromeLayout();
    }
    private void ShellWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (shellChromeClosed) return;
        // Moving a window does not change its client-relative hit regions or
        // caption. DPI changes arrive independently through XamlRoot.Changed.
        if (!args.DidSizeChange && !args.DidPresenterChange) return;
        if (args.DidPresenterChange) shellAppliedInputRects = null;
        // Maximize/restore may report a size change on the same presenter.
        UpdateShellCaptionState();
        QueueShellChromeLayout();
    }

    private void QueueShellChromeLayout()
    {
        if (shellChromeClosed || shellChromeLayoutQueued) return;
        shellChromeLayoutQueued = true;
        if (!DispatcherQueue.TryEnqueue(RefreshShellChromeLayout)) shellChromeLayoutQueued = false;
    }

    private void RefreshShellChromeLayout()
    {
        shellChromeLayoutQueued = false;
        if (shellChromeClosed) return;
        // Header size changes can cascade through several SizeChanged events.
        // Let XAML arrange normally, with at most one pending shell refresh.
        UpdateShellWordmark();
        UpdateShellInputRegions();
    }

    private void UpdateShellInputRegions()
    {
        if (shellChromeClosed || shellNonClientInput == null || Root.XamlRoot is not { } root) return;
        double scale = root.RasterizationScale;
        var rectangles = new List<RectInt32>();
        foreach (FrameworkElement element in new FrameworkElement[] { ShellScreenCapsule, ShellCaptionButtons })
        {
            if (element.ActualWidth <= 0 || element.ActualHeight <= 0) continue;
            Rect bounds = element.TransformToVisual(ShellViewport).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            int x = (int)Math.Floor(bounds.X * scale), y = (int)Math.Floor(bounds.Y * scale);
            rectangles.Add(new RectInt32(x, y, (int)Math.Ceiling(bounds.Right * scale) - x, (int)Math.Ceiling(bounds.Bottom * scale) - y));
        }
        // Route edge grips through XAML too: the content input HWND can consume
        // pointer input before the outer HWND's WM_NCHITTEST sees it.
        rectangles.AddRange(shellResizeInputRects);
        // The title remains draggable outside these interactive regions.
        var next = rectangles.ToArray();
        if (shellAppliedInputRects != null && shellAppliedInputRects.SequenceEqual(next)) return;
        shellNonClientInput.SetRegionRects(NonClientRegionKind.Passthrough, next);
        shellAppliedInputRects = next;
    }

    private void UpdateShellCaptionState()
    {
        if (AppWindow.Presenter is not OverlappedPresenter presenter) return;
        bool maximized = presenter.State == OverlappedPresenterState.Maximized;
        var state = (maximized, presenter.IsMinimizable, presenter.IsMaximizable);
        if (shellCaptionState == state) return;
        shellCaptionState = state;
        shellAppliedInputRects = null;
        ShellMaximizeButton.GlyphKind = maximized ? "Restore" : "Maximize";
        AutomationProperties.SetName(ShellMaximizeButton, maximized ? UiLanguage.Text("向下还原") : UiLanguage.Text("最大化"));
        
        ShellMinimizeButton.IsEnabled = presenter.IsMinimizable;
        ShellMaximizeButton.IsEnabled = presenter.IsMaximizable;
    }

    private void ShellMinimize_Click(object sender, RoutedEventArgs e)
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter) presenter.Minimize();
    }

    private void ShellMaximize_Click(object sender, RoutedEventArgs e)
    {
        if (AppWindow.Presenter is not OverlappedPresenter presenter) return;
        if (presenter.State == OverlappedPresenterState.Maximized) presenter.Restore();
        else presenter.Maximize();
    }

    // Preserve AppWindow.Closing's existing save/hide-to-tray behavior.
    private void ShellClose_Click(object sender, RoutedEventArgs e)
    {
        PostShellCommand(WinRT.Interop.WindowNative.GetWindowHandle(this), 0x0112, 0xF060, 0);
    }
}
