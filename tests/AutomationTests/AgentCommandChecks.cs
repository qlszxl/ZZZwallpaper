using System.Diagnostics;
using System.Text;
using System.Text.Json;
using LumaWall.Automation;

internal static class AgentCommandChecks
{
    public static async Task Run(string executable,Action<bool,string> check)
    {
        executable=Path.GetFullPath(executable);
        async Task<(int Exit,JsonElement Json)> Invoke(string[] args,string? input=null) {
            var start=new ProcessStartInfo(executable) {UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardInputEncoding=new UTF8Encoding(false)};
            foreach(string arg in args)start.ArgumentList.Add(arg);
            using var process=Process.Start(start)!;
            var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try {
                if(input!=null)await process.StandardInput.WriteAsync(input.AsMemory(),timeout.Token);
                process.StandardInput.Close();await process.WaitForExitAsync(timeout.Token);
                string text=await output;await errors;
                using var json=JsonDocument.Parse(text);return(process.ExitCode,json.RootElement.Clone());
            } finally {if(!process.HasExited)process.Kill(entireProcessTree:true);}
        }
        Task<(int Exit,JsonElement Json)> InvokeWrite(string[] args,string input) {
            var node=System.Text.Json.Nodes.JsonNode.Parse(input)!;node["expectedRevision"]="fixture";
            return Invoke(args,node.ToJsonString());
        }
        foreach(var item in new[] {
            (new[]{"--execute","cmd.exe"},"{}"),
            (new[]{"--agent-call","execute_code"},"{}"),
            (new[]{"--agent-call","app_get_state"},"{\"code\":\"bad\"}"),
            (new[]{"--agent-call","wallpaper_set_fps_limit"},"{\"fps\":60,\"fps\":120}"),
            (new[]{"--agent-call","wallpaper_set_fps_limit"},"{\"fps\":241}"),
            (new[]{"--agent-call","app_get_state"},"[]"),
            (new[]{"--agent-call","app_get_state"},"{"),
            (new[]{"--agent-call","app_get_state"},new string(' ',65537))
        }) {
            var result=await Invoke(item.Item1,item.Item2);
            check(result.Exit!=0&&result.Json.GetProperty("isError").GetBoolean(),"One-shot MCP rejects invalid command/arguments: "+string.Join(' ',item.Item1)+" / "+item.Item2.Length+" bytes");
        }
        var tools=await Invoke(["--agent-tools"]);
        check(tools.Json.GetProperty("data").GetProperty("tools").EnumerateArray().All(t=>t.TryGetProperty("outputSchema",out _)&&t.TryGetProperty("annotations",out _)),"CLI exports output schemas and annotations for every tool");
        var oldName=await Invoke(["--agent-call","get_state"],"{}");
        check(oldName.Json.GetProperty("data").GetProperty("errorCode").GetString()=="unknown_tool","CLI rejects legacy tool names");
        var missingRevision=await Invoke(["--agent-call","widget_remove"],"{\"widgetId\":\"fixture\"}");
        check(missingRevision.Json.GetProperty("data").GetProperty("errorCode").GetString()=="missing_required_field"&&missingRevision.Json.GetProperty("data").GetProperty("field").GetString()=="arguments.expectedRevision","CLI reports structured missing revision error");
        check(tools.Exit==0&&tools.Json.GetProperty("data").GetProperty("tools").GetArrayLength()==LumaWall.Automation.ToolCatalog.All.Count&&!tools.Json.GetProperty("data").GetProperty("connectionVerified").GetBoolean(),"Tool discovery reports schemas without claiming an app connection");

        var pendingStart=new ProcessStartInfo(executable) {UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
        pendingStart.ArgumentList.Add("--agent-call");pendingStart.ArgumentList.Add("app_get_state");
        using(var pending=Process.Start(pendingStart)!) {
            var output=pending.StandardOutput.ReadToEndAsync();var errors=pending.StandardError.ReadToEndAsync();
            using var inputTimeout=new CancellationTokenSource(TimeSpan.FromSeconds(55));
            try {
                // Keep stdin open indefinitely: the bridge must still respect its own 45-second deadline.
                await pending.WaitForExitAsync(inputTimeout.Token);
                using var result=JsonDocument.Parse(await output);await errors;
                check(pending.ExitCode!=0&&result.RootElement.GetProperty("isError").GetBoolean(),"Unclosed standard input cannot hang the one-shot client indefinitely");
            } finally {if(!pending.HasExited)pending.Kill(entireProcessTree:true);}
        }

        // Exclusive ownership makes this fixture unable to coexist with a real running MCP service.
        LocalOperationServer fixture;
        try {fixture=new LocalOperationServer((request,token)=>Task.FromResult(OperationReply.Success(new {version="test-fixture",mcpEnabled=true,operation=request.Name,arguments=request.Arguments})),_=>{});}
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException) {Console.WriteLine("SKIP: active app owns MCP pipe; no real application operations attempted.");return;}
        using(fixture) {
            var connected=await Invoke(["--agent-connect"]);
            check(connected.Exit==0&&connected.Json.GetProperty("data").GetProperty("version").GetString()=="test-fixture", "One-shot connection reads state through real stdio MCP and named pipe");
            var changed=await InvokeWrite(["--agent-call","wallpaper_set_fps_limit"],"{\"fps\":60}");
            check(changed.Exit==0&&changed.Json.GetProperty("data").GetProperty("arguments").GetProperty("fps").GetInt32()==60,"One-shot mutation reaches the same bounded MCP service");
            var unicode=await InvokeWrite(["--agent-call","widget_add"],"{\"screenId\":\"测试屏幕\",\"kind\":\"Text\",\"text\":\"你好，壁纸 🌸 ' $ `\"}");
            check(unicode.Exit==0&&unicode.Json.GetProperty("data").GetProperty("arguments").GetProperty("text").GetString()=="你好，壁纸 🌸 ' $ `","UTF-8 and shell characters round-trip as data");
            var bom=await Invoke(["--agent-call","app_get_state"],"\uFEFF{}");
            check(bom.Exit==0,"UTF-8 BOM accepted on standard input");
            var website=await InvokeWrite(["--agent-call","widget_add"],"{\"screenId\":\"display\",\"kind\":\"Shortcuts\",\"name\":\"必应\",\"url\":\"https://www.bing.com/\"}");
            check(website.Exit==0&&website.Json.GetProperty("data").GetProperty("arguments").GetProperty("kind").GetString()=="Shortcuts","Website creation parameters reach the same MCP service");
            var mask=await InvokeWrite(["--agent-call","widget_add"],"{\"screenId\":\"display\",\"kind\":\"Mask\",\"visual\":{\"FillColor\":\"#000000\",\"FillOpacity\":1,\"ClickThrough\":true}}");
            check(mask.Exit==0&&mask.Json.GetProperty("data").GetProperty("arguments").GetProperty("visual").GetProperty("ClickThrough").GetBoolean(),"Mask options round-trip through real stdio MCP");
            var imported=await InvokeWrite(["--agent-call","media_import"],JsonSerializer.Serialize(new {path=@"C:\素材 & $\图片.png"}));
            check(imported.Exit==0&&imported.Json.GetProperty("data").GetProperty("arguments").GetProperty("path").GetString()==@"C:\素材 & $\图片.png","Local image import path reaches MCP as literal data");
            var scoped=await Invoke(["--agent-call","effect_set_scope"],JsonSerializer.Serialize(new {screenId="display",assetId="image-a",effect="Glow",scope="asset",expectedRevision="fixture"}));
            check(scoped.Exit==0&&scoped.Json.GetProperty("data").GetProperty("arguments").GetProperty("assetId").GetString()=="image-a","Material effect scope reaches real stdio MCP");
            var material=await Invoke(["--agent-call","effect_set"],JsonSerializer.Serialize(new {screenId="display",assetId="image-a",effect="EdgeOutline",scope="asset",expectedRevision="fixture",enabled=true,parameters=new {EdgeOutline=new {Width=2.5,Color="#336699"}}}));
            check(material.Exit==0&&material.Json.GetProperty("data").GetProperty("arguments").GetProperty("parameters").GetProperty("EdgeOutline").GetProperty("Width").GetDouble()==2.5,"Advanced material effect parameters survive real stdio MCP");
            var clear=await Invoke(["--agent-call","effect_clear"],JsonSerializer.Serialize(new {screenId="display",scope="screen",expectedRevision="fixture"}));
            check(clear.Exit==0&&!clear.Json.GetProperty("data").GetProperty("arguments").TryGetProperty("assetId",out _),"Empty-library display clear reaches MCP without a material ID");
            var copy=await Invoke(["--agent-call","effect_copy_screen_to_asset"],JsonSerializer.Serialize(new {screenId="display",assetId="image-a",expectedRevision="fixture"}));
            check(copy.Exit==0&&copy.Json.GetProperty("data").GetProperty("arguments").GetProperty("assetId").GetString()=="image-a","Display-to-material copy reaches real stdio MCP");
            var audio=await Invoke(["--agent-call","audio_set_rule"],JsonSerializer.Serialize(new {screenId="display",assetId="image-a",scope="asset",expectedRevision="fixture",rule=new {Parameter="GlowStrength",Enabled=true,Band=1,Response=1,Weak=.1,Strong=.8,Attack=(double?)null,Release=.3}}));
            check(audio.Exit==0&&audio.Json.GetProperty("data").GetProperty("arguments").GetProperty("rule").GetProperty("Attack").ValueKind==JsonValueKind.Null,"Audio parameter rule and nullable overrides reach real stdio MCP");
            var removeAudio=await Invoke(["--agent-call","audio_remove_rule"],JsonSerializer.Serialize(new {screenId="display",assetId="image-a",scope="asset",expectedRevision="fixture",parameter="GlowStrength"}));
            check(removeAudio.Exit==0&&removeAudio.Json.GetProperty("data").GetProperty("arguments").GetProperty("parameter").GetString()=="GlowStrength","Audio inheritance restore reaches real stdio MCP");
            bool duplicateRejected=false;
            try {using var duplicate=new LocalOperationServer((_,_)=>Task.FromResult(OperationReply.Success(new {})),_=>{});}
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException) {duplicateRejected=true;}
            check(duplicateRejected,"Second service cannot impersonate an existing MCP instance");
        }
        var closed=await Invoke(["--agent-connect"]);
        check(closed.Exit!=0&&closed.Json.GetProperty("isError").GetBoolean(),"Closed software/switch returns a failed connection, never false success");
    }
}
