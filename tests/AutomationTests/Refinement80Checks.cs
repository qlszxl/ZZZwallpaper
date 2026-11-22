using System.Text.Json;
using LumaWall.Automation;
using LumaWall.Contracts;
internal static class Refinement80Checks
{
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        JsonElement J(string text)=>JsonDocument.Parse(text).RootElement.Clone();
        check(WallpaperPosition.FromJson("{\"X\":25,\"Y\":-30}")==new WallpaperPosition(25,-30,100),"Old positions retain 100% scale");
        check(WallpaperPosition.FromJson(new WallpaperPosition(-20,35,175).ToJson())==new WallpaperPosition(-20,35,175),"Scale round trips with both position axes");
        reject(()=>new WallpaperPosition(0,0,401).Validate(),"Position scale upper bound");reject(()=>new WallpaperPosition(0,0,9).Validate(),"Position scale lower bound");
        var center=WallpaperPreviewLayout.Calculate(800,600,2,16/9d,false,new());var scaled=WallpaperPreviewLayout.Calculate(800,600,2,16/9d,false,new(0,0,50));
        check(Math.Abs(center.Content.Width/2-scaled.Content.Width)<.001&&Math.Abs(center.Content.Height/2-scaled.Content.Height)<.001,"Preview uniformly scales around the center");
        ToolCatalog.Validate("wallpaper_set_screen_options",J("{\"screenId\":\"s\",\"expectedRevision\":\"r\",\"scale\":175}"));check(true,"MCP accepts scale-only changes");
        reject(()=>ToolCatalog.Validate("wallpaper_set_screen_options",J("{\"screenId\":\"s\",\"expectedRevision\":\"r\",\"scale\":401}")),"MCP bounds scale");
        var options=new PerformanceWidgetOptions();options.Validate();check(options.Order.Length==27,"Expanded default metric ordering validates");
        var legacy=new PerformanceWidgetOptions {Order=[PerformanceMetrics.Gpu,PerformanceMetrics.Cpu,PerformanceMetrics.Memory]};legacy.Validate();
        check(legacy.Selected(PerformanceMetrics.Cpu|PerformanceMetrics.GpuTemperature).SequenceEqual(new[]{PerformanceMetrics.Cpu,PerformanceMetrics.GpuTemperature}),"Old metric order appends new selections without losing order");
        var args=J("{\"widgetId\":\"one\",\"expectedRevision\":\"r\",\"alwaysOnTop\":true,\"metrics\":134217727,\"performance\":{\"Order\":[512,2048,4096,256],\"Columns\":4}}");
        ToolCatalog.Validate("widget_update",args);var changed=WidgetPatch.Apply(new WidgetInstance {Id="one",Kind=WidgetKind.Performance},args);
        check(changed.AlwaysOnTop&&changed.Metrics==PerformanceFields.All&&changed.Performance.Order[0]==PerformanceMetrics.CpuTemperature,"MCP applies new metrics and topmost through validated contracts");
        var state=new WidgetConfiguration {Widgets=[changed,new() {Id="two",Kind=WidgetKind.Text,Layer=999}],LayoutGroups=[]};
        check(WidgetStacking.FrontToBack(state).First().Id=="one","Topmost band precedes ordinary layer numbers");
        check(WidgetConfiguration.FromJson(state.ToJson()).Widgets[0].AlwaysOnTop,"Widget configuration persists topmost");
        var grouped=changed with {LayoutGroupId="g"};var groupedState=state with {Widgets=[grouped],LayoutGroups=[new() {Id="g"}]};
        var onlyTop=J("{\"widgetId\":\"one\",\"expectedRevision\":\"r\",\"idempotencyKey\":\"test\",\"alwaysOnTop\":false}");
        ToolCatalog.Validate("widget_update",onlyTop);check(WidgetPatch.CanUpdate(groupedState,grouped,onlyTop),"Grouped topmost edit supports retry keys without relaxing layout locks");
    }
}
