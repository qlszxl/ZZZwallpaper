namespace LumaWall.ControlCenter;

// A slow or hung child must never make input handling wait on a pipe. Only this
// worker serializes configurations and writes stdin; commands retain their order.
internal sealed class PlaybackCommandWriter
{
    private sealed record Command(string? Key,Func<string> Serialize);
    private readonly object gate=new();
    private readonly List<Command> pending=[];
    private readonly SemaphoreSlim available=new(0);
    private readonly Func<string,Task> write;
    private readonly Action<Exception> failed;
    private readonly int capacity;
    private bool closing;
    internal Task Completion {get;}
    internal int PendingCount {get {lock(gate)return pending.Count;}}

    internal PlaybackCommandWriter(Func<string,Task> write,Action<Exception> failed,int capacity=64)
    {
        if(capacity<1)throw new ArgumentOutOfRangeException(nameof(capacity));
        this.write=write;this.failed=failed;this.capacity=capacity;
        Completion=Task.Run(Drain);
    }

    internal bool TryWrite(Func<string> serialize,string? coalesceKey=null)
    {
        lock(gate) {
            if(closing)return false;
            // Replace only adjacent state updates. A pause/show/restart between
            // two configurations is a boundary, never moved past newer state.
            if(coalesceKey!=null&&pending.Count>0&&pending[^1].Key==coalesceKey) {
                pending[^1]=new(coalesceKey,serialize);return true;
            }
            if(pending.Count>=capacity)return false;
            pending.Add(new(coalesceKey,serialize));available.Release();return true;
        }
    }

    internal void Complete(string? finalCommand=null)
    {
        lock(gate) {
            if(closing)return;
            closing=true;pending.Clear();
            if(finalCommand!=null)pending.Add(new(null,()=>finalCommand));
            available.Release();
        }
    }

    private async Task Drain()
    {
        try {
            while(true) {
                await available.WaitAsync().ConfigureAwait(false);
                Command? command;
                lock(gate) {
                    if(pending.Count==0) {if(closing)return;continue;}
                    command=pending[0];pending.RemoveAt(0);
                }
                await write(command.Serialize()).ConfigureAwait(false);
                lock(gate)if(closing&&pending.Count==0)return;
            }
        }
        catch(Exception error) {Complete();failed(error);}
    }
}
