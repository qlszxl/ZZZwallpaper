using System.Text;
using System.Text.Json;
using LumaWall.Contracts;

namespace LumaWall.WidgetHost;

// Line framing and JSON belong to the process boundary, never to individual widgets.
internal sealed class WidgetHostChannel : IDisposable
{
    private readonly object outputLock=new();
    private readonly StreamReader input;
    private StreamWriter? output;
    internal WidgetHostChannel(Stream source,Stream destination) {
        input=new StreamReader(source,new UTF8Encoding(false));
        output=new StreamWriter(destination,new UTF8Encoding(false)) {AutoFlush=true};
    }
    internal void Send(WidgetMessage message) {
        lock(outputLock) {
            try {output?.WriteLine(JsonSerializer.Serialize(message));}
            catch(IOException) { }
            catch(ObjectDisposedException) { }
        }
    }
    internal void ReportFault(string stage,Exception exception) {
        AppLog.Error("Widget."+stage,"Widget host operation failed",exception);
        Send(new() {Type="fault",FaultStage=stage,FaultType=exception.GetType().Name,FaultHResult=exception.HResult});
    }
    internal async Task ReadMessagesAsync(Action<WidgetMessage> receive) {
        while(await input.ReadLineAsync() is { } line) {
            if(line.Length>16*1024*1024)throw new InvalidDataException();
            var message=JsonSerializer.Deserialize<WidgetMessage>(line);
            if(message!=null)receive(message);
        }
    }
    public void Dispose() {
        lock(outputLock) {var writer=output;output=null;try {writer?.Dispose();}catch(IOException) { }}
        // Closing the pipe also releases a pending asynchronous reader at shutdown.
        input.Dispose();
    }
}
