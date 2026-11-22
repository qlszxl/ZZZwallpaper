using System.Text.Json;
using LumaWall.Automation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

var installationRoot = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
if (Path.GetFileName(installationRoot).Equals("Mcp", StringComparison.OrdinalIgnoreCase))
    installationRoot = Path.GetDirectoryName(installationRoot)!;
using var lifetime = new LumaWall.Lifecycle.InstallationLifetime(installationRoot);
if (lifetime.IsStopping) return;
using var stopping = new CancellationTokenSource();
lifetime.Listen(stopping.Cancel);
if(args.Length>0) {
    using var timeout=CancellationTokenSource.CreateLinkedTokenSource(stopping.Token);
    timeout.CancelAfter(TimeSpan.FromSeconds(190));
    Console.CancelKeyPress+=(_,e)=> {e.Cancel=true;timeout.Cancel();};
    Environment.ExitCode=await LumaWall.McpBridge.AgentCommands.Run(args,timeout.Token);
    return;
}
// No arguments: stdio belongs exclusively to the MCP server.
var builder=Host.CreateApplicationBuilder(new HostApplicationBuilderSettings {Args=[],DisableDefaults=true});
builder.Logging.AddConsole(options=>options.LogToStandardErrorThreshold=LogLevel.Trace);
builder.Services.AddMcpServer(options=> {
    var version=typeof(Program).Assembly.GetName().Version!;
    options.ServerInfo=new() {Name="ZZZwallpaper",Version=version.ToString(version.Revision>0?4:3)};
    options.ServerInstructions=AgentGuidance.Instructions;
})
.WithStdioServerTransport()
.WithListToolsHandler((_,_)=>ValueTask.FromResult(new ListToolsResult {Tools=ToolCatalog.All.Select(tool=>new Tool {
    Name=tool.Name,Description=tool.Description,InputSchema=tool.InputSchema,OutputSchema=tool.OutputSchema,
    Annotations=new() {ReadOnlyHint=tool.ReadOnlyHint,DestructiveHint=tool.Destructive,IdempotentHint=tool.Idempotent,OpenWorldHint=false}
}).ToList()}))
.WithCallToolHandler(async (context,token)=> {
    OperationReply reply;
    try {
        var request=context.Params??throw new ArgumentException("Missing tool call");
        reply=await LocalTransport.Call(new(request.Name,JsonSerializer.SerializeToElement(request.Arguments??new Dictionary<string,JsonElement>())),token);
    } catch(OperationCanceledException) when(token.IsCancellationRequested) {throw;}
    catch(Exception error) {reply=OperationErrors.FromException(error);}
    reply=OperationErrors.Normalize(reply);
    return new CallToolResult {IsError=reply.IsError,Content=[new TextContentBlock {Text=reply.Data.GetRawText()}],StructuredContent=reply.Data};
});
await builder.Build().RunAsync(stopping.Token);
