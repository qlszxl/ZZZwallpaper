using System.Diagnostics;

namespace LumaWall.ControlCenter;

// StreamWriter must not be closed/disposed while WriteLineAsync is still using
// its buffer. On a blocked pipe, terminate the reader first to release the write.
internal static class ChildProcessShutdown
{
    internal static async Task StopAsync(Process process,Task sending,CancellationTokenSource cancellation,
        Task? reading=null,Action<Exception>? report=null,int graceMilliseconds=500)
    {
        try {
            if(!await Finished(sending,graceMilliseconds).ConfigureAwait(false)) {
                cancellation.Cancel();Kill(process);
            }
            if(!await Finished(sending,2000).ConfigureAwait(false)) {
                report?.Invoke(new TimeoutException("子进程发送任务尚未结束，延后释放管道。"));
                // A single suspended await, never a polling loop or disposal race.
                await sending.ConfigureAwait(false);
            }
            try {process.StandardInput.Close();}catch(Exception error) when(error is IOException or InvalidOperationException or ObjectDisposedException) { }
            if(!await Finished(process.WaitForExitAsync(),1000).ConfigureAwait(false)) {
                cancellation.Cancel();Kill(process);
                await Finished(process.WaitForExitAsync(),2000).ConfigureAwait(false);
            }
            if(reading!=null)await Finished(reading,2000).ConfigureAwait(false);
        } catch(Exception error) {report?.Invoke(error);Kill(process);}
        finally {
            // Do not let a fault/cancellation route reintroduce the same race.
            if(!sending.IsCompleted) {try {await sending.ConfigureAwait(false);}catch { }}
            process.Dispose();cancellation.Dispose();
        }
    }
    private static async Task<bool> Finished(Task task,int milliseconds)
    {
        try {await task.WaitAsync(TimeSpan.FromMilliseconds(milliseconds)).ConfigureAwait(false);return true;}
        catch(TimeoutException) {return task.IsCompleted;}
        catch {return true;}
    }
    private static void Kill(Process process)
    {
        try {if(!process.HasExited)process.Kill(entireProcessTree:true);}
        catch(Exception error) when(error is InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }
}
