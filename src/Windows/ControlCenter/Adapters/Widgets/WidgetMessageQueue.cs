using System.Text.Json;
using LumaWall.Contracts;

namespace LumaWall.ControlCenter;

// A stalled host must not retain every image-bearing configuration snapshot.
// Keep the newest complete state and at most 16 small request/reply messages.
internal sealed class WidgetMessageQueue
{
    private readonly object gate=new();
    private readonly List<WidgetMessage> pending=[];
    private readonly SemaphoreSlim available=new(0);
    private bool completed;
    private static bool IsState(WidgetMessage message)=>message.Type is "sync" or "reflow";
    internal int Count {get {lock(gate)return pending.Count;}}
    internal bool TryWrite(WidgetMessage message)
    {
        lock(gate) {
            if(completed)return false;
            if(IsState(message)) {
                int index=pending.FindIndex(IsState);
                if(index>=0) {
                    // A pending reflow still needs to run with the newest state.
                    pending[index]=pending[index].Type=="reflow"?message with {Type="reflow"}:message;
                    return true;
                }
            } else if(pending.Count(m=>!IsState(m))>=16)return false;
            pending.Add(message);available.Release();return true;
        }
    }
    internal async IAsyncEnumerable<string> ReadAllAsync()
    {
        WidgetConfiguration? previous=null;
        AppearanceOptions? previousAppearance=null;
        while(true) {
            await available.WaitAsync();WidgetMessage? message;
            lock(gate) {
                if(completed)message=null;
                else {message=pending[0];pending.RemoveAt(0);}
            }
            if(message==null)yield break;
            var outgoing=message;
            if(IsState(message)&&message.Configuration is { } state) {
                if(message.Type=="sync"&&previous!=null)
                    outgoing=message with {Configuration=null,Delta=WidgetDelta.Between(previous,state),Appearance=message.Appearance==previousAppearance?null:message.Appearance};
                previous=state;previousAppearance=message.Appearance;
            }
            yield return JsonSerializer.Serialize(outgoing);
        }
    }
    internal void Complete()
    {
        lock(gate) {if(completed)return;completed=true;pending.Clear();available.Release();}
    }
}
