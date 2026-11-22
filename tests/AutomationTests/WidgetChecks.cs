using System.Text.Json;
using LumaWall.Automation;
using LumaWall.Contracts;

internal static class WidgetChecks
{
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        JsonElement J(string value)=>JsonDocument.Parse(value).RootElement.Clone();
        WidgetInstance Apply(WidgetInstance current,string json) {
            var node=System.Text.Json.Nodes.JsonNode.Parse(json)!;node["expectedRevision"]="fixture";
            var args=JsonSerializer.SerializeToElement(node);ToolCatalog.Validate("widget_add",args);return WidgetPatch.Apply(current,args);
        }
        foreach(var kind in Enum.GetValues<WidgetKind>()) {
            var args=kind==WidgetKind.Shortcuts?ToolCatalog.Json(new {screenId="display",kind=kind.ToString(),expectedRevision="fixture",name="Site",url="https://example.com"}):ToolCatalog.Json(new {screenId="display",kind=kind.ToString(),expectedRevision="fixture"});
            ToolCatalog.Validate("widget_add",args);
            check(WidgetPatch.Apply(new() {Kind=kind},args).Kind==kind,"All UI widget kinds are discoverable: "+kind);
        }
        var top=Apply(new() {Kind=WidgetKind.Mask},"""{"screenId":"display","kind":"Mask","x":0,"y":0,"widthDip":1536,"heightDip":90,"opacity":1,"visual":{"FillColor":"#000000","FillOpacity":1,"Radius":0,"Padding":0,"ClickThrough":true}}""");
        var bottom=Apply(new() {Kind=WidgetKind.Mask},"""{"screenId":"display","kind":"Mask","x":0,"y":1,"widthDip":1536,"heightDip":90,"opacity":1,"visual":{"FillColor":"#000000","FillOpacity":1,"Radius":0,"Padding":0,"ClickThrough":true}}""");
        var state=WidgetPatch.AddWebsite(new(),new() {Kind=WidgetKind.Shortcuts},"必应","https://www.bing.com/");
        state=WidgetPatch.AddWebsite(state,new() {Kind=WidgetKind.Shortcuts},"绝区零","https://zzz.mihoyo.com/");
        state=(state with {Widgets=[..state.Widgets,top,bottom]}).Validate();
        var restored=WidgetConfiguration.FromJson(state.ToJson());
        check(restored.Widgets.Length==4&&restored.Shortcuts.Length==2&&restored.Widgets.All(w=>w.Kind!=WidgetKind.Shortcuts||restored.Shortcuts.Any(c=>c.Id==w.ShortcutId)),"Two website shortcuts and two masks persist with correct target bindings");
        check(top.Visual.Width==1536&&top.Visual.FillColor=="#000000"&&top.Visual.FillOpacity==1&&top.Opacity==1&&top.Visual.ClickThrough&&top.Y==0&&bottom.Y==1,"Opaque black click-through bars retain full-width logical dimensions and edge placement");
        var edited=WidgetPatch.UpdateWebsite(state,state.Widgets[0],J("""{"name":"Bing","url":"https://www.bing.com/search?q=test"}"""));
        check(edited.Shortcuts[0].Name=="Bing"&&edited.Shortcuts[1]==state.Shortcuts[1],"Website edit preserves other shortcuts");
        reject(()=>WidgetPatch.AddWebsite(state,new() {Kind=WidgetKind.Shortcuts},"bad","javascript:alert(1)"),"Website creation rejects non-HTTP targets");
        var command=new Shortcut("command","Command","cmd.exe","common",0,Kind:ShortcutKind.Command);
        reject(()=>WidgetPatch.UpdateWebsite(state with {Shortcuts=[command]},new() {Kind=WidgetKind.Shortcuts,ShortcutId=command.Id},J("""{"url":"https://example.com"}""")),"Website tool cannot rewrite a command shortcut");
        reject(()=>Apply(top,"""{"screenId":"display","kind":"Mask","visual":{"FillColor":"black"}}"""),"Invalid mask color rejected");
        reject(()=>Apply(top,"""{"screenId":"display","kind":"Mask","visual":{"Image":"base64"}}"""),"MCP image injection rejected");
        reject(()=>Apply(top,"""{"screenId":"display","kind":"Mask","search":{"Engine":1}}"""),"Options for another component kind rejected");
        var text=Apply(new() {Kind=WidgetKind.Text,Visual=new() {FillColor="#123456"}},"""{"screenId":"display","kind":"Text","text":"新文本","visual":{"FontSize":40}}""");
        check(text.Visual.Text=="新文本"&&text.Visual.FontSize==40&&text.Visual.FillColor=="#123456","Partial visual edit preserves unmentioned settings");
        var search=Apply(new() {Kind=WidgetKind.Search},"""{"screenId":"display","kind":"Search","search":{"Engine":4,"CustomUrl":"https://example.com/?q={query}"}}""");
        check(search.Search.Engine==SearchEngine.Custom,"Search schema enum and template apply");
        reject(()=>Apply(new() {Kind=WidgetKind.Search},"""{"screenId":"display","kind":"Search","search":{"Engine":4,"CustomUrl":"https://example.com/"}}"""),"Invalid custom search template rejected");
        var performance=Apply(new() {Kind=WidgetKind.Performance},"""{"screenId":"display","kind":"Performance","metrics":7,"performance":{"Order":[4,2,1],"Columns":3}}""");
        check((int)performance.Metrics==7&&performance.Performance.Order[0]==PerformanceMetrics.Gpu,"Performance metrics and ordered layout apply");
        reject(()=>Apply(new() {Kind=WidgetKind.Performance},"""{"screenId":"display","kind":"Performance","performance":{"Order":[1,1]}}"""),"Duplicate metric ordering rejected");
        var spectrum=Apply(new() {Kind=WidgetKind.Spectrum},"""{"screenId":"display","kind":"Spectrum","spectrum":{"Bands":48,"Glow":true,"Weighting":{"Enabled":true}}}""");
        check(spectrum.Spectrum.Bands==48&&spectrum.Spectrum.Glow&&spectrum.Spectrum.Weighting.Enabled,"Spectrum nested edits preserve defaults");
        check(!WidgetPatch.Schemas["Visual"].GetProperty("properties").TryGetProperty("Image",out _),"Discovered component schema omits image bytes");
    }
}
