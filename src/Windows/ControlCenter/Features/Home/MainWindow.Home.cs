using LumaWall.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private bool homeChromeReady, homeChromeSelected, homeMotionShutdown;
    private BrandBackdrop? homeBrandBackdrop;
    private bool homeBrandBackdropFailed;
    private readonly Dictionary<(DependencyObject Target, DependencyProperty Property), object> homeOverrides = new();
    private readonly Dictionary<Button, UIElement> originalHomeNavigationIcons = new();
    private readonly Dictionary<Button, Viewbox> homeNavigationIcons = new();
    private Viewbox? homeScreenVector;
    private UISettings? homeUiSettings;
    private bool? homePauseDisplayed;

    private static Style HomeStyle(string key) => (Style)Microsoft.UI.Xaml.Application.Current.Resources[key];
    private static PathGeometry HomeGeometry(string name) => CloneIconGeometry("HomeIcon" + name);
    private static PathGeometry CloneIconGeometry(string resourceKey)
    {
        // WinUI Geometry instances cannot belong to multiple PathIcons. Treat resources
        // as prototypes, including when navigation creates another icon on a later visit.
        var source = (PathGeometry)Microsoft.UI.Xaml.Application.Current.Resources[resourceKey];
        var geometry = new PathGeometry { FillRule = source.FillRule };
        foreach (var sourceFigure in source.Figures)
        {
            var figure = new PathFigure {
                StartPoint = sourceFigure.StartPoint, IsClosed = sourceFigure.IsClosed, IsFilled = sourceFigure.IsFilled
            };
            foreach (var segment in sourceFigure.Segments)
                figure.Segments.Add(segment switch {
                    LineSegment line => new LineSegment { Point = line.Point },
                    QuadraticBezierSegment curve => new QuadraticBezierSegment { Point1 = curve.Point1, Point2 = curve.Point2 },
                    BezierSegment curve => new BezierSegment { Point1 = curve.Point1, Point2 = curve.Point2, Point3 = curve.Point3 },
                    _ => throw new NotSupportedException($"Unsupported home icon segment: {segment.GetType().Name}")
                });
            geometry.Figures.Add(figure);
        }
        return geometry;
    }
    private static Brush HomeBrush(string key) => (Brush)Microsoft.UI.Xaml.Application.Current.Resources[key];

    private static Viewbox CreateHomeVectorIcon(string name, double size, DependencyObject foregroundSource) => CreateVectorIcon(HomeGeometry(name), size, foregroundSource);
    private static Viewbox CreateEffectsVectorIcon(string name, double size, DependencyObject foregroundSource) => CreateVectorIcon(CloneIconGeometry("EffectsIcon" + name), size, foregroundSource);
    private static Viewbox CreateVectorIcon(PathGeometry geometry, double size, DependencyObject foregroundSource)
    {
        // An explicit 24-DIP canvas keeps every glyph's proportions and stroke weight
        // consistent at small sizes. Each shape owns a fresh geometry instance.
        var shape = new Microsoft.UI.Xaml.Shapes.Path {
            Width = 24, Height = 24, Stretch = Stretch.None, Data = geometry, IsHitTestVisible = false
        };
        shape.SetBinding(Microsoft.UI.Xaml.Shapes.Shape.FillProperty,
            new Binding { Source = foregroundSource, Path = new PropertyPath("Foreground") });
        return new Viewbox { Width = size, Height = size, Stretch = Stretch.Uniform, Child = shape,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
    }

    private void InitializeHomeChrome()
    {
        foreach (var button in EffectsGlobalNavigation.Children.OfType<Button>())
        {
            originalHomeNavigationIcons[button] = ((StackPanel)button.Content).Children[0];
            string icon = (string)button.Tag switch {
                "wallpaper" => "Home", "library" => "Library", "display" => "Effects",
                "widgets" => "Widgets", "presets" => "Presets", "performance" => "Performance", "appearance" => "Appearance", "history" => "Queue", _ => "Settings"
            };
            homeNavigationIcons[button] = CreateHomeVectorIcon(icon, 24, button);
        }
        homeScreenVector = CreateHomeVectorIcon("Monitor", 24, ShellScreenIcon);
        homeUiSettings = new UISettings();
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            homeUiSettings.AnimationsEnabledChanged += HomeAnimations_Changed;
        foreach (var key in new[] { "ListViewItemBackgroundSelected", "ListViewItemBackgroundSelectedPointerOver", "ListViewItemBackgroundSelectedPressed" })
            PlaybackQueueList.Resources[key] = HomeBrush("InteractionAccentBrush");
        homeChromeReady = true;
        AppWindow.Changed += (_, args) => {
            if (args.DidVisibilityChange || args.DidPresenterChange || args.DidSizeChange)
                UpdateHomeMotionVisibility();
        };
        Activated += (_, _) => UpdateHomeMotionVisibility();
        Root.Loaded += (_, _) => { ResizeHomeBackdrop(); UpdateHomeMotionVisibility(); };
        Root.Unloaded += (_, _) => {StopHomeMotion();StopPageEntry();};
        Closed += (_, _) => {
            homeMotionShutdown = true; StopHomeMotion();StopPageEntry();
            homeBrandBackdrop?.Dispose(); homeBrandBackdrop = null;
            if (homeUiSettings != null && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
                homeUiSettings.AnimationsEnabledChanged -= HomeAnimations_Changed;
        };
    }

    private void HomeOverride(DependencyObject target, DependencyProperty property, object value)
    {
        var key = (target, property);
        if (!homeOverrides.ContainsKey(key)) homeOverrides[key] = target.ReadLocalValue(property);
        target.SetValue(property, value);
    }

    private void HomeSharedFrame(Border outer, Border inner, Thickness padding)
    {
        HomeOverride(outer, Border.BorderThicknessProperty, new Thickness(4));
        HomeOverride(outer, Border.CornerRadiusProperty, new CornerRadius(24));
        HomeOverride(outer, Border.BackgroundSizingProperty, BackgroundSizing.OuterBorderEdge);
        HomeOverride(outer, Border.BorderBrushProperty, HomeBrush("HomeRimBrush"));
        HomeOverride(outer, Border.BackgroundProperty, EffectsBrush(3, 3, 3));
        HomeOverride(outer, Border.PaddingProperty, new Thickness(2));
        HomeOverride(inner, Border.BorderThicknessProperty, new Thickness(1));
        HomeOverride(inner, Border.CornerRadiusProperty, new CornerRadius(18));
        HomeOverride(inner, Border.BackgroundSizingProperty, BackgroundSizing.OuterBorderEdge);
        HomeOverride(inner, Border.BorderBrushProperty, EffectsBrush(41, 41, 41));
        HomeOverride(inner, Border.BackgroundProperty, HomeBrush("HomeWellBrush"));
        HomeOverride(inner, Border.PaddingProperty, padding);
    }

    private void SetHomeChrome(string page)
    {
        if (!homeChromeReady) return;
        // Shell decoration is page-specific; the interaction accent remains
        // shared across every page and keeps its phase during navigation.
        homeChromeSelected = page is "wallpaper" or "library" or "display" or "widgets" or "presets" || IsSettingsPage(page);
        HomeAnimatedBackground.Visibility = homeChromeSelected ? Visibility.Visible : Visibility.Collapsed;
        ShellStaticBackground.Visibility = homeChromeSelected ? Visibility.Collapsed : Visibility.Visible;
        if (homeChromeSelected)
        {
            HomeSharedFrame(ShellFooter, ShellFooterInset, new Thickness(12, 3, 12, 3));
            HomeSharedFrame(ShellScreenCapsule, ShellScreenInset, new Thickness(10, 0, 0, 0));
            HomeOverride(ShellScreenCapsule, Border.BorderThicknessProperty, new Thickness(2));
            HomeOverride(ShellScreenCapsule, Border.CornerRadiusProperty, new CornerRadius(24));
            HomeOverride(ShellScreenInset, Border.CornerRadiusProperty, new CornerRadius(20));
            HomeOverride(ShellScreenInset, Border.BorderBrushProperty, EffectsBrush(4, 4, 4));
            // Reserve the closed selector's geometry so opening the popup cannot
            // remeasure its auto-width shell or nudge the neighbouring monitor icon.
            HomeOverride(ShellScreenCapsule, FrameworkElement.WidthProperty, 314d);
            HomeOverride(ShellScreenCapsule, FrameworkElement.HeightProperty, 56d);
            HomeOverride(ShellScreenCapsule, FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            HomeOverride(ScreenPicker, FrameworkElement.WidthProperty, 260d);
            HomeOverride(ScreenPicker, FrameworkElement.HeightProperty, 44d);
            HomeOverride(ScreenPicker, FrameworkElement.MinHeightProperty, 44d);
            HomeOverride(ScreenPicker, FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            HomeOverride(ShellScreenIcon, FrameworkElement.WidthProperty, 24d);
            HomeOverride(ShellScreenIcon, FrameworkElement.HeightProperty, 24d);
            HomeOverride(ShellScreenIcon, FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            HomeOverride(ShellHeaderBorder, Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(110, 8, 8, 8)));
            HomeOverride(ShellHeaderBorder, Border.BorderBrushProperty, EffectsBrush(25, 25, 25));
            HomeOverride(ShellHeaderBorder, Border.BorderThicknessProperty, new Thickness(1, 1, 1, 3));
            HomeOverride(ScreenPicker, FrameworkElement.StyleProperty, HomeStyle("HomeScreenComboBox"));
            HomeOverride(ScreenPicker, Control.FontFamilyProperty, UiFonts.Display);
            HomeOverride(CancelImportButton, FrameworkElement.StyleProperty, HomeStyle("HomeCompactButton"));
            HomeOverride(CancelImportButton, Control.PaddingProperty, new Thickness(10, 0, 10, 0));
            HomeOverride(Notice, FrameworkElement.StyleProperty, HomeStyle("HomeInfoBar"));
            foreach (var button in EffectsGlobalNavigation.Children.OfType<Button>())
            {
                HomeOverride(button, FrameworkElement.StyleProperty, HomeStyle("CassetteNavButtonStyle"));
                var content = (StackPanel)button.Content;
                if (content.Children[0] != homeNavigationIcons[button])
                {
                    content.Children.RemoveAt(0); content.Children.Insert(0, homeNavigationIcons[button]);
                }
                button.Background = (string)button.Tag == MainNavigationPage(page) ? HomeBrush("InteractionAccentBrush") : new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
            }
            var screenContent = (StackPanel)ShellScreenInset.Child;
            if (homeScreenVector != null && screenContent.Children[0] != homeScreenVector)
            {
                screenContent.Children.RemoveAt(0); screenContent.Children.Insert(0, homeScreenVector);
            }
            ResizeHomeBackdrop();
        }
        else
        {
            foreach (var ((target, property), original) in homeOverrides)
            {
                if (original == DependencyProperty.UnsetValue) target.ClearValue(property);
                else target.SetValue(property, original);
            }
            homeOverrides.Clear();
            foreach (var (button, icon) in originalHomeNavigationIcons)
            {
                var content = (StackPanel)button.Content;
                if (content.Children[0] != icon) { content.Children.RemoveAt(0); content.Children.Insert(0, icon); }
            }
            var screenContent = (StackPanel)ShellScreenInset.Child;
            if (screenContent.Children[0] != ShellScreenIcon)
            {
                screenContent.Children.RemoveAt(0); screenContent.Children.Insert(0, ShellScreenIcon);
            }
        }
        LayoutCassetteNavigation(Root.ActualWidth);
        UpdateCassetteSelection(page);
        UpdateHomeMotionVisibility();
    }

    private void RefreshHomePause(bool paused)
    {
        if (homePauseDisplayed == paused) return;
        HomePauseLabel.Text = paused ? UiLanguage.Text("继续播放") : UiLanguage.Text("暂停壁纸");
        HomePauseIcon.Data = HomeGeometry(paused ? "Play" : "Pause");
        AutomationProperties.SetName(PauseButton, HomePauseLabel.Text);
        homePauseDisplayed = paused;
    }

    private void ResizeHomeBackdrop()
    {
        if (!homeChromeReady || !homeChromeSelected || homeMotionShutdown) return;
        double width = ShellBackground.ActualWidth, height = ShellBackground.ActualHeight;
        if (width <= 0 || height <= 0) return;
        HomeBackgroundClip.Rect = new Rect(0, 0, width, height);
        if (!Root.IsLoaded || homeBrandBackdropFailed) return;
        try
        {
            homeBrandBackdrop ??= new BrandBackdrop(HomeBrandBackdropHost);
            homeBrandBackdrop.Resize(width, height);
            homeBrandBackdrop.SetVisible(Appearance.Pattern);
        }
        catch (Exception ex)
        {
            // Decorative graphics must not prevent the control center opening.
            homeBrandBackdropFailed = true;
            homeBrandBackdrop?.Dispose(); homeBrandBackdrop = null;
            AppLog.Error("BrandBackdrop", UiLanguage.Text("品牌底纹初始化失败，保留纯色背景。"), ex);
        }
    }

    private void UpdateHomeMotionVisibility()
    {
        if (!homeChromeReady || homeMotionShutdown) { StopHomeMotion(); return; }
        nint handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        bool animate = Appearance.Motion && Root.IsLoaded && IsWindowVisible(handle) && !IsIconic(handle) && homeUiSettings?.AnimationsEnabled != false;
        InteractionAccent.SetMotion(animate);
        LibraryList.SetMotionEnabled(animate && navigationPage == "library");
        EffectsAddButton.MotionEnabled = animate && navigationPage == "display";
        WidgetsAddButton.MotionEnabled = animate && navigationPage == "widgets";
        homeBrandBackdrop?.SetMotion(animate && homeChromeSelected && Appearance.Pattern);
    }
    private void HomeAnimations_Changed(UISettings sender, object args) => DispatcherQueue.TryEnqueue(UpdateHomeMotionVisibility);
    private void StopHomeMotion()
    {
        InteractionAccent.SetMotion(false);
        LibraryList.SetMotionEnabled(false);
        EffectsAddButton.MotionEnabled = WidgetsAddButton.MotionEnabled = false;
        homeBrandBackdrop?.SetMotion(false);
    }
}
