using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;

namespace LumaWall.Automation;

public sealed record OperationRequest(string Name,JsonElement Arguments);
public sealed record OperationReply(bool IsError,JsonElement Data)
{
    public static OperationReply Success(object data)=>new(false,ToolCatalog.Json(data));
    public static OperationReply Error(string message,string errorCode="operation_failed")=>OperationErrors.Create(message,errorCode);
}
public static class LocalTransport
{
    public static int RequestSeconds(string name)=>name is "diagnostics_benchmark_decode" or "media_preview_poster" or "media_set_poster" or "media_reset_poster" or "media_refresh" or "media_relink" or "media_get_info" or "media_import"?180:35;
    // Two independent custom particle images can each occupy up to 1.4 MB.
    public const int MaxRequestBytes=4*1024*1024,MaxReplyBytes=4*1024*1024;
    public static string PipeName=>"ZZZwallpaper.Mcp.v1."+(WindowsIdentity.GetCurrent().User?.Value??throw new InvalidOperationException("No Windows identity"))+"."+Process.GetCurrentProcess().SessionId;
    public static async Task Write<T>(Stream stream,T value,int maximum,CancellationToken token)
    {
        byte[] bytes=JsonSerializer.SerializeToUtf8Bytes(value);
        if(bytes.Length>maximum)throw new IOException("操作消息过大");
        byte[] length=new byte[4];BinaryPrimitives.WriteInt32LittleEndian(length,bytes.Length);
        await stream.WriteAsync(length,token);await stream.WriteAsync(bytes,token);await stream.FlushAsync(token);
    }
    public static async Task<T> Read<T>(Stream stream,int maximum,CancellationToken token)
    {
        byte[] header=new byte[4];await stream.ReadExactlyAsync(header,token);int length=BinaryPrimitives.ReadInt32LittleEndian(header);
        if(length<=0||length>maximum)throw new IOException("操作消息长度无效");
        byte[] bytes=new byte[length];await stream.ReadExactlyAsync(bytes,token);
        return JsonSerializer.Deserialize<T>(bytes,new JsonSerializerOptions {MaxDepth=16,UnmappedMemberHandling=System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow})??throw new IOException("操作消息为空");
    }
    public static async Task<OperationReply> Call(OperationRequest request,CancellationToken token,string? pipeName=null)
    {
        ToolCatalog.Validate(request.Name,request.Arguments);
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(RequestSeconds(request.Name)+5));
        using var pipe=new NamedPipeClientStream(".",pipeName??PipeName,PipeDirection.InOut,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
        try {
            await pipe.ConnectAsync(1500,timeout.Token);
            await Write(pipe,request,MaxRequestBytes,timeout.Token);
            return await Read<OperationReply>(pipe,MaxReplyBytes,timeout.Token);
        } catch(TimeoutException) {return OperationErrors.Create("请先启动 ZZZwallpaper，并在设置 → 通用中开启「允许 AI 操作软件（MCP）」。","connection_unavailable",recovery:"reconnect");}
        catch(IOException) {return OperationErrors.Create("MCP 连接已关闭；已完成的操作不会因断线自动撤销，请重读状态。","connection_closed",recovery:"query_operation");}
        catch(UnauthorizedAccessException) {return OperationErrors.Create("MCP 只允许同一 Windows 用户连接，请以相同用户身份运行软件和 AI 客户端。","permission_denied",recovery:"reconnect");}
        catch(OperationCanceledException) when(!token.IsCancellationRequested) {return OperationErrors.Create("操作超时，请重新读取状态后再决定是否重试。","timeout",recovery:"query_operation");}
    }
}

// No TCP listener; Windows restricts the pipe to the current user. One bounded request per connection.
public sealed class LocalOperationServer : IDisposable
{
    private readonly CancellationTokenSource lifetime=new();
    private readonly Func<OperationRequest,CancellationToken,Task<OperationReply>> execute;
    private readonly Action<Exception> report;
    private readonly NamedPipeServerStream initial;
    private readonly SemaphoreSlim clients=new(4);
    private readonly Task loop;
    private readonly string pipeName;
    private NamedPipeServerStream CreatePipe(bool first=false)=>new(pipeName,PipeDirection.InOut,4,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly|(first?PipeOptions.FirstPipeInstance:PipeOptions.None));
    public LocalOperationServer(Func<OperationRequest,CancellationToken,Task<OperationReply>> execute,Action<Exception> report,string? pipeName=null)
    {
        this.execute=execute;this.report=report;this.pipeName=pipeName??LocalTransport.PipeName;initial=CreatePipe(true);loop=Listen();
    }
    private async Task Listen()
    {
        NamedPipeServerStream? waiting=initial;
        try {
            while(!lifetime.IsCancellationRequested) {
                await clients.WaitAsync(lifetime.Token);
                waiting??=CreatePipe();await waiting.WaitForConnectionAsync(lifetime.Token);
                var connected=waiting;waiting=null;_=Handle(connected);
            }
        } catch(Exception) when(lifetime.IsCancellationRequested) { }
        catch(Exception error) {report(error);lifetime.Cancel();}
        finally {waiting?.Dispose();}
    }
    private async Task Handle(NamedPipeServerStream pipe)
    {
        using(pipe)using(var requestLifetime=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token)) {
            requestLifetime.CancelAfter(TimeSpan.FromSeconds(35));
            try {
                var request=await LocalTransport.Read<OperationRequest>(pipe,LocalTransport.MaxRequestBytes,requestLifetime.Token);
                requestLifetime.CancelAfter(TimeSpan.FromSeconds(LocalTransport.RequestSeconds(request.Name)));
                _=CancelOnDisconnect(pipe,requestLifetime);
                OperationReply reply;
                try {ToolCatalog.Validate(request.Name,request.Arguments);requestLifetime.Token.ThrowIfCancellationRequested();reply=await execute(request,requestLifetime.Token);}
                catch(Exception error) when(error is not OperationCanceledException) {reply=OperationErrors.FromException(error);}
                await LocalTransport.Write(pipe,reply,LocalTransport.MaxReplyBytes,requestLifetime.Token);
            } catch(Exception error) when(error is IOException or OperationCanceledException or JsonException or ObjectDisposedException) { }
            catch(Exception error) {report(error);}
            finally {clients.Release();}
        }
    }
    private static async Task CancelOnDisconnect(Stream pipe,CancellationTokenSource requestLifetime)
    {
        try {
            // The one-request protocol expects no more client bytes. EOF means the MCP call was cancelled.
            await pipe.ReadAsync(new byte[1],requestLifetime.Token);
            requestLifetime.Cancel();
        } catch(IOException) {try {requestLifetime.Cancel();}catch(ObjectDisposedException) { }}
        catch(OperationCanceledException) { }
        catch(ObjectDisposedException) { }
    }
    public void Dispose() {lifetime.Cancel();initial.Dispose();}
}
