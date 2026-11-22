namespace LumaWall.Automation;

// Separates a successful pipe write from a renderer acknowledgement, and detects superseded edits.
public sealed class ConfigurationAcknowledgement
{
    private readonly object gate=new();
    private long revision;
    private long confirmedRevision;
    public long RequestedRevision {get {lock(gate)return revision;}}
    public long ConfirmedRevision {get {lock(gate)return confirmedRevision;}}
    private TaskCompletionSource<bool>? pending;
    public long Begin()
    {
        lock(gate) {pending?.TrySetResult(false);pending=new(TaskCreationOptions.RunContinuationsAsynchronously);return ++revision;}
    }
    public void Confirm(long acknowledged) {lock(gate) {if(acknowledged==revision) {confirmedRevision=acknowledged;pending?.TrySetResult(true);}}}
    public void Fail() {lock(gate)pending?.TrySetResult(false);}
    public async Task<bool> Wait(CancellationToken token)
    {
        Task<bool>? task;lock(gate)task=pending?.Task;
        if(task==null)return false;
        try {return await task.WaitAsync(TimeSpan.FromSeconds(5),token);}
        catch(TimeoutException) {return false;}
    }
}
