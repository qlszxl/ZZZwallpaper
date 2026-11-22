using LumaWall.Contracts;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace LumaWall.ControlCenter;

// Shell extensions and default-browser activation may block or need STA COM.
// Keep them off the XAML dispatcher, with one worker and a bounded queue.
internal sealed class DesktopTargetLauncher : IDisposable
{
    private readonly BlockingCollection<(string Target,TaskCompletionSource Completion)> queue=new(8);
    private readonly object gate=new();
    private readonly Action<string> launch;
    private bool started,disposed;
    public DesktopTargetLauncher():this(target=> {using var process=Process.Start(new ProcessStartInfo(target) {UseShellExecute=true,Verb="open"});}) { }
    internal DesktopTargetLauncher(Action<string> launch)=>this.launch=launch;
    public Task Open(string target)
    {
        lock(gate) {
            ObjectDisposedException.ThrowIf(disposed,this);
            var completion=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if(!queue.TryAdd((target,completion)))throw new InvalidOperationException(UiLanguage.Text("正在打开，请稍后重试"));
            if(!started) {
                started=true;
                var worker=new Thread(Work) {IsBackground=true,Name="Desktop target launcher"};
                worker.SetApartmentState(ApartmentState.STA);worker.Start();
            }
            return completion.Task;
        }
    }
    private void Work()
    {
        foreach(var request in queue.GetConsumingEnumerable()) {
            lock(gate)if(disposed) {request.Completion.TrySetCanceled();continue;}
            try {
                // A reused browser or Explorer may return no Process. Successful
                // shell acceptance, rather than a new PID, is the success signal.
                launch(request.Target);
                request.Completion.TrySetResult();
            }catch(Exception error) {request.Completion.TrySetException(error);}
        }
        queue.Dispose();
    }
    public void Dispose()
    {
        lock(gate) {
            if(disposed)return;disposed=true;queue.CompleteAdding();
            if(!started)queue.Dispose();
        }
    }
}
