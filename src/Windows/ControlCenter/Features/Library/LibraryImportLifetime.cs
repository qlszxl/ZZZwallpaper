namespace LumaWall.ControlCenter;

internal sealed class LibraryImportLifetime
{
    public CancellationTokenSource? Active { get; private set; }
    public bool IsShutdown { get; private set; }
    public CancellationTokenSource Begin()
    {
        if(IsShutdown)throw new InvalidOperationException("Library imports have stopped.");
        if(Active!=null)throw new InvalidOperationException("A library import is already active.");
        return Active=new CancellationTokenSource();
    }
    public void Complete(CancellationTokenSource operation)
    {
        if(ReferenceEquals(Active,operation))Active=null;
        operation.Dispose();
    }
    public void Check(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if(IsShutdown)throw new OperationCanceledException(token);
    }
    public void Shutdown() {IsShutdown=true;Active?.Cancel();}
}
