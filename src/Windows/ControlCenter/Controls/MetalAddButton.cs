using LumaWall.Contracts;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace LumaWall.ControlCenter;

// A command button, never a selectable item. Button supplies pointer capture,
// Space/Enter, UI Automation and Click; this class changes only presentation.
public sealed class MetalAddButton : Button
{
    public static readonly DependencyProperty BackdropTextProperty = DependencyProperty.Register(
        nameof(BackdropText), typeof(string), typeof(MetalAddButton), new PropertyMetadata("", (d, _) => ((MetalAddButton)d).ResizeWatermark()));
    public string BackdropText { get => (string)GetValue(BackdropTextProperty); set => SetValue(BackdropTextProperty, value); }
    private MetalMaterial? material;
    private FrameworkElement? watermarkRegion;
    private FrameworkElement? watermarkScale;
    private Border? focusRing, focusContrast, pressedShade;
    private ThemeSettings? themeSettings;
    private readonly UISettings uiSettings = new();
    private bool motionEnabled = true, windowActive = true, pointerOver, keyboardFocus, highContrast;
    private bool observingAccessibility;
    private double pointerX = .78, pointerY = .5;

    public bool MotionEnabled {
        get => motionEnabled;
        set { if (motionEnabled == value) return; motionEnabled = value; UpdatePresentation(); }
    }
    public bool WindowActive {
        get => windowActive;
        set {
            if (windowActive == value) return;
            windowActive = value;
            if (!value) pointerOver = false;
            RefreshAccessibility();
        }
    }

    public MetalAddButton()
    {
        InteractionFocus.SetSuppressFeedback(this, true);
        Loaded += (_, _) => {
            if (!observingAccessibility) {
                // AccessibilitySettings.HighContrastChanged requires a UWP
                // CoreWindow. WinUI desktop must subscribe through WindowId.
                try {
                    themeSettings = ThemeSettings.CreateForWindowId(XamlRoot.ContentIslandEnvironment.AppWindowId);
                    themeSettings.Changed += HighContrastChanged;
                } catch (System.Runtime.InteropServices.COMException error) {
                    themeSettings = null;
                    AppLog.Warning("MetalAddButton", UiLanguage.Text("窗口主题通知不可用，保留系统主题读取：") + error.Message);
                }
                uiSettings.TextScaleFactorChanged += TextScaleChanged;
                observingAccessibility = true;
            }
            RefreshAccessibility();
        };
        Unloaded += (_, _) => {
            if (observingAccessibility) {
                if (themeSettings != null) { themeSettings.Changed -= HighContrastChanged; themeSettings = null; }
                uiSettings.TextScaleFactorChanged -= TextScaleChanged;
                observingAccessibility = false;
            }
            pointerOver = false;
            material?.SetInteraction(false, false);
        };
        SizeChanged += (_, _) => ResizeWatermark();
        ActualThemeChanged += (_, _) => RefreshAccessibility();
        GotFocus += (_, _) => {
            // Remember keyboard origin while an existing add drawer owns focus.
            if (FocusState != FocusState.Programmatic) keyboardFocus = FocusState == FocusState.Keyboard;
            UpdatePresentation();
        };
        LostFocus += (_, _) => UpdatePresentation();
        PointerEntered += PointerPositionChanged;
        PointerMoved += PointerPositionChanged;
        PointerExited += (_, _) => { pointerOver = false; UpdatePresentation(); };
        PointerCanceled += (_, _) => { pointerOver = false; UpdatePresentation(); };
        // Do not mark these routed events handled: preserve native Button input.
        AddHandler(PointerPressedEvent, new PointerEventHandler((_, _) => { keyboardFocus = false; UpdatePresentation(); }), true);
        AddHandler(PointerCaptureLostEvent, new PointerEventHandler((_, e) => {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) { pointerOver = false; UpdatePresentation(); }
        }), true);
        RegisterPropertyChangedCallback(IsPressedProperty, (_, _) => UpdatePresentation());
        RegisterPropertyChangedCallback(IsEnabledProperty, (_, _) => {
            if (!IsEnabled) pointerOver = false;
            UpdatePresentation();
        });
        RegisterPropertyChangedCallback(VisibilityProperty, (_, _) => {
            pointerOver = false;
            UpdatePresentation();
        });
    }

    protected override void OnApplyTemplate()
    {
        material?.SetInteraction(false, false);
        base.OnApplyTemplate();
        material = GetTemplateChild("Material") as MetalMaterial;
        watermarkRegion = GetTemplateChild("WatermarkRegion") as FrameworkElement;
        watermarkScale = GetTemplateChild("WatermarkScale") as FrameworkElement;
        focusRing = GetTemplateChild("FocusRing") as Border;
        focusContrast = GetTemplateChild("FocusContrast") as Border;
        pressedShade = GetTemplateChild("PressedShade") as Border;
        RefreshAccessibility();
        ResizeWatermark();
    }

    private void PointerPositionChanged(object sender, PointerRoutedEventArgs e)
    {
        if (!IsEnabled || !windowActive || ActualWidth <= 0 || ActualHeight <= 0) return;
        // A touch tap gets the normal press feedback, not a persistent hover.
        if (e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Touch) return;
        var p = e.GetCurrentPoint(this).Position;
        pointerOver = p.X >= 0 && p.Y >= 0 && p.X <= ActualWidth && p.Y <= ActualHeight;
        pointerX = Math.Clamp(p.X / ActualWidth, 0, 1);
        pointerY = Math.Clamp(p.Y / ActualHeight, 0, 1);
        UpdatePresentation();
    }

    private void HighContrastChanged(ThemeSettings sender, object args) => DispatcherQueue.TryEnqueue(RefreshAccessibility);
    private void TextScaleChanged(UISettings sender, object args) => DispatcherQueue.TryEnqueue(ResizeWatermark);
    private void RefreshAccessibility()
    {
        highContrast = themeSettings?.HighContrast ?? System.Windows.Forms.SystemInformation.HighContrast;
        if (material != null) material.Visibility = highContrast ? Visibility.Collapsed : Visibility.Visible;
        ResizeWatermark();
        UpdatePresentation();
    }
    private void ResizeWatermark()
    {
        if (watermarkRegion == null || watermarkScale == null) return;
        // Protect the command label, including when Windows enlarges text.
        double textScale = uiSettings.TextScaleFactor;
        double protectedWidth = 88 + 100 * textScale;
        watermarkRegion.Width = Math.Max(0, ActualWidth - protectedWidth - 8);
        watermarkRegion.Visibility = highContrast || watermarkRegion.Width < 210 ? Visibility.Collapsed : Visibility.Visible;
        // Scale only the decorative lettering, never the command or button.
        // Both words span the middle/right at every width instead of leaving
        // an increasingly empty center on wider windows.
        double fillLeft = Math.Max(0, ActualWidth * .31 - protectedWidth);
        double letteringHeight = Math.Clamp(ActualHeight * 1.85, 120, 210);
        watermarkScale.Width = Math.Max(1, watermarkRegion.Width - fillLeft - 26);
        watermarkScale.Height = letteringHeight;
        Canvas.SetLeft(watermarkScale, fillLeft);
        Canvas.SetTop(watermarkScale, (ActualHeight - letteringHeight) / 2 - 5);
    }
    private void UpdatePresentation()
    {
        bool usable = IsLoaded && IsEnabled && windowActive;
        bool pressed = usable && IsPressed;
        bool focused = usable && keyboardFocus && FocusState != FocusState.Unfocused;
        if (focusRing != null) focusRing.Opacity = focused ? 1 : 0;
        if (focusContrast != null) focusContrast.Opacity = focused ? 1 : 0;
        if (pressedShade != null) pressedShade.Opacity = pressed ? (highContrast ? .2 : .11) : 0;
        if (material == null) return;
        material.SetInteraction(usable && motionEnabled && !highContrast, pressed);
        if (usable && motionEnabled && pointerOver && !highContrast) material.Follow(pointerX, pointerY);
        else material.Rest();
    }
}

// Decorative type must not contribute its desired size to the button's Auto
// height. Otherwise sizing the watermark from ActualHeight creates a feedback
// loop and grows a 76-DIP command into a 300-DIP card after layout passes.
public sealed class MetalDecorationLayer : Panel
{
    public MetalDecorationLayer() => SizeChanged += (_, e) =>
        Clip = new RectangleGeometry { Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height) };
    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children) child.Measure(availableSize);
        return new Size(0, 0);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var child in Children) child.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height));
        return finalSize;
    }
}

// Only the white highlight map moves. Dark folds, grain and halftone remain
// fixed in material space. Composition renders the layers; a timer eases toward
// pointer input or slowly sweeps the light while this button is visible/active.
public sealed class MetalMaterial : FrameworkElement
{
    private readonly List<CompositionObject> owned = new(), sized = new();
    private readonly List<LoadedImageSurface> images = new();
    private ContainerVisual? root, grainPlane;
    private ShapeVisual? dots;
    private SpriteVisual? reflection, shoulder, fold;
    private CompositionSurfaceBrush? grainBrush;
    private CompositionRoundedRectangleGeometry? clipGeometry;
    private Compositor? compositor;
    private DispatcherQueueTimer? timer;
    private bool animate, failed, pressed, following;
    private Vector2 size;
    private const double RestX = .57;
    private double currentX = RestX, currentY = .5, targetX = RestX, targetY = .5;
    private double idlePhase;
    private long lastTick;

    public MetalMaterial()
    {
        Loaded += (_, _) => Initialize();
        Unloaded += (_, _) => Release();
        SizeChanged += (_, _) => { if (root != null) { Resize(); Wake(); } };
        EffectiveViewportChanged += (_, _) => Wake();
    }
    private T Own<T>(T item) where T : CompositionObject { owned.Add(item); return item; }
    private T Sized<T>(T item) where T : CompositionObject { sized.Add(item); return item; }
    private CompositionSurfaceBrush Texture(string name)
    {
        var image = LoadedImageSurface.StartLoadFromUri(new Uri($"ms-appx:///Assets/MetalButton/{name}.png"));
        images.Add(image);
        var brush = Own(compositor!.CreateSurfaceBrush(image));
        brush.Stretch = CompositionStretch.Fill;
        brush.BitmapInterpolationMode = CompositionBitmapInterpolationMode.Linear;
        return brush;
    }
    private void Initialize()
    {
        if (root != null || failed) return;
        try {
            compositor = ElementCompositionPreview.GetElementVisual(this).Compositor;
            root = Own(compositor.CreateContainerVisual());
            clipGeometry = Own(compositor.CreateRoundedRectangleGeometry());
            clipGeometry.CornerRadius = new Vector2(9);
            root.Clip = Own(compositor.CreateGeometricClip(clipGeometry));
            var reflectionBrush = Texture("reflection");
            shoulder = Own(compositor.CreateSpriteVisual());
            shoulder.Brush = reflectionBrush; shoulder.Opacity = .55f;
            root.Children.InsertAtTop(shoulder);
            fold = Own(compositor.CreateSpriteVisual());
            fold.Brush = reflectionBrush; fold.Opacity = .38f;
            root.Children.InsertAtTop(fold);
            reflection = Own(compositor.CreateSpriteVisual());
            reflection.Brush = Texture("highlight");
            root.Children.InsertAtTop(reflection);
            grainBrush = Texture("grain");
            grainPlane = Own(compositor.CreateContainerVisual());
            grainPlane.Opacity = .32f;
            root.Children.InsertAtTop(grainPlane);
            dots = Own(compositor.CreateShapeVisual());
            root.Children.InsertAtTop(dots);
            timer = DispatcherQueue.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(16);
            timer.Tick += Settle;
            ElementCompositionPreview.SetElementChildVisual(this, root);
            Resize();
            Wake();
        } catch (Exception ex) {
            failed = true;
            Release();
            AppLog.Error("MetalAddButton", UiLanguage.Text("银色按钮材质初始化失败，保留可操作的静态按钮。"), ex);
        }
    }
    private void Resize()
    {
        if (root == null || compositor == null) return;
        size = new Vector2((float)Math.Max(0, ActualWidth), (float)Math.Max(0, ActualHeight));
        root.Size = size; clipGeometry!.Size = size;
        grainPlane!.Children.RemoveAll(); dots!.Shapes.Clear();
        for (int i = sized.Count - 1; i >= 0; i--) sized[i].Dispose();
        sized.Clear();
        // Constant 128-DIP tiles keep the grain fine at any window width/DPI.
        for (float y = 0; y < size.Y; y += 128) for (float x = 0; x < size.X; x += 128) {
            var tile = Sized(compositor.CreateSpriteVisual());
            tile.Brush = grainBrush; tile.Size = new Vector2(128); tile.Offset = new Vector3(x, y, 0);
            grainPlane.Children.InsertAtTop(tile);
        }
        dots.Size = size;
        float dotWidth = Math.Min(900, Math.Min(size.X * .68f, Math.Max(0, size.X - 240)));
        // A staggered, irregular density falloff avoids a uniform dot wallpaper.
        var dotBrush = Sized(compositor.CreateColorBrush(Color.FromArgb(140, 40, 43, 46)));
        for (int row = 0; row * 5.5f < size.Y; row++) for (int col = 0; col * 5.5f < dotWidth; col++) {
            float x = size.X - 4 - col * 5.5f - (row % 2) * 2.75f, y = 4 + row * 5.5f;
            float across = col * 5.5f / Math.Max(1, dotWidth);
            float edge = Math.Max(0, 1 - y / Math.Max(1, size.Y) - across * .4f + .1f * MathF.Sin(col * .12f));
            float fade = MathF.Pow(1 - across, 1.45f) * MathF.Pow(edge, 1.4f);
            float radius = fade * (1.75f + .18f * MathF.Sin(col * 1.7f + row * 2.1f));
            if (radius < .18f) continue;
            var ellipse = Sized(compositor.CreateEllipseGeometry()); ellipse.Radius = new Vector2(radius);
            var dot = Sized(compositor.CreateSpriteShape(ellipse)); dot.Offset = new Vector2(x, y); dot.FillBrush = dotBrush;
            dots.Shapes.Add(dot);
        }
        // Sparse tiny particles bridge the halftone falloff. No stain patches,
        // scratches or coarse splatter are used on the clean silver face.
        var sprayBrush = Sized(compositor.CreateColorBrush(Color.FromArgb(40, 63, 69, 74)));
        var random = new Random(11022);
        for (int i = 0; i < 180 && dotWidth > 0; i++) {
            float across = (float)random.NextDouble(), down = (float)random.NextDouble();
            float radius = (.18f + (float)random.NextDouble() * .42f) * (1 - across * .65f);
            var ellipse = Sized(compositor.CreateEllipseGeometry()); ellipse.Radius = new Vector2(radius);
            var dot = Sized(compositor.CreateSpriteShape(ellipse));
            dot.Offset = new Vector2(size.X - 4 - across * dotWidth, 3 + down * Math.Max(0, size.Y - 6));
            dot.FillBrush = sprayBrush; dots.Shapes.Add(dot);
        }
        float width = Math.Clamp(size.X * .48f, 280, 760);
        SizeReflection(reflection!, width, 12);
        SizeReflection(shoulder!, width * .9f, 11);
        shoulder!.Offset = new Vector3(size.X * .38f - shoulder.Size.X / 2, (size.Y - shoulder.Size.Y) / 2, 0);
        shoulder.RotationAngleInDegrees = -11;
        SizeReflection(fold!, width * .72f, 8);
        fold!.Offset = new Vector3(size.X * .73f - fold.Size.X / 2, (size.Y - fold.Size.Y) / 2, 0);
        fold.RotationAngleInDegrees = 8;
        Paint();
    }
    private void SizeReflection(SpriteVisual visual, float width, float maxTiltDegrees)
    {
        // Rotation lifts the edge of a wide sprite much farther than its
        // original 1.5x-height padding. Cover the full strip at maximum tilt;
        // only the button's rounded clip may cut the reflection, never the map.
        float height = MathF.Ceiling(size.Y + width * MathF.Sin(maxTiltDegrees * MathF.PI / 180) + 16);
        visual.Size = new Vector2(width, height);
        visual.CenterPoint = new Vector3(visual.Size / 2, 0);
    }
    public void SetInteraction(bool motion, bool isPressed)
    {
        pressed = isPressed;
        if (animate != motion) {
            animate = motion;
            following = false;
            if (!motion) { timer?.Stop(); currentX = targetX = RestX; currentY = targetY = .5; }
            else BeginIdle();
        }
        Paint();
        Wake();
    }
    public void Follow(double x, double y)
    {
        if (!animate || root == null) return;
        following = true;
        targetX = Math.Clamp(x, 0, 1); targetY = Math.Clamp(y, 0, 1);
        Wake();
    }
    public void Rest()
    {
        if (following) { following = false; BeginIdle(); }
        Wake();
    }
    private void BeginIdle()
    {
        // Resume the slow sweep from the displayed position instead of
        // snapping back to a fixed starting point when the pointer leaves.
        idlePhase = Math.Asin(Math.Clamp((currentX - .5) / .43, -1, 1));
        targetX = currentX; targetY = .5;
    }
    private void Wake()
    {
        if (timer == null) return;
        if (!animate || !VisibleInTree()) { timer.Stop(); return; }
        timer.Interval = TimeSpan.FromMilliseconds(following ? 16 : 33);
        if (!timer.IsRunning && (!following || Math.Abs(currentX - targetX) > .0005 || Math.Abs(currentY - targetY) > .0005)) {
            lastTick = System.Diagnostics.Stopwatch.GetTimestamp();
            timer.Start();
        }
    }
    private bool VisibleInTree()
    {
        for (DependencyObject? node = this; node != null; node = VisualTreeHelper.GetParent(node))
            if (node is UIElement element && element.Visibility != Visibility.Visible) return false;
        return IsLoaded;
    }
    private void Settle(DispatcherQueueTimer sender, object args)
    {
        if (!animate || !VisibleInTree()) {
            sender.Stop(); return;
        }
        double elapsed = Math.Min(.1, System.Diagnostics.Stopwatch.GetElapsedTime(lastTick).TotalSeconds);
        lastTick = System.Diagnostics.Stopwatch.GetTimestamp();
        if (!following) {
            idlePhase = (idlePhase + elapsed * Math.PI * 2 / 14) % (Math.PI * 2);
            targetX = .5 + .43 * Math.Sin(idlePhase);
            targetY = .5 + .12 * Math.Sin(idlePhase * 2);
        }
        double blend = 1 - Math.Exp(-elapsed * (following ? 24 : 8));
        currentX += (targetX - currentX) * blend;
        currentY += (targetY - currentY) * blend;
        if (following && Math.Abs(currentX - targetX) < .0005 && Math.Abs(currentY - targetY) < .0005) {
            currentX = targetX; currentY = targetY; sender.Stop();
        }
        Paint();
    }
    private void Paint()
    {
        if (reflection == null) return;
        reflection.Offset = new Vector3((float)currentX * size.X - reflection.Size.X / 2, (size.Y - reflection.Size.Y) / 2, 0);
        reflection.RotationAngleInDegrees = (float)(-3 + (currentY - .5) * 12);
        reflection.Opacity = pressed ? .5f : .92f;
    }
    private void Release()
    {
        if (timer != null) { timer.Stop(); timer.Tick -= Settle; timer = null; }
        if (root != null) ElementCompositionPreview.SetElementChildVisual(this, null);
        for (int i = sized.Count - 1; i >= 0; i--) sized[i].Dispose();
        for (int i = owned.Count - 1; i >= 0; i--) owned[i].Dispose();
        foreach (var image in images) image.Dispose();
        sized.Clear(); owned.Clear(); images.Clear();
        root = grainPlane = null; dots = null; reflection = shoulder = fold = null;
        grainBrush = null; clipGeometry = null; compositor = null;
        following = false;
        currentX = targetX = RestX; currentY = targetY = .5;
    }
}
