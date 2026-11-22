using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using LumaWall.Automation;
using LumaWall.Contracts;
using LumaWall.DesktopIntegration;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;

namespace LumaWall.ControlCenter;

public static class ReportRegressionProgram
{
    [STAThread] public static void Main() {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(parameters=> {
            SynchronizationContext.SetSynchronizationContext(new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()));
            _=new App();
        });
    }
}
public partial class App : Application
{
    public static ChildProcessGroup ChildProcesses {get;}=new();
    internal static string Output=Path.GetFullPath(Environment.GetCommandLineArgs()[1]);
    public App() {InitializeComponent();UnhandledException+=(_,e)=> {File.WriteAllText(Path.Combine(Output,"unhandled.txt"),e.Exception.ToString()+"\n"+string.Join('\n',e.Exception.Data.Keys.Cast<object>().Select(k=>$"{k}: {e.Exception.Data[k]}")));Environment.Exit(1);};}
    protected override void OnLaunched(LaunchActivatedEventArgs args) {
        Directory.CreateDirectory(Output);AppLog.Initialize("ReportRegression",Output);
        DebugSettings.IsXamlResourceReferenceTracingEnabled=true;
        DebugSettings.XamlResourceReferenceFailed+=(_,e)=>File.AppendAllText(Path.Combine(Output,"resources.txt"),e.Message+"\n");
        var store=new LibraryStore(Path.Combine(Output,"data"));
        store.SetSetting("appearance.v1",new AppearanceOptions {Motion=false,Pattern=false}.ToJson());
        // Synthetic library only; no wallpaper/controller startup or user database.
        var window=new MainWindow(store);
        window.LoadedReady+=()=>window.CheckReports(Output);
        window.AppWindow.Move(new Windows.Graphics.PointInt32(-20000,-20000));window.Activate();
    }
}
public sealed partial class MainWindow
{
    private static IEnumerable<FrameworkElement> ReportElements(DependencyObject root) {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++) {
            var child=VisualTreeHelper.GetChild(root,i);if(child is FrameworkElement e)yield return e;
            foreach(var descendant in ReportElements(child))yield return descendant;
        }
    }
    private async Task ReportCapture(string path) {
        var bitmap=new RenderTargetBitmap();await bitmap.RenderAsync(Root);
        var pixels=(await bitmap.GetPixelsAsync()).ToArray();using var stream=File.Create(path);
        var encoder=await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId,stream.AsRandomAccessStream());
        encoder.SetPixelData(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Premultiplied,(uint)bitmap.PixelWidth,(uint)bitmap.PixelHeight,96,96,pixels);await encoder.FlushAsync();
    }
    internal async void CheckReports(string output) {
        var checks=new List<string>();var layouts=new List<object>();
        void Check(bool value,string message) {if(!value)throw new Exception(message);checks.Add(message);}
        try {
            posterRepairCancellation.Cancel();
            for(int i=0;i<17;i++)store.Upsert(new MediaAsset("report-"+i,Path.Combine(output,"sample-"+i+".png"),"测试图片 "+i,6412,0,128,128,"mjpeg","SDR","Software"));
            // Use real images so library and poster selection follow normal paths.
            foreach(var asset in store.Assets()) {File.Copy(Path.Combine(AppContext.BaseDirectory,"Assets","Branding","wordmark.png"),asset.Path);File.Copy(asset.Path,store.Poster(asset.Id),true);}
            RefreshLibrary();Navigate("library");
            foreach(int width in new[]{2560,1440,1000}) {
                AppWindow.Resize(new Windows.Graphics.SizeInt32(width,940));await Task.Delay(250);
                foreach(int count in new[]{0,1,2,0}) {
                    ClearLibrarySelection();for(int i=0;i<count;i++)LibraryList.SelectedItems.Add(LibraryList.Items[i]);
                    await Task.Delay(160);LibraryWorkspace.UpdateLayout();ResizeLibraryCards();LibraryList.UpdateLayout();
                    var panel=(ItemsWrapGrid)LibraryList.ItemsPanelRoot;
                    double available=LibraryList.CardViewportWidth;
                    double cardWidth=panel.ItemWidth-LibraryPresentation.CardSpacing;
                    double slack=available-(panel.ItemWidth*panel.MaximumRowsOrColumns-LibraryPresentation.CardSpacing);
                    Check(slack>=-1,$"Adaptive columns fit the measured viewport at {width}px with {count} selected");
                    Check(cardWidth<=360&&(available<240||cardWidth>=240),$"Cards respect the adaptive width limits at {width}px with {count} selected");
                    var cards=ReportElements(LibraryList).OfType<GridViewItem>().Where(x=>x.ActualWidth>0).ToArray();
                    Check(cards.Length>0&&cards.All(c=>c.ActualWidth<=panel.ItemWidth+1),$"Realized cards fit cells at {width}px / {count}");
                    layouts.Add(new {width,count,gridWidth=LibraryList.ActualWidth,columns=panel.MaximumRowsOrColumns,itemWidth=panel.ItemWidth,slack});
                    if(count==1) {Check(LibraryInfoMetadata.Text.Contains("6.3 KiB"),"Small image shows 6.3 KiB at "+width);await ReportCapture(Path.Combine(output,$"library-{width}.png"));}
                }
                LibraryList.SelectedItem=LibraryList.Items[LibraryList.Items.Count-1];await Task.Delay(200);
                var selectedContainer=(FrameworkElement)LibraryList.ContainerFromItem(LibraryList.SelectedItem);
                var position=selectedContainer.TransformToVisual(LibraryList).TransformPoint(new Windows.Foundation.Point());
                Check(position.Y<LibraryList.ActualHeight&&position.Y+selectedContainer.ActualHeight>0,"Selection remains visible after reflow at "+width);
            }
            foreach(var pair in new[]{(0L,"0 B"),(1L,"1 B"),(1023L,"1,023 B"),(1024L,"1.0 KiB"),(6412L,"6.3 KiB"),(1048576L,"1.0 MiB"),(1073741824L,"1.0 GiB")})
                Check(LibraryPresentation.FileSize(pair.Item1)==pair.Item2,"File size boundary "+pair.Item1);
            widgets=new WidgetController(store,action=>DispatcherQueue.TryEnqueue(()=>action()));
            string screen=SelectedScreen!.DeviceName;
            var text=new WidgetInstance {Id="text",Kind=WidgetKind.Text,Screen=screen,Enabled=false};
            var clock=new WidgetInstance {Id="clock",Kind=WidgetKind.Clock,Screen=screen,Enabled=false};
            widgets.Save(new WidgetConfiguration {Widgets=[text,clock]});
            async Task<JsonElement> Group(object arguments) {
                var reply=await AutomationWidgetResources(new("widget_edit_group",ToolCatalog.Json(arguments)),default);
                return JsonSerializer.SerializeToElement(reply.Data);
            }
            var created=await Group(new {action="create",screenId=screen,widgetIds=new[]{"text","clock"},flow="Vertical"});
            string id=created.GetProperty("groupId").GetString()!;
            var reordered=await Group(new {action="members",groupId=id,widgetIds=new[]{"clock","text"}});
            Check(reordered.GetProperty("members").EnumerateArray().Select(x=>x.GetString()).SequenceEqual(new[]{"clock","text"}),"Production group response returns reordered members");
            var saved=WidgetConfiguration.FromJson(store.GetSetting("widgets.v1"));
            Check(WidgetStacking.GroupMembers(saved,id).Select(x=>x.Id).SequenceEqual(new[]{"clock","text"}),"Persisted group order matches response");
            var layout=WidgetGroupLayout.Arrange(saved.LayoutGroups.Single(),WidgetStacking.GroupMembers(saved,id).Select(w=>new WidgetGroupLayout.Item(w.Id,100,100)).ToArray(),1920,1080,20);
            Check(layout.Slots[0].Id=="clock"&&layout.Slots[0].Y<layout.Slots[1].Y,"Layout places clock before text");
            var removed=await Group(new {action="members",groupId=id,widgetIds=new[]{"text"}});
            Check(removed.GetProperty("members").GetArrayLength()==1&&widgets.State.Widgets.Single(w=>w.Id=="clock").LayoutGroupId==null,"Membership removal releases previous member");
            var ungrouped=await Group(new {action="ungroup",groupId=id});
            Check(ungrouped.GetProperty("members").GetArrayLength()==0&&ungrouped.GetProperty("group").ValueKind==JsonValueKind.Null,"Ungroup response has no group or members");
            Navigate("sharing");RefreshSharingPage();await Task.Delay(180);
            Check(shareAsset.PlaceholderText.Contains("素材")&&!shareTargetStatus.Text.Contains("视频"),"Image sharing uses material terminology");
            await ReportCapture(Path.Combine(output,"sharing.png"));
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new {passed=true,checks,layouts},new JsonSerializerOptions {WriteIndented=true}));
        } catch(Exception ex) {File.WriteAllText(Path.Combine(output,"failure.txt"),ex.ToString());Environment.ExitCode=1;}
        finally {widgets?.Dispose();ShutdownImports();ShutdownPerformanceMonitor();ShutdownWindowPlacement();store.Dispose();Close();Application.Current.Exit();}
    }
}
