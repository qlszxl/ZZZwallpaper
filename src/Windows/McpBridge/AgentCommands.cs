using System.Text.Json;
using LumaWall.Automation;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace LumaWall.McpBridge;

// A one-shot MCP client for agents that cannot attach a new server to their current chat.
// No shell, script, arbitrary executable, pipe name or filesystem argument is accepted.
internal static class AgentCommands
{
    public static async Task<int> Run(string[] args,CancellationToken token)
    {
        try {
            bool connect=args.Length==1&&args[0]=="--agent-connect";
            bool list=args.Length==1&&args[0]=="--agent-tools";
            bool call=args.Length==2&&args[0]=="--agent-call";
            if(!connect&&!list&&!call)throw new ArgumentException("仅支持无参数的 stdio MCP，或 --agent-connect、--agent-tools、--agent-call <工具名>（从标准输入读取 JSON）。");
            string name=connect?"app_get_state":call?args[1]:"";
            JsonElement arguments=call?await ReadArguments(token):ToolCatalog.Json(new {});
            if(!list)ToolCatalog.Validate(name,arguments);
            string executable=Path.Combine(AppContext.BaseDirectory,"ZZZwallpaper.Mcp.exe");
            await using var client=await McpClient.CreateAsync(new StdioClientTransport(new() {
                Name="ZZZwallpaper local agent connection",Command=executable,Arguments=[]
            }),cancellationToken:token);
            if(list) {
                var catalog=await client.ListToolsAsync(cancellationToken:token);
                Write(false,new {tools=catalog.Select(tool=>new {name=tool.Name,description=tool.Description,inputSchema=tool.JsonSchema,outputSchema=tool.ProtocolTool.OutputSchema,annotations=tool.ProtocolTool.Annotations}),connectionVerified=false},client.ServerInstructions);
                return 0;
            }
            var result=await client.CallToolAsync(name,arguments.EnumerateObject().ToDictionary(p=>p.Name,p=>(object?)p.Value),cancellationToken:token);
            object data=result.StructuredContent is {} structured?structured:result.Content.OfType<TextContentBlock>().Select(t=>t.Text).ToArray();
            Write(result.IsError==true,data,connect?client.ServerInstructions:null);
            return result.IsError==true?1:0;
        } catch(Exception error) {
            Write(true,OperationErrors.FromException(error).Data);return 1;
        }
    }
    private static void Write(bool isError,object data,string? instructions=null)=>Console.WriteLine(JsonSerializer.Serialize(
        instructions==null?(object)new {isError,data}:new {isError,data,instructions}));
    private static async Task<JsonElement> ReadArguments(CancellationToken token)
    {
        using var inputTimeout=CancellationTokenSource.CreateLinkedTokenSource(token);
        inputTimeout.CancelAfter(TimeSpan.FromSeconds(45));token=inputTimeout.Token;
        if(!Console.IsInputRedirected)throw new ArgumentException("请通过标准输入传入 UTF-8 JSON 参数对象，并关闭标准输入。");
        using var input=Console.OpenStandardInput();using var buffer=new MemoryStream();
        byte[] chunk=new byte[4096];
        while(true) {
            // Console stdin on Windows may not cancel a pending native read; bound the await too.
            int count=await input.ReadAsync(chunk,token).AsTask().WaitAsync(token);if(count==0)break;
            if(buffer.Length+count>LocalTransport.MaxRequestBytes)throw new ArgumentException("JSON 参数超过 4 MiB 上限。");
            buffer.Write(chunk,0,count);
        }
        var bytes=buffer.ToArray();
        int offset=bytes.Length>=3&&bytes[0]==0xEF&&bytes[1]==0xBB&&bytes[2]==0xBF?3:0;
        using var document=JsonDocument.Parse(bytes.AsMemory(offset),new JsonDocumentOptions {MaxDepth=16});
        return document.RootElement.Clone();
    }
}
