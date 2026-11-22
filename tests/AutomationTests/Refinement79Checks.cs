using System.Text.Json;
using LumaWall.Automation;
using LumaWall.Contracts;
using LumaWall.Contracts.Sharing;
internal static class Refinement79Checks
{
    internal static void Run(Action<bool,string> check,Action<Action,string> reject)
    {
        JsonElement J(string json)=>JsonDocument.Parse(json).RootElement.Clone();
        var visual=new TaskbarVisualOptions {TransparentOpacity=25,FrostedOpacity=60,Blur=45,Color="#FF0000",ShowBorder=false};
        check(visual.ToCommand(TaskbarStyle.Transparent)=="2,25,45,16711680,0"&&visual.ToCommand(TaskbarStyle.Frosted)=="3,60,45,16711680,0","Appearance protocol has bounded, culture-independent tint, opacity, blur and border");
        check(new EffectCollection().TaskbarVisual==new TaskbarVisualOptions(),"Legacy profiles receive appearance defaults without a migration");
        reject(()=>(visual with {Blur=101}).Validate(),"Blur is bounded");
        reject(()=>(visual with {TransparentOpacity=-1}).Validate(),"Opacity is bounded");
        reject(()=>(visual with {Color="#abcd"}).Validate(),"Tint requires full RGB");
        var effects=VideoEffects.Original with {Collection=new EffectCollection {Added=EffectKind.HideTaskbar,TaskbarStyle=TaskbarStyle.Frosted,TaskbarVisual=visual}};
        check(AssetEffects.Copy(effects,VideoEffects.Original,EffectKind.HideTaskbar).GetCollection().TaskbarVisual==visual,"Effect copy preserves all taskbar parameters");
        check(JsonSerializer.Deserialize<VideoEffects>(JsonSerializer.Serialize(effects))!.Validate().GetCollection().TaskbarVisual==visual,"Taskbar details survive serialization and validation");
        ToolCatalog.Validate("wallpaper_set_screen_options",J("{\"screenId\":\"screen\",\"expectedRevision\":\"r\",\"taskbarVisual\":{\"blur\":45,\"color\":\"#123456\",\"showBorder\":false}}"));
        check(true,"MCP accepts appearance-only edits");
        reject(()=>ToolCatalog.Validate("wallpaper_set_screen_options",J("{\"screenId\":\"screen\",\"expectedRevision\":\"r\",\"taskbarVisual\":{\"blur\":101}}")),"MCP bounds taskbar parameters");
    }
}
