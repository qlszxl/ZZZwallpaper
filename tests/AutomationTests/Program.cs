using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text.Json;
using LumaWall.Automation;
using LumaWall.Contracts;
using ModelContextProtocol.Client;

if(args is ["--seed-audit",var dataRoot,var fixtureRoot]) {
    using var fixtureStore=new LumaWall.Storage.LibraryStore(Path.GetFullPath(dataRoot));
    fixtureStore.SetSetting(ToolCatalog.EnabledSetting,"true");
    fixtureStore.SetSetting("automation.mediaRoots",JsonSerializer.Serialize(new[]{Path.GetFullPath(fixtureRoot)}));
    fixtureStore.SetSetting("appearance.v1",new AppearanceOptions {Motion=false,Pattern=false}.ToJson());
    fixtureStore.SetSetting("fullscreenPause","false");
    Console.WriteLine("Seeded isolated test profile only: "+fixtureStore.Root);return;
}
if(args.Length==2&&args[0]=="--seed") {
    using var store=new LumaWall.Storage.LibraryStore(Path.GetFullPath(args[1]));
    store.SetSetting(ToolCatalog.EnabledSetting,"true");
    string video=Path.GetFullPath("artifacts/samples/av1.mp4");
    store.Upsert(new("mcp-smoke",video,"MCP synthetic test",new FileInfo(video).Length,0,0,0,"av1","SDR",""));
    Console.WriteLine("Seeded isolated MCP fixture: "+store.Root);return;
}
if(args.Length==2&&args[0]=="--live") {await LiveChecks.Run(args[1]);return;}

int checks=0;
void Check(bool condition,string name) {if(!condition)throw new Exception("FAIL: "+name);checks++;Console.WriteLine("PASS: "+name);}
void Reject(Action action,string name) {try {action();}catch(ArgumentException) {Check(true,name);return;}throw new Exception("FAIL: accepted "+name);}
JsonElement J(string json)=>JsonDocument.Parse(json).RootElement.Clone();
if(args is ["--widget-locks"]) {WidgetLockChecks.Run(Check,Reject);Console.WriteLine($"Independent widget lock automation checks passed: {checks}");return;}

if(args is ["--schema-first"]) {
    // Run in a fresh process: a cyclic initializer can silently capture an
    // undefined JsonElement when EffectPatch is touched before ToolCatalog.
    Check(EffectPatch.ParameterSchema.ValueKind==JsonValueKind.Object,"Effect schema can initialize before tool catalog");
    var advertised=ToolCatalog.All.Single(t=>t.Name=="effect_set").InputSchema.GetProperty("properties").GetProperty("parameters");
    Check(advertised.GetProperty("properties").GetProperty("Saturation").GetProperty("maximum").GetDouble()==2,"Schema-first startup advertises complete parameter bounds");
    Check(!string.IsNullOrWhiteSpace(AgentGuidance.Instructions),"Agent guide survives schema-first startup");
    Console.WriteLine($"Schema-first checks passed: {checks}");return;
}

Check(!ToolCatalog.IsEnabled(null)&&!ToolCatalog.IsEnabled("")&&!ToolCatalog.IsEnabled("false")&&!ToolCatalog.IsEnabled("1"),"MCP defaults to disabled; only explicit true enables");
Check(ToolCatalog.IsEnabled("true"),"Explicit persisted opt-in");
Check(ToolCatalog.All.Count>=29&&ToolCatalog.All.Select(t=>t.Name).Distinct().Count()==ToolCatalog.All.Count,"Unique operation catalog");
ExtendedChecks.Run(Check,Reject);
InputChecks.Run(Check);
ContractChecks.Run(Check,Reject);
await Refinement78Checks.Run(Check,Reject);
Refinement79Checks.Run(Check,Reject);
Refinement80Checks.Run(Check,Reject);
Check(typeof(EffectPatch).Assembly.GetManifestResourceNames().Length>1&&!string.IsNullOrWhiteSpace(AgentGuidance.Instructions),"Tool catalog and Agent guide coexist with multiple embedded resources");
var effectArguments=ToolCatalog.All.Single(t=>t.Name=="effect_set").InputSchema.GetProperty("properties").GetProperty("parameters");
Check(EffectPatch.Groups.Values.SelectMany(fields=>fields).Distinct().All(field=>effectArguments.GetProperty("properties").TryGetProperty(field,out _)),"Discovered effect_set schema contains every editable parameter");
Reject(()=>ToolCatalog.Validate("execute_code",J("{\"code\":\"anything\"}")),"No execute-code tool");
Reject(()=>ToolCatalog.Validate("write_file",J("{\"path\":\"main.cs\"}")),"No file writer");
Reject(()=>ToolCatalog.Validate("app_get_state",J("{\"command\":\"cmd.exe\"}")),"Unknown arguments rejected even on read tools");
Reject(()=>ToolCatalog.Validate("wallpaper_set_fps_limit",J("{\"fps\":30,\"fps\":60}")),"Duplicate arguments rejected");
Reject(()=>ToolCatalog.Validate("wallpaper_set_fps_limit",J("{\"fps\":241}")),"FPS out of range rejected");
Reject(()=>ToolCatalog.Validate("wallpaper_set_fps_limit",J("{\"fps\":30.5}")),"Fractional FPS rejected");
Reject(()=>ToolCatalog.Validate("wallpaper_set_fps_limit",J("{\"fps\":\"30\"}")),"Wrong argument type rejected");
Reject(()=>ToolCatalog.Validate("effect_set",J("{\"screenId\":\"display\",\"effect\":\"Color\",\"enabled\":true}")),"Effect mutation requires revision");
Reject(()=>ToolCatalog.Validate("effect_clear",J("{\"screenId\":\"display\",\"scope\":\"screen\"}")),"Clear-effects mutation requires revision");
Reject(()=>ToolCatalog.Validate("effect_copy_screen_to_asset",J("{\"screenId\":\"display\",\"expectedRevision\":\"test\"}")),"Copy effects requires an explicit material destination");
Reject(()=>ToolCatalog.Validate("widget_update",J("{\"widgetId\":\"id\",\"command\":\"shell\"}")),"Widget updates cannot edit command targets");
Reject(()=>ToolCatalog.Validate("widget_add",J("{\"screenId\":\"display\",\"kind\":\"Command\"}")),"Executable shortcut creation not exposed");
WidgetChecks.Run(Check,Reject);
WidgetLockChecks.Run(Check,Reject);
Reject(()=>ToolCatalog.Validate("wallpaper_apply",J("{\"screenId\":\"display\",\"assetId\":\"id\",\"path\":\"C:\\\\script.ps1\"}")),"Wallpaper input is an existing asset ID only");
Reject(()=>ToolCatalog.Validate("set_mcp_enabled",J("{\"enabled\":true}")),"Agent cannot enable its own access");
var original=VideoEffects.Original;
var color=EffectPatch.Apply(original,"Color",true,.8,J("{\"Saturation\":0.8,\"Brightness\":-0.1}"),1920,1080);
Check(color.Saturation==.8&&color.Brightness==-.1&&color.GetCollection().Added.HasFlag(EffectKind.Color)&&color.GetCollection().Color==.8,"Effect parameters, membership and strength applied together");
var off=EffectPatch.Apply(color,"Color",false,null,J("{}"),1920,1080);
Check(!off.GetCollection().ColorEnabled&&off.Saturation==.8,"Disable preserves the existing parameter values");
foreach(var group in EffectPatch.Groups.Keys) {
    var value=EffectPatch.Apply(original,group,true,null,J("{}"),1920,1080);
    Check(value.GetCollection().Added.HasFlag(Enum.Parse<EffectKind>(group)),"Allowed effect initializes: "+group);
}
Reject(()=>EffectPatch.Apply(original,"Color",true,null,J("{\"Saturation\":3}"),1920,1080),"Effect schema ranges enforced");
Reject(()=>EffectPatch.Apply(original,"Color",true,null,J("{\"GlowStrength\":1}"),1920,1080),"Cross-effect field rejected");
Reject(()=>EffectPatch.Apply(original,"Color",true,null,J("{\"Version\":22}"),1920,1080),"Configuration version cannot be patched");
Reject(()=>EffectPatch.Apply(original,"Color",true,null,J("{\"$type\":\"Process\"}"),1920,1080),"Type-injection field rejected");
Reject(()=>EffectPatch.Apply(original,"RgbSplit",true,null,J("{\"RgbOffset\":200}"),1920,1080),"Screen-specific RGB bound enforced");
var ack=new ConfigurationAcknowledgement();long first=ack.Begin();var firstWait=ack.Wait(default);long second=ack.Begin();
Check(!await firstWait,"Superseded render request returns unconfirmed");
var secondWait=ack.Wait(default);ack.Confirm(first);Check(!secondWait.IsCompleted,"Old render acknowledgement cannot confirm new request");
ack.Confirm(second);Check(await secondWait,"Matching renderer revision confirms application");
ack.Begin();var failed=ack.Wait(default);ack.Fail();Check(!await failed,"Renderer failure does not report application success");

// Explicit offline suite for restricted build agents. The default invocation
// still runs the named-pipe authorization and cancellation integration checks.
if(args is ["--contracts-only"]) {
    Console.WriteLine($"Automation contract checks passed: {checks}; named-pipe integration not requested");
    return;
}

using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(60));
string pipeName="ZZZwallpaper.Mcp.Test."+Guid.NewGuid().ToString("N");int calls=0;
var errors=new List<Exception>();
using(var server=new LocalOperationServer((request,token)=> {Interlocked.Increment(ref calls);return Task.FromResult(OperationReply.Success(new {called=request.Name}));},e=>errors.Add(e),pipeName)) {
    var result=await LocalTransport.Call(new("app_get_state",J("{}")),timeout.Token,pipeName);
    Check(!result.IsError&&calls==1,"Current-user pipe request and response"+(result.IsError?": "+result.Data.GetRawText()+" / "+string.Join(";",errors.Select(e=>e.Message)):""));
    using(var raw=new NamedPipeClientStream(".",pipeName,PipeDirection.InOut,PipeOptions.Asynchronous)) {
        await raw.ConnectAsync(timeout.Token);
        await LocalTransport.Write(raw,new OperationRequest("app_get_state",J("{\"code\":\"bad\"}")),LocalTransport.MaxRequestBytes,timeout.Token);
        var denied=await LocalTransport.Read<OperationReply>(raw,LocalTransport.MaxReplyBytes,timeout.Token);
        Check(denied.IsError&&calls==1,"Application-side validation rejects a bridge bypass");
    }
    using(var raw=new NamedPipeClientStream(".",pipeName,PipeDirection.InOut,PipeOptions.Asynchronous)) {
        await raw.ConnectAsync(timeout.Token);byte[] header=new byte[4];BinaryPrimitives.WriteInt32LittleEndian(header,int.MaxValue);await raw.WriteAsync(header,timeout.Token);await raw.FlushAsync(timeout.Token);
        byte[] one=new byte[1];int count;
        try {count=await raw.ReadAsync(one,timeout.Token);}catch(IOException) {count=0;}
        Check(count==0&&calls==1,"Oversized frames are closed before allocation or execution");
    }
}
var closed=await LocalTransport.Call(new("app_get_state",J("{}")),timeout.Token,pipeName);
Check(closed.IsError&&calls==1,"Disabling server prevents further operations");
var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var cancelled=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
using(var server=new LocalOperationServer(async(_,token)=> {
    started.SetResult();try {await Task.Delay(30000,token);}catch(OperationCanceledException) {cancelled.SetResult();throw;}return OperationReply.Success(new {});
},e=>errors.Add(e),pipeName)) {
    var call=LocalTransport.Call(new("app_get_state",J("{}")),timeout.Token,pipeName);await started.Task.WaitAsync(timeout.Token);server.Dispose();await cancelled.Task.WaitAsync(timeout.Token);
    try {await call;}catch(IOException) { }
    Check(true,"Disabling cancels in-flight operations");
}
Check(errors.Count==0,"Normal enable/disable does not emit service faults");
var disconnectStarted=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var disconnected=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
using(var server=new LocalOperationServer(async(_,token)=> {
    disconnectStarted.SetResult();try {await Task.Delay(30000,token);}catch(OperationCanceledException) {disconnected.SetResult();throw;}return OperationReply.Success(new {});
},e=>errors.Add(e),pipeName)) {
    using var caller=new CancellationTokenSource();var call=LocalTransport.Call(new("app_get_state",J("{}")),caller.Token,pipeName);
    await disconnectStarted.Task.WaitAsync(timeout.Token);caller.Cancel();try {await call;}catch(OperationCanceledException) { }
    await disconnected.Task.WaitAsync(timeout.Token);Check(true,"Client cancellation/disconnect cancels application operation");
}

if(args.Length==1) {
    foreach(string protocol in new[]{"2025-11-25","2026-07-28"}) {
        await using var client=await McpClient.CreateAsync(new StdioClientTransport(new() {Name="ZZZwallpaper integration test",Command=Path.GetFullPath(args[0]),Arguments=[]}),new() {ProtocolVersion=protocol},cancellationToken:timeout.Token);
        var tools=await client.ListToolsAsync(cancellationToken:timeout.Token);
        Check(tools.Count==ToolCatalog.All.Count&&tools.Any(t=>t.Name=="widget_get")&&tools.Single(t=>t.Name=="widget_add").JsonSchema.GetProperty("properties").GetProperty("kind").GetProperty("enum").EnumerateArray().Any(x=>x.GetString()=="Mask"),"Real stdio MCP discovery includes new component capabilities: "+protocol);
        var result=await client.CallToolAsync("app_get_state",new Dictionary<string,object?>{{"script","not allowed"}},cancellationToken:timeout.Token);
        Check(result.IsError==true,"Real MCP rejects extra script field: "+protocol);
    }
    await AgentCommandChecks.Run(args[0],Check);
}
Console.WriteLine($"Automation checks passed: {checks}");
