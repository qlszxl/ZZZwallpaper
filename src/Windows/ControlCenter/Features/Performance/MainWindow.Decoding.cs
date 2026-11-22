using System.Text.Json;
using LumaWall.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private ComboBox? decoderPicker;
    private bool decodeBenchmarkShowing;
    private static string DecoderLabel(DecoderMode mode)=>mode switch {DecoderMode.Hardware=>UiLanguage.Text("优先硬件解码"),DecoderMode.Software=>UiLanguage.Text("软件解码"),_=>UiLanguage.Text("自动选择")};
    private void ChangeDecoderMode(DecoderMode mode)
    {
        modules.Settings.Set("decoderMode",mode.ToString());wallpapers?.SetDecoderMode(mode);
        if(decoderPicker!=null&&decoderPicker.SelectedIndex!=(int)mode)decoderPicker.SelectedIndex=(int)mode;
    }
    private void InitializeDecodeSettings(StackPanel host)
    {
        host.Children.Add(UtilityText(UiLanguage.Text("视频解码"),"UtilityHeading"));
        decoderPicker=new ComboBox {Style=HomeStyle("AudioRuleComboBox"),HorizontalAlignment=HorizontalAlignment.Stretch};
        foreach(var mode in Enum.GetValues<DecoderMode>())decoderPicker.Items.Add(new ComboBoxItem {Content=DecoderLabel(mode),Tag=mode});
        decoderPicker.SelectedIndex=(int)modules.Settings.Decoder();
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(decoderPicker,UiLanguage.Text("视频解码方式"));
        decoderPicker.SelectionChanged+=(_,_)=> {
            if(syncingSettings||decoderPicker.SelectedItem is not ComboBoxItem {Tag:DecoderMode mode})return;
            try {ChangeDecoderMode(mode);}catch(Exception ex) {ShowError(UiLanguage.Text("解码设置未保存：")+ex.Message,ex);}
        };
        host.Children.Add(decoderPicker);
        var button=new Button {Style=HomeStyle("UtilityButton"),HorizontalAlignment=HorizontalAlignment.Left};
        UtilityButtonContent(button,UiLanguage.Text("测试视频解码性能"));
        button.Click+=async(_,_)=> {
            var asset=SelectedLibraryAssets() is [var chosen]&&!chosen.IsImage?chosen:selected;
            if(asset==null||asset.IsImage)asset=SelectedScreen==null?null:wallpapers?.Running.GetValueOrDefault(SelectedScreen.DeviceName)?.Asset;
            if(asset==null||asset.IsImage) {Inform(UiLanguage.Text("请先在素材库选择要测试的视频。"));return;}
            try {await ShowDecodeBenchmark(asset);}catch(Exception ex) {ShowError(UiLanguage.Text("无法打开解码测试：")+ex.Message,ex);}
        };
        host.Children.Add(button);
    }
    private async void LibraryDecodeTest_Click(object sender,RoutedEventArgs e)
    {
        if(SelectedLibraryAssets() is not [var asset]||asset.IsImage)return;
        try {await ShowDecodeBenchmark(asset);}catch(Exception ex) {ShowError(UiLanguage.Text("无法打开解码测试：")+ex.Message,ex);}
    }
    private async Task<DecodeReport> MeasureDecode(MediaAsset asset,int cap,CancellationToken token,Action<string>? progress=null)
    {
        var report=await MediaOperations.Benchmark(asset,cap,token,progress);token.ThrowIfCancellationRequested();
        modules.Settings.Set("decode.report.v1:"+asset.Id,JsonSerializer.Serialize(report));
        return report;
    }
    private static string DecodeMemory(double? bytes)=>bytes is not { } value?UiLanguage.Text("不可用"):value>=1073741824?$"{value/1073741824:0.00} GiB":$"{value/1048576:0.#} MiB";
    private static FrameworkElement DecodeResultCard(DecodeMeasurement row,bool recommended)
    {
        var content=new StackPanel {Spacing=14};
        var heading=new Grid {ColumnSpacing=8};
        heading.ColumnDefinitions.Add(new());heading.ColumnDefinitions.Add(new() {Width=GridLength.Auto});
        heading.Children.Add(UtilityText(row.Requested=="Hardware"?UiLanguage.Text("硬件解码"):UiLanguage.Text("软件解码"),"UtilityHeading"));
        if(recommended) {
            var badge=UtilityText(UiLanguage.Text("建议"),"UtilityLabel");badge.Foreground=HomeBrush("InteractionAccentBrush");
            Grid.SetColumn(badge,1);heading.Children.Add(badge);
        }
        content.Children.Add(heading);
        if(!row.Ok) {
            content.Children.Add(UtilityText(UiLanguage.Text("测试失败"),"UtilityLabel"));content.Children.Add(UtilityText(row.Error??UiLanguage.Text("未取得结果")));
        } else {
            if(row.Requested=="Hardware"&&row.Decoder!="D3D11VA")content.Children.Add(UtilityText(UiLanguage.Text("已回退软件解码")));
            var speed=new StackPanel {Spacing=2};speed.Children.Add(UtilityText(UiLanguage.Text("处理帧率 · FPS")));
            var value=UtilityText($"{row.SelectedFps:0.0}","UtilityHeading");value.FontSize=30;speed.Children.Add(value);content.Children.Add(speed);
            var metrics=new Grid {ColumnSpacing=12,RowSpacing=12};
            void Metric(string label,string text) {
                var cell=new StackPanel {Spacing=3};cell.Children.Add(UtilityText(label));cell.Children.Add(UtilityText(text,"UtilityHeading"));metrics.Children.Add(cell);
            }
            Metric(UiLanguage.Text("实时倍速"),$"{row.Speed:0.00}×");Metric(UiLanguage.Text("CPU 平均"),$"{row.CpuPercent:0.0}%");
            Metric(UiLanguage.Text("GPU 平均"),row.GpuPercent is { } gpu?$"{gpu:0.0}%":UiLanguage.Text("不可用"));Metric(UiLanguage.Text("内存峰值"),DecodeMemory(row.PeakWorkingSetBytes));
            Metric(UiLanguage.Text("专用显存峰值"),DecodeMemory(row.PeakDedicatedGpuBytes));Metric(UiLanguage.Text("共享显存峰值"),DecodeMemory(row.PeakSharedGpuBytes));
            FlowCards(metrics,2);content.Children.Add(metrics);
            var details=new StackPanel {Spacing=6};
            details.Children.Add(UtilityText(UiLanguage.Format($"平均耗时  {row.MeanMs:0.00} ms")));
            details.Children.Add(UtilityText(UiLanguage.Format($"P95 耗时  {row.P95Ms:0.00} ms")));
            details.Children.Add(UtilityText(UiLanguage.Text("GPU 峰值  ")+(row.PeakGpuPercent is { } peak?$"{peak:0.0}%":UiLanguage.Text("不可用"))));
            details.Children.Add(UtilityText(UiLanguage.Text("私有提交峰值  ")+DecodeMemory(row.PeakPrivateBytes)));
            content.Children.Add(new Expander {Style=HomeStyle("EffectsExpander"),Header=UtilityText(UiLanguage.Text("详细数据"),"UtilityLabel"),Content=details,HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch});
        }
        return new Border {Style=HomeStyle("LibraryInset"),Padding=new Thickness(16),Child=content};
    }
    private Task ShowDecodeBenchmark(MediaAsset asset)=>ShowDecodeBenchmark(asset,(cap,token,progress)=>MeasureDecode(asset,cap,token,progress));
    private async Task ShowDecodeBenchmark(MediaAsset asset,Func<int,CancellationToken,Action<string>,Task<DecodeReport>> measure)
    {
        if(decodeBenchmarkShowing)return;
        decodeBenchmarkShowing=true;
        try {await RunDecodeBenchmarkDialog(asset,measure);}
        finally {decodeBenchmarkShowing=false;}
    }
    private async Task RunDecodeBenchmarkDialog(MediaAsset asset,Func<int,CancellationToken,Action<string>,Task<DecodeReport>> measure)
    {
        using var cancellation=CancellationTokenSource.CreateLinkedTokenSource(mediaOperationLifetime.Token);
        var body=new StackPanel {Spacing=12,Width=Math.Max(250,Math.Min(600,Root.ActualWidth-140))};
        body.Children.Add(UtilityText(asset.Name,"UtilityHeading"));
        var status=UtilityText(UiLanguage.Text("准备测试…"));
        var progress=new ProgressRing {Width=20,Height=20,IsActive=true,Foreground=HomeBrush("InteractionSolidAccentBrush")};
        var progressRow=new StackPanel {Orientation=Orientation.Horizontal,Spacing=10};progressRow.Children.Add(progress);progressRow.Children.Add(status);
        var results=new StackPanel {Spacing=18};body.Children.Add(progressRow);body.Children.Add(results);
        var scroll=new ScrollViewer {Style=HomeStyle("HomeScrollViewer"),Content=body,MaxHeight=Math.Max(220,Root.ActualHeight-220),VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        var dialog=new ContentDialog {XamlRoot=Root.XamlRoot,Title=UiLanguage.Text("解码性能测试"),Content=scroll,PrimaryButtonText=UiLanguage.Text("采用建议方式"),CloseButtonText=UiLanguage.Text("取消测试"),Style=HomeStyle("LibraryDialog"),IsPrimaryButtonEnabled=false,DefaultButton=ContentDialogButton.None};
        dialog.Resources["ContentDialogMaxWidth"]=740d;
        DecodeReport? report=null;bool closed=false;int cap=wallpapers?.FrameRateLimit??modules.Settings.FpsLimit();
        var operation=OperationTrace.Current;Task measurement=Task.CompletedTask;
        async Task Measure()
        {
            try {
                OperationTrace.Record(operation,"decode_benchmark","started");
                report=await measure(cap,cancellation.Token,text=> {if(!closed)status.Text=text;});
                OperationTrace.Record(operation,"decode_benchmark","completed");
                if(closed)return;
                foreach(var group in report.Results.GroupBy(row=>row.FpsLimit)) {
                    var section=new StackPanel {Spacing=10};
                    double sourceFps=group.FirstOrDefault(row=>row.Ok)?.SourceFps??0;
                    string title=group.Key==0?UiLanguage.Text("原始帧率")+(sourceFps>0?$" · {sourceFps:0.##} FPS":""):UiLanguage.Format($"限制 {group.Key} FPS");
                    section.Children.Add(UtilityText(title,"UtilityLabel"));
                    var comparison=new Grid {ColumnSpacing=12};
                    foreach(var row in group)comparison.Children.Add(DecodeResultCard(row,row.Ok&&row.FpsLimit==cap&&row.Requested==report.Recommendation));
                    UtilityAdaptiveGrid(comparison,2,500);section.Children.Add(comparison);results.Children.Add(section);
                }
                dialog.CloseButtonText=UiLanguage.Text("关闭");
                bool available=Enum.TryParse<DecoderMode>(report.Recommendation,out var mode);
                bool sufficient=report.Results.Any(r=>r.FpsLimit==cap&&r.Ok&&r.Speed>=1.2);
                status.Text=available?UiLanguage.Format($"建议：{DecoderLabel(mode)}")+(sufficient?"":UiLanguage.Text(" · 余量不足")):UiLanguage.Text("未取得有效结果");
                dialog.IsPrimaryButtonEnabled=available;
            }catch(OperationCanceledException) {OperationTrace.Record(operation,"decode_benchmark","cancelled");if(!closed)status.Text=UiLanguage.Text("测试已取消");}
             catch(Exception ex) {OperationTrace.Record(operation,"decode_benchmark","failed",ex.GetType().Name);AppLog.Warning("DecodeBenchmark","Decode benchmark failed",ex);if(!closed) {status.Text=UiLanguage.Text("测试失败：")+ex.Message;dialog.CloseButtonText=UiLanguage.Text("关闭");}}
             finally {if(!closed) {progress.IsActive=false;progress.Visibility=Visibility.Collapsed;}}
        }
        dialog.PrimaryButtonClick+=(_,args)=> {
            if(report==null||!Enum.TryParse<DecoderMode>(report.Recommendation,out var mode)) {args.Cancel=true;return;}
            try {ChangeDecoderMode(mode);Inform(UiLanguage.Text("已切换为")+DecoderLabel(mode)+"。");}
            catch(Exception ex) {args.Cancel=true;status.Text=UiLanguage.Text("设置未保存：")+ex.Message;}
        };
        // Start only after the dialog has opened. Keep its cancellation source
        // alive until the worker exits, and never update an unloaded dialog.
        dialog.Opened+=(_,_)=> {OperationTrace.Record(operation,"decode_dialog","opened");measurement=Measure();};
        dialog.Closing+=(_,_)=> {closed=true;progress.IsActive=false;cancellation.Cancel();};
        try {await dialog.ShowAsync();}
        finally {closed=true;progress.IsActive=false;cancellation.Cancel();await measurement;OperationTrace.Record(operation,"decode_dialog","closed");}
    }
}
