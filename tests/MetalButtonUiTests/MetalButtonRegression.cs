using System.Text.Json;
using System.Runtime.InteropServices.WindowsRuntime;
using LumaWall.Contracts;
using LumaWall.DesktopIntegration;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;

namespace LumaWall.ControlCenter;

public static class MetalButtonRegressionProgram
{
    internal static string Output = "";
    [STAThread] public static void Main(string[] args)
    {
        Output = Path.GetFullPath(args[0]); Directory.CreateDirectory(Output);
        _ = Task.Run(async () => { await Task.Delay(60000); Fail(new TimeoutException("UI resource regression timed out.")); });
        try {
            WinRT.ComWrappersSupport.InitializeComWrappers();
            Application.Start(parameters => {
                SynchronizationContext.SetSynchronizationContext(new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()));
                try { _ = new App(); } catch (Exception error) { Fail(error); }
            });
        } catch (Exception error) { Fail(error); }
    }
    internal static void Fail(Exception error)
    {
        File.WriteAllText(Path.Combine(Output, "failure.txt"), error.ToString());
        Environment.Exit(1);
    }
}

public partial class App : Application
{
    public static ChildProcessGroup ChildProcesses { get; } = new();
    public App()
    {
        UnhandledException += (_, e) => MetalButtonRegressionProgram.Fail(e.Exception);
        DebugSettings.IsXamlResourceReferenceTracingEnabled = true;
        DebugSettings.XamlResourceReferenceFailed += (_, e) => File.AppendAllText(Path.Combine(MetalButtonRegressionProgram.Output, "resources.txt"), e.Message + "\n");
        InitializeComponent();
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try {
            string output = MetalButtonRegressionProgram.Output;
            AppLog.Initialize("MetalButtonRegression", output);
            var store = new LibraryStore(Path.Combine(output, "data"));
            store.SetSetting("appearance.v1", new AppearanceOptions { Motion = false, Pattern = false }.ToJson());
            // Only a synthetic window: no App startup controllers, default user
            // database, desktop integration, wallpaper host or MCP listener.
            var window = new MainWindow(store);
            window.LoadedReady += () => window.CheckMetalButtons(output);
            window.AppWindow.Move(new Windows.Graphics.PointInt32(-20000, -20000));
            window.Activate();
        } catch (Exception error) { MetalButtonRegressionProgram.Fail(error); }
    }
}

public sealed partial class MainWindow
{
    private async Task MetalLayoutCapture(UIElement element, string path)
    {
        var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(element);
        var pixels = (await bitmap.GetPixelsAsync()).ToArray();
        using var stream = File.Create(path);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream.AsRandomAccessStream());
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels);
        await encoder.FlushAsync();
    }
    private static IEnumerable<FrameworkElement> MetalTestElements(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement element) yield return element;
            foreach (var descendant in MetalTestElements(child)) yield return descendant;
        }
    }
    private static bool MetalReflectionEdgesOutside(MetalMaterial material)
    {
        var root = (ContainerVisual)ElementCompositionPreview.GetElementChildVisual(material);
        foreach (var layer in root.Children.OfType<SpriteVisual>()) {
            double angle = layer.RotationAngleInDegrees * Math.PI / 180;
            double verticalHalf = layer.Size.Y * Math.Cos(angle) / 2;
            double cornerLift = layer.Size.X * Math.Abs(Math.Sin(angle)) / 2;
            double centerY = layer.Offset.Y + layer.CenterPoint.Y;
            if (centerY - verticalHalf + cornerLift >= 0 || centerY + verticalHalf - cornerLift <= material.ActualHeight) return false;
        }
        return true;
    }
    internal async void CheckMetalButtons(string output)
    {
        var checks = new List<string>();
        var layouts = new List<object>();
        void Check(bool passed, string message) { if (!passed) throw new Exception(message); checks.Add(message); }
        try {
            posterRepairCancellation.Cancel();
            Check(true, "Real App.xaml and MainWindow initialized without XAML parse errors");
            using (var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Assets", "MetalButton", "highlight.png"))) {
                var decoder = await BitmapDecoder.CreateAsync(stream.AsRandomAccessStream());
                var pixels = (await decoder.GetPixelDataAsync(BitmapPixelFormat.Rgba8, BitmapAlphaMode.Straight,
                    new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage)).DetachPixelData();
                bool onlyLight = true, visible = false;
                for (int i = 0; i < pixels.Length; i += 4) {
                    if (pixels[i + 3] == 0) continue;
                    visible = true;
                    onlyLight &= pixels[i] == 255 && pixels[i + 1] == 255 && pixels[i + 2] == 255;
                }
                Check(visible && onlyLight, "Moving texture contains visible white light and no dark pixels");
            }
            foreach (var pair in new[] { ("display", EffectsAddButton, "添加效果"), ("widgets", WidgetsAddButton, "添加组件") }) {
                Navigate(pair.Item1); await Task.Delay(150);
                var button = pair.Item2;
                foreach (int width in new[] { 1000, 1200, 1440, 2560, 1200, 1000 }) {
                    AppWindow.Resize(new Windows.Graphics.SizeInt32(width, 940)); await Task.Delay(100);
                    button.ApplyTemplate(); Root.UpdateLayout();
                    Check(button.ActualWidth > 200 && button.ActualHeight >= 76, pair.Item1 + " button layout at " + width);
                    double firstHeight = button.ActualHeight;
                    await Task.Delay(100); Root.UpdateLayout();
                    Check(button.ActualHeight <= 84 && Math.Abs(button.ActualHeight - firstHeight) < .5, pair.Item1 + " slim height stays stable after repeated layout at " + width);
                    layouts.Add(new { page=pair.Item1, windowWidth=width, width=button.ActualWidth, height=button.ActualHeight, sidebar=EffectsNavigationColumn.Width.Value });
                    Check(EffectsNavigationColumn.Width.Value == (Root.ActualWidth < 1060 ? 80 : Root.ActualWidth >= 1280 ? 224 : 200), "Navigation preserves original width at " + width);
                    foreach (var nav in EffectsGlobalNavigation.Children.OfType<CassetteNavButton>()) {
                        var row = (StackPanel)nav.Content; var label = (TextBlock)row.Children[1];
                        double iconSize = row.Children[0] is FontIcon glyph ? glyph.FontSize : ((FrameworkElement)row.Children[0]).Width;
                        Check(Math.Abs(iconSize - 28.8) < .01 && Math.Abs(label.FontSize - (Root.ActualWidth >= 1280 ? 24 : 21.6)) < .01, "Navigation icon/text enlarged by 20%: " + nav.Tag + " / " + width);
                        if (label.Visibility == Visibility.Visible) {
                            Check(row.DesiredSize.Width + nav.Padding.Left + nav.Padding.Right + 6 <= nav.ActualWidth + 1, "Navigation content fits enlarged button: " + nav.Tag + " / " + width);
                            Check(nav.ActualHeight >= 64 && row.DesiredSize.Height + 6 <= nav.ActualHeight, "Navigation grows only in height without clipping: " + nav.Tag + " / " + width);
                        }
                    }
                    var material = MetalTestElements(button).OfType<MetalMaterial>().Single();
                    Check(ElementCompositionPreview.GetElementChildVisual(material) != null, pair.Item1 + " native material initialized at " + width);
                    Check(MetalReflectionEdgesOutside(material), pair.Item1 + " tilted reflection edges stay outside button at " + width);
                    var lettering = MetalTestElements(button).Single(x => x.Name == "WatermarkScale");
                    var text = (TextBlock)MetalTestElements(button).Single(x => x.Name == "Watermark");
                    double letteringLeft = lettering.TransformToVisual(button).TransformPoint(new Windows.Foundation.Point()).X;
                    Check(letteringLeft > 188 && letteringLeft < button.ActualWidth * .42 && lettering.ActualWidth > button.ActualWidth * .5, pair.Item1 + " lettering fills center while protecting command at " + width);
                    string word = pair.Item1 == "display" ? "effects" : "widgets";
                    Check(text.Text == word.ToUpperInvariant() && !MetalTestElements(button).Any(x => x.Name == "WatermarkOutline"), pair.Item1 + " clean filled word without outline at " + width);
                    var font = MetalTestElements(button).OfType<ContentPresenter>().First(x => Equals(x.Content, pair.Item3)).FontFamily.Source;
                    Check(font.Contains("SourceHanSansSC-Heavy.otf"), pair.Item1 + " bundled heavy font resolved");
                    if (width == 1440) {
                        // This off-screen window may receive its initial OS
                        // deactivation late. Keep that real state stable while
                        // directly exercising normalized material targets.
                        button.WindowActive = false;
                        await MetalLayoutCapture(button, Path.Combine(output, pair.Item1 + "-button-layout.png"));
                        if (pair.Item1 == "display") await MetalLayoutCapture(CassetteNavigationHost, Path.Combine(output, "navigation-layout.png"));
                        var lightLayers = ((ContainerVisual)ElementCompositionPreview.GetElementChildVisual(material)).Children.OfType<SpriteVisual>().ToArray();
                        material.SetInteraction(true, false); material.Follow(.18, 0); await Task.Delay(500);
                        var leftPositions = lightLayers.Select(x => x.Offset.X).ToArray();
                        var leftStates = lightLayers.Select(x => (x.Offset, x.RotationAngleInDegrees, x.Opacity)).ToArray();
                        Check(MetalReflectionEdgesOutside(material), pair.Item1 + " no reflection cut at top pointer edge");
                        await MetalLayoutCapture(button, Path.Combine(output, pair.Item1 + "-light-top.png"));
                        material.Follow(.82, 1); await Task.Delay(500);
                        double distance = lightLayers.Select((layer, i) => layer.Offset.X - leftPositions[i]).Max();
                        Check(distance > material.ActualWidth * .5, pair.Item1 + " light follows horizontal movement: " + distance + " / width " + material.ActualWidth);
                        var moved = lightLayers.Where((layer, i) => Math.Abs(layer.Offset.X - leftPositions[i]) > 1).ToArray();
                        Check(moved.Length == 1, pair.Item1 + " exactly one highlight layer follows the pointer");
                        var highlight = moved.Single();
                        bool FixedFolds() => lightLayers.Select((layer, i) => ReferenceEquals(layer, highlight) ||
                            (layer.Offset, layer.RotationAngleInDegrees, layer.Opacity) == leftStates[i]).All(x => x);
                        Check(FixedFolds(), pair.Item1 + " shadow positions, angles and opacity remain unchanged while following");
                        Check(MetalReflectionEdgesOutside(material), pair.Item1 + " no reflection cut at bottom pointer edge");
                        await MetalLayoutCapture(button, Path.Combine(output, pair.Item1 + "-light-bottom.png"));

                        var beforeLeave = highlight.Offset;
                        material.Rest();
                        Check(highlight.Offset == beforeLeave, pair.Item1 + " pointer exit does not snap the highlight");
                        await Task.Delay(1500);
                        Check(Math.Abs(highlight.Offset.X - beforeLeave.X) > material.ActualWidth * .025, pair.Item1 + " highlight keeps moving without pointer input");
                        Check(FixedFolds() && MetalReflectionEdgesOutside(material), pair.Item1 + " idle light keeps shadows fixed and has no edge cut");
                        await MetalLayoutCapture(button, Path.Combine(output, pair.Item1 + "-light-idle.png"));
                        var beforeEnter = highlight.Offset;
                        material.Follow(.18, 0);
                        Check(highlight.Offset == beforeEnter, pair.Item1 + " pointer reentry hands over without jumping");
                        await Task.Delay(500);
                        Check(Math.Abs(highlight.Offset.X - beforeEnter.X) > material.ActualWidth * .5, pair.Item1 + " pointer takes control after idle sweep");
                        Check(FixedFolds(), pair.Item1 + " shadow stays fixed through idle and pointer handover");

                        // Exercise the real button state wiring, not just the
                        // material's directly supplied pointer targets.
                        button.MotionEnabled = true; button.WindowActive = true;
                        button.IsEnabled = false; await Task.Delay(80);
                        var disabled = highlight.Offset; await Task.Delay(120);
                        Check(highlight.Offset == disabled, pair.Item1 + " disabled command pauses idle motion");
                        button.IsEnabled = true; await Task.Delay(500);
                        Check(highlight.Offset != disabled, pair.Item1 + " enabled command resumes idle motion without mouse input");
                        button.Visibility = Visibility.Collapsed; await Task.Delay(100);
                        var hidden = highlight.Offset; await Task.Delay(150);
                        Check(highlight.Offset == hidden, pair.Item1 + " hidden button pauses motion");
                        button.Visibility = Visibility.Visible; await Task.Delay(600);
                        Check(highlight.Offset != hidden, pair.Item1 + " visible button resumes idle motion");
                        button.WindowActive = false; await Task.Delay(80);
                        var inactive = highlight.Offset; await Task.Delay(150);
                        Check(highlight.Offset == inactive, pair.Item1 + " inactive window pauses motion");
                        button.WindowActive = true; await Task.Delay(500);
                        Check(highlight.Offset != inactive, pair.Item1 + " reactivated window resumes idle motion");
                        button.MotionEnabled = false;
                        material.SetInteraction(false, false);
                        var restingPositions = lightLayers.Select(x => x.Offset).ToArray();
                        material.Follow(.1, 0); await Task.Delay(100);
                        Check(lightLayers.Select((layer, i) => layer.Offset == restingPositions[i]).All(x => x), pair.Item1 + " disabled motion leaves static metal");
                    }
                }
                button.FontSize = 36; await Task.Delay(100); Root.UpdateLayout();
                Check(button.ActualHeight > 84 && button.ActualHeight < 120, pair.Item1 + " larger command text may grow height without decorative feedback");
                button.FontSize = 20; await Task.Delay(100); Root.UpdateLayout();
                Check(button.ActualHeight <= 84, pair.Item1 + " restoring text size restores slim height");
                var peer = FrameworkElementAutomationPeer.CreatePeerForElement(button)!;
                Check(peer.GetName() == pair.Item3 && peer.GetAutomationControlType() == AutomationControlType.Button, pair.Item1 + " native accessible command button");
                Check(InteractionFocus.GetSuppressFeedback(button), pair.Item1 + " excludes global focus decoration");
                button.WindowActive = true;
                Check(button.Focus(FocusState.Keyboard), pair.Item1 + " accepts keyboard focus");
                await Task.Delay(50);
                var ring = MetalTestElements(button).OfType<Border>().Single(x => x.Name == "FocusRing");
                Check(ring.Opacity == 1, pair.Item1 + " independent keyboard focus ring");
                var invoke = (IInvokeProvider)peer.GetPattern(PatternInterface.Invoke);
                invoke.Invoke(); await Task.Delay(100);
                Check(!EffectsBodyHost.IsEnabled, pair.Item1 + " native Invoke opens modal and disables underlying page");
                if (pair.Item1 == "display") {
                    Check(EffectsAddOverlay.Visibility == Visibility.Visible, "Actual effect picker opens");
                    CloseEffectsAdd(true);
                } else {
                    Check(WidgetDrawerOverlay.Visibility == Visibility.Visible, "Actual widget picker opens");
                    CloseWidgetDrawer(true);
                }
                await Task.Delay(50);
                Check(EffectsBodyHost.IsEnabled && ReferenceEquals(FocusManager.GetFocusedElement(Root.XamlRoot), button), pair.Item1 + " modal closes and restores focus");
            }
            File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { passed = true, checks, layouts, actualInstallationRun = false, userDataAccessed = false, dpiScale = Root.XamlRoot.RasterizationScale }, new JsonSerializerOptions { WriteIndented = true }));
            Close(); store.Dispose(); App.ChildProcesses.Dispose(); Environment.Exit(0);
        } catch (Exception error) { MetalButtonRegressionProgram.Fail(error); }
    }
}
