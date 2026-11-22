using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

internal static class LiveChecks
{
    internal static async Task Run(string bridge)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(100));
        await using var client=await McpClient.CreateAsync(new StdioClientTransport(new() {Name="Isolated ZZZwallpaper smoke test",Command=Path.GetFullPath(bridge),Arguments=[]}),cancellationToken:timeout.Token);
        int checks=0;
        void Check(bool value,string message) {if(!value)throw new Exception("FAIL: "+message);Console.WriteLine("PASS: "+message);checks++;}
        async Task<JsonElement> Call(string name,Dictionary<string,object?>? values=null,bool error=false) {
            var result=await client.CallToolAsync(name,values??new(),cancellationToken:timeout.Token);
            if((result.IsError==true)!=error)throw new Exception(name+": "+string.Join(" ",result.Content.OfType<TextContentBlock>().Select(c=>c.Text)));
            return result.StructuredContent??JsonDocument.Parse(result.Content.OfType<TextContentBlock>().First().Text).RootElement.Clone();
        }
        var state=await Call("app_get_state");Check(state.GetProperty("version").GetString()=="0.19.81","Live application version and enabled access");
        var media=await Call("media_list");Check(media.GetProperty("items").GetArrayLength()==1&&media.GetProperty("items")[0].GetProperty("id").GetString()=="mcp-smoke","Isolated synthetic fixture; no user library");
        string screen=state.GetProperty("screens")[0].GetProperty("id").GetString()!;
        var applied=await Call("wallpaper_apply",new(){{"screenId",screen},{"assetId","mcp-smoke"}});Check(applied.GetProperty("applied").GetBoolean(),"Apply existing video through real controller");
        var effects=await Call("effect_get",new(){{"screenId",screen}});string revision=effects.GetProperty("revision").GetString()!;
        var changed=await Call("effect_set",new(){{"screenId",screen},{"expectedRevision",revision},{"effect","Color"},{"enabled",true},{"parameters",new {Saturation=.8}}});
        Check(changed.GetProperty("saved").GetBoolean()&&changed.GetProperty("renderConfirmed").GetBoolean(),"Effects saved and acknowledged by native renderer");
        var after=await Call("effect_get",new(){{"screenId",screen}});Check(after.GetProperty("parameters").GetProperty("Saturation").GetDouble()==.8,"Read-back matches actual saved effect");
        await Call("effect_set",new(){{"screenId",screen},{"expectedRevision",revision},{"effect","Color"},{"enabled",false}},true);Check(true,"Live stale-revision edit rejected");
        await Call("effect_undo",new(){{"screenId",screen},{"expectedRevision",after.GetProperty("revision").GetString()}});
        var restored=await Call("effect_get",new(){{"screenId",screen}});Check(restored.GetProperty("parameters").GetProperty("Saturation").GetDouble()==effects.GetProperty("parameters").GetProperty("Saturation").GetDouble(),"Undo restores real effect configuration");
        await Call("wallpaper_set_fps_limit",new(){{"fps",24}});state=await Call("app_get_state");Check(state.GetProperty("fpsLimit").GetInt32()==24,"FPS setting updates controller state");
        var paused=await Call("wallpaper_set_paused",new(){{"screenId",screen},{"paused",true}});Check(paused.GetProperty("userPaused").GetBoolean(),"Pause through real controller");
        await Call("wallpaper_set_paused",new(){{"screenId",screen},{"paused",false}});await Call("wallpaper_set_sound",new(){{"screenId",screen},{"enabled",false}});
        await Call("wallpaper_set_playback",new(){{"screenId",screen},{"mode","SingleLoop"}});
        var added=await Call("widget_add",new(){{"screenId",screen},{"kind","Text"},{"text","MCP 验证"}});string id=added.GetProperty("widgetId").GetString()!;
        await Call("widget_update",new(){{"widgetId",id},{"x",.1},{"y",.2},{"widthDip",300},{"heightDip",80},{"enabled",false}});
        state=await Call("app_get_state");var widget=state.GetProperty("widgets").EnumerateArray().Single(w=>w.GetProperty("id").GetString()==id);
        Check(widget.GetProperty("X").GetDouble()==.1&&widget.GetProperty("size").GetProperty("widthDip").GetDouble()==300&&!widget.GetProperty("Enabled").GetBoolean(),"Widget creation, position, actual dimensions and visibility");
        var performance=await Call("diagnostics_sample_performance");Check(performance.GetProperty("processes").GetArrayLength()>=2,"Independent performance sampling");
        Console.WriteLine($"Live application checks passed: {checks}");
    }
}
