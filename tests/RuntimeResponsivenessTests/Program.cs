using System.Collections.Concurrent;
using System.Diagnostics;
using LumaWall.ControlCenter;

if(args is ["--blocked-input-child"]) {Console.WriteLine("ready");await Task.Delay(Timeout.Infinite);return;}

int checks=0;
void Check(bool condition,string text) {if(!condition)throw new Exception(text);checks++;Console.WriteLine("PASS: "+text);}
TaskCompletionSource Signal()=>new(TaskCreationOptions.RunContinuationsAsynchronously);

var started=Signal();var release=Signal();var marker=Signal();
var received=new ConcurrentQueue<string>();var failures=new ConcurrentQueue<Exception>();
var writer=new PlaybackCommandWriter(async command=> {
    received.Enqueue(command);
    if(command=="hold") {started.TrySetResult();await release.Task;}
    if(command=="marker")marker.TrySetResult();
},failures.Enqueue);
writer.TryWrite(()=>"hold");await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
int serialized=0;var watch=Stopwatch.StartNew();
for(int i=0;i<10000;i++) {int value=i;if(!writer.TryWrite(()=>{Interlocked.Increment(ref serialized);return "configure "+value;},"configure"))throw new Exception("Coalesced input was rejected");}
watch.Stop();
Check(serialized==0&&!release.Task.IsCompleted,"A blocked host does not serialize queued effects on the caller or block new input");
Check(writer.PendingCount==1,"Ten thousand rapid effects retain one pending snapshot");
Check(writer.TryWrite(()=>"pause")&&writer.TryWrite(()=>"configure 10000","configure"),"Control commands form an ordering boundary between configurations");
writer.TryWrite(()=>"sound-volume 10","sound-volume");writer.TryWrite(()=>"sound-volume 80","sound-volume");
writer.TryWrite(()=>"marker");release.TrySetResult();await marker.Task.WaitAsync(TimeSpan.FromSeconds(5));
Check(received.SequenceEqual(new[]{"hold","configure 9999","pause","configure 10000","sound-volume 80","marker"}),"Newest effects/volume arrive in order without moving past pause");
Check(serialized==1,"Superseded effect snapshots never perform expensive serialization");
writer.Complete();await writer.Completion.WaitAsync(TimeSpan.FromSeconds(5));
Check(failures.IsEmpty&&!writer.TryWrite(()=>"late"),"Completion releases the worker and rejects later input");
Console.WriteLine($"Blocked-host fixture: 10,000 input updates accepted in {watch.Elapsed.TotalMilliseconds:F2} ms; deferred serializations: {serialized}.");

started=Signal();release=Signal();var stop=Signal();received.Clear();
var bounded=new PlaybackCommandWriter(async command=> {
    received.Enqueue(command);
    if(command=="hold") {started.TrySetResult();await release.Task;}
    if(command=="stop")stop.TrySetResult();
},failures.Enqueue,capacity:2);
bounded.TryWrite(()=>"hold");await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
Check(bounded.TryWrite(()=>"first")&&bounded.TryWrite(()=>"second")&&!bounded.TryWrite(()=>"overflow"),"Backpressure is bounded and explicitly rejects overflow");
bounded.Complete("stop");release.TrySetResult();await stop.Task.WaitAsync(TimeSpan.FromSeconds(5));await bounded.Completion.WaitAsync(TimeSpan.FromSeconds(5));
Check(received.SequenceEqual(new[]{"hold","stop"}),"Shutdown discards stale work and sends the final stop without waiting on the caller");

var failureSeen=Signal();int failureCount=0;
var broken=new PlaybackCommandWriter(_=>Task.FromException(new IOException("fixture broken pipe")),_=>{Interlocked.Increment(ref failureCount);failureSeen.TrySetResult();});
broken.TryWrite(()=>"configure");await failureSeen.Task.WaitAsync(TimeSpan.FromSeconds(5));await broken.Completion.WaitAsync(TimeSpan.FromSeconds(5));
Check(failureCount==1&&!broken.TryWrite(()=>"late"),"Pipe errors are reported once and close the queue");

var serializationFailure=Signal();
var invalid=new PlaybackCommandWriter(_=>Task.CompletedTask,_=>serializationFailure.TrySetResult());
invalid.TryWrite(()=>throw new InvalidDataException("fixture invalid effect"));await serializationFailure.Task.WaitAsync(TimeSpan.FromSeconds(5));await invalid.Completion;
Check(!invalid.TryWrite(()=>"late"),"Deferred serialization errors use the same failure path");
Console.WriteLine($"Runtime responsiveness checks passed: {checks}");

if(OperatingSystem.IsWindows()) {
    var own=Process.GetCurrentProcess();
    var payload=new string('x',2*1024*1024);
    for(int iteration=0;iteration<16;iteration++) {
        var start=new ProcessStartInfo(Environment.ProcessPath!) {UseShellExecute=false,CreateNoWindow=true,
            WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardInput=true,RedirectStandardOutput=true};
        start.ArgumentList.Add("--blocked-input-child");
        var child=Process.Start(start)!;
        Check(await child.StandardOutput.ReadLineAsync()=="ready","Blocked-pipe fixture ready "+iteration);
        var cancel=new CancellationTokenSource();var writing=Signal();
        var sender=new PlaybackCommandWriter(async text=> {
            writing.TrySetResult();await child.StandardInput.WriteLineAsync(text.AsMemory(),cancel.Token);
            await child.StandardInput.FlushAsync(cancel.Token);
        },_=>{});
        sender.TryWrite(()=>payload);await writing.Task;await Task.Delay(25);
        sender.Complete();
        await ChildProcessShutdown.StopAsync(child,sender.Completion,cancel,graceMilliseconds:20).WaitAsync(TimeSpan.FromSeconds(6));
        Check(sender.Completion.IsCompleted&&!sender.TryWrite(()=>"late"),"Blocked sender exits before stream/process disposal "+iteration);
    }
    await Task.Delay(300);own.Refresh();double idleStart=own.TotalProcessorTime.TotalSeconds;
    var idle=Stopwatch.StartNew();await Task.Delay(1500);own.Refresh();double cores=(own.TotalProcessorTime.TotalSeconds-idleStart)/idle.Elapsed.TotalSeconds;
    Check(cores<.25,$"Repeated shutdown leaves no busy-loop worker: {cores:F3} CPU cores while idle");
    Console.WriteLine($"Runtime shutdown regression checks passed: {checks}");
}
