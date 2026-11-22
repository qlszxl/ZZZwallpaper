using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using LumaWall.Contracts;

namespace LumaWall.ControlCenter;
public sealed partial class MainWindow
{
    private async Task CheckSavedWarehouseCards(string zip)
    {
        using var controller=new WidgetController(store,action=>DispatcherQueue.TryEnqueue(()=>action()),modules);
        var clock=new WidgetInstance {Id="clock",Kind=WidgetKind.Clock,Enabled=false};
        var warehouse=new WidgetInstance {Id="warehouse",Kind=WidgetKind.Warehouse,Enabled=false,X=.24,Y=.76};
        var layout=new WidgetConfiguration {Widgets=[clock,warehouse,warehouse with {Id="grouped",LayoutGroupId="group",LayoutOrder=4}],
            LayoutGroups=[new(){Id="group"}]};
        controller.Save(layout);ConnectWidgets(controller);Navigate("widgets");await ReorderSettle();
        bool WarehouseCard()=>EffectsLogicalChildren(WidgetListPanel).OfType<TextBlock>().Any(t=>t.Text=="搜索物资");
        bool Count(int count)=>WidgetCountText.Text==$"我的组件 · {count} 个组件 · 1 个组";
        ReorderCheck(!WarehouseCard()&&Count(1),"Absent resource pack hides saved warehouse cards from the overview and total");
        OpenWidgetGroup("group");await ReorderSettle();
        ReorderCheck(EffectsLogicalChildren(WidgetEditorPanel).OfType<TextBlock>().Any(t=>t.Text=="组内组件 · 0"),"Grouped warehouse is hidden");
        WidgetBack();await ImportExpansion(zip);await ReorderSettle();
        ReorderCheck(!WarehouseCard()&&Count(1),"Import starts without instances");
        var added=warehouse with {Kind=WidgetKind.Extension,PackageWidgetId=WidgetConfiguration.LegacyMaterialsWidget,GridOptions=new(4,700)};
        controller.Save(layout with {Widgets=[clock,added]});await ReorderSettle();
        ReorderCheck(WarehouseCard()&&Count(2),"Fresh instance appears in the overview");
        OpenWidget("warehouse");await UpdateExpansion(ExpansionCatalog.Packages.Single().Remove);await ReorderSettle();
        ReorderCheck(selectedWidgetId==null&&!WarehouseCard()&&Count(1),"Removing a pack closes its open editor and hides its card");
        ReorderCheck(controller.State.ToJson()==(layout with {Widgets=[clock]}).ToJson(),"Removal clears the instance and retains the unrelated clock and group");
    }

    internal async void CheckDecodeDialog(string output)
    {
        try {
            await ReorderSettle();ReorderCheckIsolation();
            if(Environment.GetEnvironmentVariable("ZZZWALLPAPER_SAVED_WIDGET_FIXTURE") is {Length:>0} zip){
                await CheckSavedWarehouseCards(zip);
                File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new {passed=true,checks=reorderChecks}));
                App.ReorderIsolation?.WriteReport();Environment.Exit(0);return;
            }
            var body=new StackPanel {Spacing=12,Width=500};
            body.Children.Add(UtilityText("Decode fixture","UtilityHeading"));
            var status=UtilityText("Testing");
            var row=new StackPanel {Orientation=Orientation.Horizontal,Spacing=10};
            bool deferred=output.Contains("deferred"),without=output.Contains("without");
            var ring=new ProgressRing {Width=20,Height=20,IsActive=!deferred,Foreground=HomeBrush("InteractionAccentBrush")};
            if(!without)row.Children.Add(ring);row.Children.Add(status);body.Children.Add(row);body.Children.Add(new StackPanel());
            var dialog=new ContentDialog {XamlRoot=Root.XamlRoot,Title="解码性能测试",Content=new ScrollViewer {Style=HomeStyle("HomeScrollViewer"),Content=body,MaxHeight=600},PrimaryButtonText="采用建议方式",CloseButtonText="取消测试",Style=HomeStyle("LibraryDialog"),IsPrimaryButtonEnabled=false};
            File.WriteAllText(Path.Combine(output,"stage.txt"),"Opening real dialog with active ProgressRing; no media process");
            var shown=dialog.ShowAsync();await ReorderSettle(400);
            if(deferred){File.AppendAllText(Path.Combine(output,"stage.txt"),"\nInitial inactive layout passed; activating");ring.IsActive=true;}
            await ReorderSettle(800);
            ReorderCheck(without||(ring.IsLoaded&&ring.ActualWidth>0),"Dialog content loads without template failure");
            dialog.Hide();await shown;
            // Exercise the production dialog, injecting only the media worker so
            // these lifecycle checks cannot touch desktop playback or user files.
            var asset=new MediaAsset("decode-fixture",Path.Combine(output,"fixture.mp4"),"Decode fixture",100,10,1920,1080,"h264","sdr","fixture");
            var started=new TaskCompletionSource();var finish=new TaskCompletionSource<DecodeReport>();
            var success=ShowDecodeBenchmark(asset,(_,_,progress)=> {progress("硬件解码");started.TrySetResult();return finish.Task;});
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));await ReorderSettle();
            var actual=DecodeDialog();ReorderCheck(decodeBenchmarkShowing&&!actual.IsPrimaryButtonEnabled,"Real dialog starts worker only after opening and disables apply before result");
            bool duplicate=false;await ShowDecodeBenchmark(asset,(_,_,_)=> {duplicate=true;return finish.Task;});
            ReorderCheck(!duplicate,"Repeated entry cannot start a second benchmark");
            finish.SetResult(new(asset.Id,0,"fixture",DateTimeOffset.Now,[new DecodeMeasurement {Ok=true,Requested="Hardware",Decoder="D3D11VA",FpsLimit=0,Speed=2,SelectedFps=60}],"Hardware"));
            await ReorderSettle(500);
            ReorderCheck(actual.IsPrimaryButtonEnabled&&actual.CloseButtonText=="关闭","Successful results enable recommendation and render result card");
            actual.Hide();await success;ReorderCheck(!decodeBenchmarkShowing,"Success releases dialog guard");
            for(int attempt=0;attempt<3;attempt++) {
                bool cancelled=false;started=new();
                var pending=ShowDecodeBenchmark(asset,async(_,token,progress)=> {
                    started.TrySetResult();try {await Task.Delay(Timeout.Infinite,token);}finally {cancelled=token.IsCancellationRequested;}
                    return finish.Task.Result;
                });
                await started.Task.WaitAsync(TimeSpan.FromSeconds(5));await ReorderSettle();
                DecodeDialog().Hide();await pending;
                ReorderCheck(cancelled&&!decodeBenchmarkShowing,"Closing waits for cancellation cleanup, iteration "+attempt);
            }
            var failure=ShowDecodeBenchmark(asset,(_,_,_)=>Task.FromException<DecodeReport>(new IOException("fixture decoder failure")));
            await ReorderSettle(500);actual=DecodeDialog();
            ReorderCheck(!actual.IsPrimaryButtonEnabled&&actual.CloseButtonText=="关闭","Decoder failure remains inside dialog with close action");
            actual.Hide();await failure;
            BusyRing.IsActive=true;await ReorderSettle();BusyRing.IsActive=false;
            ReorderCheck(Microsoft.UI.Xaml.Application.Current.Resources["SystemControlHighlightAccentBrush"] is SolidColorBrush,"Native accent fallback preserves the SolidColorBrush interface contract");
            await CheckRefinement78(output);
            if(Environment.GetEnvironmentVariable("ZZZWALLPAPER_DECODE_FIXTURE") is {Length:>0} fixture) {
                var realAsset=asset with {Path=fixture};
                var real=ShowDecodeBenchmark(realAsset);
                var deadline=DateTime.UtcNow.AddSeconds(25);
                do {await ReorderSettle(150);actual=DecodeDialog();}while(actual.CloseButtonText!="关闭"&&DateTime.UtcNow<deadline);
                ReorderCheck(actual.IsPrimaryButtonEnabled,"Real hardware/software decode workers finish and populate the production dialog");
                actual.Hide();await real;
                ReorderCheck(store.GetSetting("decode.report.v1:"+realAsset.Id)!=null,"Real decode result persists only in isolated test profile");
            }
            ReorderCheckIsolation();
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new {passed=true,checks=reorderChecks}));
            App.ReorderIsolation?.WriteReport();Close();
        }catch(Exception error){App.ReorderIsolation?.WriteReport();EffectReorderRegressionProgram.Fail(error);}
    }
    private ContentDialog DecodeDialog()
    {
        ContentDialog? Find(DependencyObject node) {
            if(node is ContentDialog found)return found;
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)if(Find(VisualTreeHelper.GetChild(node,i)) is { } child)return child;
            return null;
        }
        return VisualTreeHelper.GetOpenPopupsForXamlRoot(Root.XamlRoot).Select(p=>Find(p.Child)).OfType<ContentDialog>().Single();
    }
}
