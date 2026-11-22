using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using LumaWall.Automation;
using LumaWall.Contracts;
using LumaWall.DesktopIntegration;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;

namespace LumaWall.ControlCenter;

public static class AutomationRegressionProgram
{
    [STAThread] public static void Main() {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(parameters=> {
            SynchronizationContext.SetSynchronizationContext(new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()));
            _=new App();
        });
    }
}
public partial class App:Application
{
    public static ChildProcessGroup ChildProcesses {get;}=new();
    internal static string Output=Path.GetFullPath(Environment.GetCommandLineArgs()[1]);
    public App() {InitializeComponent();UnhandledException+=(_,e)=> {File.WriteAllText(Path.Combine(Output,"unhandled.txt"),e.Exception.ToString());Environment.Exit(1);};}
    protected override void OnLaunched(LaunchActivatedEventArgs args) {
        Directory.CreateDirectory(Output);AppLog.Initialize("AutomationRegression",Output);
        var store=new LibraryStore(Path.Combine(Output,"data"));
        store.SetSetting("appearance.v1",new AppearanceOptions {Motion=false,Pattern=false}.ToJson());
        var window=new MainWindow(store);
        window.LoadedReady+=()=>window.CheckAutomationUi(Output);
        var area=System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
        window.AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(area.X+40,area.Y+40,Math.Min(1400,area.Width-80),Math.Min(1080,area.Height-80)));
        window.Activate();
    }
}
public sealed partial class MainWindow
{
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint handle,out uint process);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool AttachThreadInput(uint from,uint to,bool attach);
    private void RegressionForeground()
    {
        nint handle=WinRT.Interop.WindowNative.GetWindowHandle(this),foreground=AutomationForeground();
        uint current=GetCurrentThreadId(),other=GetWindowThreadProcessId(foreground,out _);
        bool attached=other!=0&&other!=current&&AttachThreadInput(current,other,true);
        try {ShowControlCenter();SetForegroundWindow(handle);}
        finally {if(attached)AttachThreadInput(current,other,false);}
        if(AutomationForeground()!=handle)throw new Exception("Test window could not obtain foreground");
    }
    internal async void CheckAutomationUi(string output)
    {
        var checks=new List<string>();
        void Check(bool condition,string message) {if(!condition)throw new Exception(message);checks.Add(message);File.AppendAllText(Path.Combine(output,"checks.txt"),message+"\n");}
        async Task Until(Func<bool> condition,string message) {for(int i=0;i<60&&!condition();i++)await Task.Delay(50);Check(condition(),message);}
        async Task<JsonElement> Call(string name,object args) {
            var json=ToolCatalog.Json(args);ToolCatalog.Validate(name,json);
            var reply=await AutomationUi(new(name,json),default);
            if(reply.IsError)throw new Exception(reply.Data.GetRawText());return reply.Data;
        }
        async Task<(string Revision,JsonElement Node)> Observe(FrameworkElement element) {
            var first=await Call("ui_get_tree",new {limit=200});string revision=first.GetProperty("treeRevision").GetString()!;
            string id=AutomationControlIdentity.Get(element);
            for(int offset=0;offset<first.GetProperty("total").GetInt32();offset+=200) {
                var page=offset==0?first:await Call("ui_get_tree",new {limit=200,offset,treeRevision=revision});
                foreach(var node in page.GetProperty("controls").EnumerateArray())if(node.GetProperty("ControlId").GetString()==id)return(revision,node.Clone());
            }
            throw new Exception("Target missing from paginated snapshot: "+element.Name);
        }
        async Task<JsonElement> Act(FrameworkElement element,string action,object? extra=null) {
            RegressionForeground();
            var observed=await Observe(element);var args=new Dictionary<string,object?> { ["controlId"]=observed.Node.GetProperty("ControlId").GetString(),["treeRevision"]=observed.Revision,["action"]=action };
            if(extra!=null)foreach(var p in ToolCatalog.Json(extra).EnumerateObject())args[p.Name]=p.Value.Clone();
            var result=await Call("ui_perform_action",args);
            Check(result.GetProperty("status").GetString()=="input_dispatched"&&result.GetProperty("eventCount").GetInt32()>0,"Dispatched "+action+" to "+element.GetType().Name);
            return result;
        }
        async Task Reject(Func<Task> action,string code,string message) {
            try {await action();}catch(OperationFault e) {Check(e.Code==code,message+" ("+e.Code+")");return;}
            throw new Exception("Expected refusal: "+message);
        }
        async Task Capture(string name) {
            var bitmap=new RenderTargetBitmap();await bitmap.RenderAsync(Root);using var stream=File.Create(Path.Combine(output,name+".png"));
            var encoder=await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId,stream.AsRandomAccessStream());
            encoder.SetPixelData(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Premultiplied,(uint)bitmap.PixelWidth,(uint)bitmap.PixelHeight,96,96,(await bitmap.GetPixelsAsync()).ToArray());await encoder.FlushAsync();
        }
        try {
            posterRepairCancellation.Cancel();wallpapers=new WallpaperController(store,action=>DispatcherQueue.TryEnqueue(()=>action()));
            widgets=new WidgetController(store,action=>DispatcherQueue.TryEnqueue(()=>action()));
            Navigate("display");AddEffect(EffectKind.Color);FlushEffectChanges();ShowControlCenter();await Task.Delay(400);
            var first=SnapshotAutomationUi();var ids=first.Nodes.Select(n=>n.ControlId).ToArray();
            automationControls.Clear();GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
            var second=SnapshotAutomationUi();Check(ids.SequenceEqual(second.Nodes.Select(n=>n.ControlId)),"All realized control IDs survive forced GC");
            var observed=await Observe(effectEnabled);
            FooterCpuText.Text="99.9%";await Task.Delay(100);
            bool before=effectEnabled.IsOn;
            RegressionForeground();
            await Call("ui_perform_action",new {treeRevision=observed.Revision,controlId=AutomationControlIdentity.Get(effectEnabled),action="click"});
            await Until(()=>effectEnabled.IsOn!=before,"Real toggle click changes effect state despite unrelated footer update");
            FlushEffectChanges();Check(store.EditingProfile(SelectedScreen!.DeviceName,null).Effects.GetCollection().ColorEnabled==effectEnabled.IsOn,"Toggle result persisted by production handlers");
            await Reject(()=>Call("ui_perform_action",new {treeRevision=observed.Revision,controlId=AutomationControlIdentity.Get(effectEnabled),action="click"}),"revision_conflict","Dispatched snapshot cannot be replayed");
            // Separate two clicks so Windows does not interpret them as one double-click gesture.
            await Task.Delay(System.Windows.Forms.SystemInformation.DoubleClickTime+60);
            await Act(effectEnabled,"click");await Until(()=>effectEnabled.IsOn==before,"Second real click restores effect");
            BrightnessSlider.StartBringIntoView();await Task.Delay(250);
            double brightness=BrightnessSlider.Value;
            await Act(BrightnessSlider,"drag",new {x=.45,y=.5,toX=.72,toY=.5});
            await Until(()=>BrightnessSlider.Value!=brightness,"Real drag changes brightness");
            await Act(BrightnessSlider,"click",new {x=.55,y=.5});await Task.Delay(200);
            brightness=BrightnessSlider.Value;
            await Act(BrightnessSlider,"key",new {key="Right"});
            await Until(()=>BrightnessSlider.Value>brightness,"Focused real Right key increases brightness");
            FlushEffectChanges();await Capture("effects-after-input");
            effectEnabled.Focus(FocusState.Programmatic);await Task.Delay(100);
            await Reject(()=>Act(BrightnessSlider,"key",new {key="Right"}),"invalid_state","Unfocused keyboard target rejected");
            // Native TextBox and ScrollViewer within the same allowlisted editor.
            // Fixture-only controls use the production input handler and WinUI events.
            var text=new TextBox {Name="InputRegressionText",Width=420,Height=40};
            var stack=new StackPanel {Spacing=12};stack.Children.Add(text);
            for(int i=0;i<30;i++)stack.Children.Add(new TextBlock {Text="Scroll fixture "+i,Height=40});
            var scroll=new ScrollViewer {Name="InputRegressionScroll",Content=stack,Width=500,Height=220,VerticalScrollBarVisibility=ScrollBarVisibility.Visible};
            EffectsInstanceControls.Content=scroll;EffectsParameterScroll.ChangeView(null,0,null,true);await Task.Delay(250);
            text.StartBringIntoView();await Task.Delay(150);await Act(text,"click");await Task.Delay(150);
            await Act(text,"text",new {text="测试A"});await Until(()=>text.Text=="测试A","Real Unicode text reaches TextBox");
            await Act(text,"key",new {key="Backspace"});await Until(()=>text.Text=="测试","Real Backspace edits TextBox");
            scroll.ChangeView(null,400,null,true);await Task.Delay(200);double offsetBefore=scroll.VerticalOffset;
            await Act(scroll,"scroll",new {delta=-120});await Until(()=>scroll.VerticalOffset>offsetBefore,"Real negative wheel scrolls downward");
            offsetBefore=scroll.VerticalOffset;await Act(scroll,"scroll",new {delta=120});await Until(()=>scroll.VerticalOffset<offsetBefore,"Real positive wheel scrolls upward");
            await Capture("text-and-scroll");
            var fixedPage=await Call("ui_get_tree",new {limit=200});string fixedRevision=fixedPage.GetProperty("treeRevision").GetString()!;
            text.Text="changed after observation";await Task.Delay(50);
            var repeated=await Call("ui_get_tree",new {limit=200,treeRevision=fixedRevision});
            Check(fixedPage.GetRawText()==repeated.GetRawText(),"Pinned snapshot page is immutable");
            var stale=await Observe(scroll);scroll.ChangeView(null,100,null,true);await Task.Delay(100);
            await Reject(()=>Call("ui_perform_action",new {treeRevision=stale.Revision,controlId=AutomationControlIdentity.Get(scroll),action="scroll",delta=-120}),"revision_conflict","Changed target state rejected");
            var popup=new Popup {XamlRoot=Root.XamlRoot,Child=new Border {Width=100,Height=100}};popup.IsOpen=true;await Task.Delay(100);
            await Reject(()=>Act(scroll,"scroll",new {delta=-120}),"invalid_state","Open popup prevents base-control input");popup.IsOpen=false;
            stale=await Observe(scroll);var position=AppWindow.Position;AppWindow.Move(new(position.X+12,position.Y));await Task.Delay(150);
            await Reject(()=>Call("ui_perform_action",new {treeRevision=stale.Revision,controlId=AutomationControlIdentity.Get(scroll),action="scroll",delta=-120}),"revision_conflict","Moved window invalidates target geometry");
            stale=await Observe(scroll);Navigate("library");await Task.Delay(100);
            await Reject(()=>Call("ui_perform_action",new {treeRevision=stale.Revision,controlId=AutomationControlIdentity.Get(scroll),action="scroll",delta=-120}),"revision_conflict","Changed page invalidates editing context");
            // Real desktop widget button: independent host, production controller,
            // isolated configuration, and telemetry supplied by that host.
            var timer=new WidgetInstance {Id="input-regression-timer",Kind=WidgetKind.FocusTimer,Screen=SelectedScreen!.DeviceName,X=.1,Y=.1,Enabled=true};
            widgets.Save(new WidgetConfiguration {Widgets=[timer]});AppWindow.Move(new(-20000,-20000));
            await Until(()=>widgets.AppliedRevision==widgets.ConfigurationRevision,"Widget host acknowledged isolated timer");
            var inspection=await widgets.Inspect(timer.Id,default);Check(inspection?.Telemetry!=null,"Timer telemetry available");
            var buttons=inspection!.Telemetry!.Value.GetProperty("buttons").EnumerateArray().ToArray();
            string buttonId=buttons.First(b=>b.GetProperty("visible").GetBoolean()&&b.GetProperty("enabled").GetBoolean()).GetProperty("controlId").GetString()!;
            var clicked=await Call("ui_perform_action",new {treeRevision=widgets.ConfigurationRevision,controlId=buttonId,action="click"});
            Check(clicked.GetProperty("eventCount").GetInt32()==3&&clicked.GetProperty("controlId").GetString()==buttonId,"Timer branch reports actual click sequence");
            await Until(()=>widgets.State.Widgets.Single().Focus.Running,"Real desktop timer button starts timer");
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new {passed=true,checks,dpi=Root.XamlRoot.RasterizationScale,inputSize=AutomationInput.Size},new JsonSerializerOptions {WriteIndented=true}));
        } catch(Exception ex) {File.WriteAllText(Path.Combine(output,"failure.txt"),ex.ToString());Environment.ExitCode=1;}
        finally {widgets?.Dispose();wallpapers?.Dispose();ShutdownImports();ShutdownPerformanceMonitor();ShutdownWindowPlacement();store.Dispose();Close();App.ChildProcesses.Dispose();Application.Current.Exit();}
    }
}
