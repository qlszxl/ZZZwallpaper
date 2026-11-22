using LumaWall.Contracts;
using LumaWall.DesktopIntegration;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;

namespace LumaWall.ControlCenter;

internal static class WidgetRefreshUiTest
{
    internal static readonly string Output = Path.Combine(Path.GetTempPath(), "ZZZ-widget-refresh-" + Guid.NewGuid());

    [STAThread]
    public static int Main()
    {
        Directory.CreateDirectory(Output);
        Console.WriteLine(Output);
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Microsoft.UI.Xaml.Application.Start(_ =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            new App();
        });
        return Environment.ExitCode;
    }
}

public partial class App : Microsoft.UI.Xaml.Application
{
    public static ChildProcessGroup ChildProcesses { get; } = new();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var output = WidgetRefreshUiTest.Output;
        AppContext.SetData("LumaWall.ExpansionPackRoot", Path.Combine(output, "packs"));
        Environment.SetEnvironmentVariable("ZZZWALLPAPER_FEATURE_PACK_ROOT", Path.Combine(output, "features"));
        AppLog.Initialize("WidgetRefreshTest", output);
        InitializeComponent();
        var store = new LibraryStore(Path.Combine(output, "data"));
        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
            store.SetSetting(RuntimeSettingsReader.WidgetsEnabledKey(screen.DeviceName), "false");
        var modules = new ControlCenterServices(store);
        var window = new MainWindow(store, modules);
        var controller = new WidgetController(store, action => window.DispatcherQueue.TryEnqueue(() => action()), modules);
        window.ConnectWidgets(controller);
        window.LoadedReady += async () =>
        {
            try
            {
                await window.CheckWidgetRefresh();
                File.WriteAllText(Path.Combine(output, "passed.txt"), "Performance drawer, refresh bursts, native menu, navigation and reopen passed.");
            }
            catch (Exception ex)
            {
                Environment.ExitCode = 1;
                File.WriteAllText(Path.Combine(output, "failure.txt"), ex.ToString());
            }
            finally
            {
                controller.Dispose();
                window.Close();
                store.Dispose();
                ChildProcesses.Dispose();
                Exit();
            }
        };
        window.AppWindow.Move(new Windows.Graphics.PointInt32(-20000, -20000));
        window.AppWindow.Show(false);
    }
}

public sealed partial class MainWindow
{
    internal async Task CheckWidgetRefresh()
    {
        static void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }
        async Task Turn()
        {
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => done.SetResult());
            await done.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        async Task Until(Func<bool> condition, string message)
        {
            var timeout = System.Diagnostics.Stopwatch.StartNew();
            while (!condition() && timeout.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(20);
            Check(condition(), message);
        }

        Navigate("widgets");
        await Turn();
        ShowWidgetAdd(WidgetsAddButton, null);
        var add = EffectsLogicalChildren(WidgetDrawerContent).OfType<Button>().Single(b =>
            b.Content is Grid row && row.Children.OfType<TextBlock>().Any(t => t.Text == WidgetTitle(WidgetKind.Performance)));
        int epoch = widgetEditorEpoch;
        RefreshWidgets();
        await Turn();
        Check(widgetRefreshPending && widgetEditorEpoch == epoch, "Open drawer must hold the rebuild");
        bool clickReturned = false;
        bool controlsIntact = false;
        add.Click += (_, _) =>
        {
            controlsIntact = WidgetDrawerContent.Children.Count != 0 && widgetEditorEpoch == epoch;
            for (int i = 0; i < 8; ++i) RefreshWidgets();
            clickReturned = true;
        };
        ((IInvokeProvider)new ButtonAutomationPeer(add).GetPattern(PatternInterface.Invoke)).Invoke();
        await Until(() => clickReturned && widgetDetailKey != null, "Performance editor did not open");
        Check(controlsIntact, "Click detached its own controls or rebuilt inline");
        Check(widgets!.State.Widgets.Single().Kind == WidgetKind.Performance && widgetEditorEpoch == epoch + 1 &&
            WidgetDrawerContent.Children.Count == 0, "Add should persist and build once");
        var id = selectedWidgetId!;
        WidgetBack();
        Check(WidgetEditorPanel.Children.Count != 0, "Back cleared the editor synchronously");
        await Until(() => WidgetEditorPanel.Children.Count == 0, "Overview did not replace editor");
        var card = EffectsLogicalChildren(WidgetListPanel).OfType<Button>().First(b => b.ContextFlyout is MenuFlyout);
        var menu = (MenuFlyout)card.ContextFlyout;
        menu.ShowAt(card);
        await Until(() => menu.IsOpen, "Context menu did not open");
        OpenWidget(id);
        await Turn();
        Check(widgetDetailKey == null && widgetEditorEpoch == epoch + 1, "Editor rebuilt under the menu");
        menu.Hide();
        await Until(() => widgetDetailKey == id, "Menu close lost the pending selection");
        ShowWidgetAdd(WidgetsAddButton, null);
        CloseWidgetDrawer(false);
        ShowWidgetAdd(WidgetsAddButton, null);
        await Turn();
        Check(WidgetDrawerContent.Children.Count != 0, "Previous close cleared a reopened drawer");
        CloseWidgetDrawer(false);
        await Until(() => WidgetDrawerContent.Children.Count == 0, "Final drawer was not cleaned up");
    }
}
