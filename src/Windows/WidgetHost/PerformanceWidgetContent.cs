using LumaWall.Contracts;
using Shortcut=LumaWall.Contracts.Shortcut;
using W=System.Windows;
using C=System.Windows.Controls;
using I=System.Windows.Input;
using M=System.Windows.Media;

namespace LumaWall.WidgetHost;

internal sealed class PerformanceWidgetContent : WidgetContent
{
    private readonly Dictionary<PerformanceMetrics,C.TextBlock> metricValues=new();
    private long nextPerformanceUpdate;
    internal override bool PerformanceUpdateDue=>Environment.TickCount64>=nextPerformanceUpdate;
    internal override void ResetPerformanceSchedule()=>nextPerformanceUpdate=0;
    internal PerformanceWidgetContent(WidgetContentContext context):base(context)
    {
            var fields=instance.Performance.Selected(instance.Metrics);int columns=instance.Performance.ColumnCount(fields.Length),rows=(fields.Length+columns-1)/columns;
            var row=new C.Grid {Margin=new W.Thickness(12,6,12,6)};
            for(int i=0;i<columns;i++)row.ColumnDefinitions.Add(new C.ColumnDefinition());
            for(int i=0;i<rows;i++)row.RowDefinitions.Add(new C.RowDefinition {Height=new W.GridLength(48)});
            for(int i=0;i<fields.Length;i++) {
                var stack=new C.StackPanel {Margin=new W.Thickness(10,0,0,0),VerticalAlignment=W.VerticalAlignment.Center};
                stack.Children.Add(new C.TextBlock {Text=PerformanceFields.Label(fields[i]),Foreground=WidgetVisuals.Muted,FontSize=11});
                var value=new C.TextBlock {Text="—",FontFamily=WidgetVisuals.Body,Foreground=WidgetVisuals.Ink,FontSize=16,TextTrimming=W.TextTrimming.CharacterEllipsis};
                stack.Children.Add(value);metricValues.Add(fields[i],value);C.Grid.SetColumn(stack,i%columns);C.Grid.SetRow(stack,i/columns);row.Children.Add(stack);
            }
            var button=new C.Button {Content=WidgetVisuals.Frame(row,out focusEdge),Style=WidgetVisuals.PlainButton(),HorizontalContentAlignment=W.HorizontalAlignment.Stretch,VerticalContentAlignment=W.VerticalAlignment.Stretch};
            button.GotKeyboardFocus+=(_,_)=>focusEdge.Background=editingLocked?WidgetVisuals.Edge:WidgetVisuals.Accent;
            button.LostKeyboardFocus+=(_,_)=>focusEdge.Background=WidgetVisuals.Edge;
            DragHandle(button,()=>Send(new() {Type="edit-widget",Id=instance.Id}),opensSettings:true);Element=button;
            W.Automation.AutomationProperties.SetName(button,UiLanguage.Text("整机性能监测"));
            WidgetVisuals.Action(menu,UiLanguage.Text("组件设置…"),"settings",()=>Send(new() {Type="edit-widget",Id=instance.Id}),()=>!editingLocked);
    }
    internal override IReadOnlyList<string>? UpdatePerformance(SystemPerformanceSample sample,long sampledAt) {
        if(metricValues.Count==0||!PerformanceUpdateDue)return null;
        nextPerformanceUpdate=sampledAt+instance.Performance.IntervalMilliseconds(instance.RefreshSeconds);
        var lines=new List<string>();
        foreach(var pair in metricValues) {
            double? value=sample.Get(pair.Key);pair.Value.Text=FormatMetric(pair.Key,value);
            string detail=pair.Key==PerformanceMetrics.Memory&&sample.UsedGiB is double used&&sample.TotalGiB is double total?$"{used:0.0} / {total:0.0} GiB":pair.Value.Text;
            lines.Add(PerformanceFields.Label(pair.Key)+" · "+(value==null?UiLanguage.Text("暂不可用"):detail));
        }
        return lines;
    }
    internal static string FormatMetric(PerformanceMetrics metric,double? value)
    {
        if(value is not double number||!double.IsFinite(number))return "—";
        if(metric is PerformanceMetrics.Cpu or PerformanceMetrics.Memory or PerformanceMetrics.Gpu or PerformanceMetrics.DiskBusy or PerformanceMetrics.GpuFan)return $"{number:0}%";
        if(metric is PerformanceMetrics.CpuTemperature or PerformanceMetrics.GpuTemperature)return $"{number:0.#} °C";
        if(metric is PerformanceMetrics.CpuPower or PerformanceMetrics.GpuPower)return $"{number:0.#} W";
        if(metric is PerformanceMetrics.CpuClock or PerformanceMetrics.GpuClock or PerformanceMetrics.GpuMemoryClock)return number>=1000?$"{number/1000:0.00} GHz":$"{number:0} MHz";
        if(metric is PerformanceMetrics.DiskReadLatency or PerformanceMetrics.DiskWriteLatency)return $"{number:0.00} ms";
        if(metric is PerformanceMetrics.ProcessCount or PerformanceMetrics.ThreadCount or PerformanceMetrics.HandleCount)return $"{number:N0}";
        if(metric==PerformanceMetrics.SystemUptime){var uptime=TimeSpan.FromSeconds(number);return uptime.TotalDays>=1?UiLanguage.Format($"{(int)uptime.TotalDays} 天 {uptime.Hours:00}:{uptime.Minutes:00}"):$"{uptime.Hours:00}:{uptime.Minutes:00}:{uptime.Seconds:00}";}
        string[] units=["B","KiB","MiB","GiB","TiB"];int index=0;
        while(number>=1024&&index<units.Length-1) {number/=1024;index++;}
        return $"{number:0.#} {units[index]}"+(metric is PerformanceMetrics.GpuMemory or PerformanceMetrics.GpuMemoryTotal or PerformanceMetrics.MemoryAvailable or PerformanceMetrics.MemoryCommitted?"":"/s");
    }
}
