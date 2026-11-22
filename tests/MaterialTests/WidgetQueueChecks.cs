using System.Text.Json;
using LumaWall.Contracts;
using LumaWall.ControlCenter;

internal static class WidgetQueueChecks
{
    internal static async Task Run(Action<bool,string> check)
    {
        var queue=new WidgetMessageQueue();
        queue.TryWrite(new() {Type="reflow",Configuration=new()});
        for(int i=0;i<10000;i++)queue.TryWrite(new() {Type="sync",Configuration=new() {Widgets=[new() {Id="latest-"+i}]}});
        check(queue.Count==1,"10,000 state updates to a stalled host retain only the latest snapshot");
        for(int i=0;i<16;i++)check(queue.TryWrite(new() {Type="inspect",RequestId=i.ToString()}),"Bounded request queue accepts ordered control messages");
        check(!queue.TryWrite(new() {Type="inspect",RequestId="overflow"})&&queue.Count==17,"Full control queue rejects overflow explicitly instead of growing");
        await using var reader=queue.ReadAllAsync().GetAsyncEnumerator();
        check(await reader.MoveNextAsync(),"Queued state is readable");
        var state=JsonSerializer.Deserialize<WidgetMessage>(reader.Current)!;
        check(state.Type=="reflow"&&state.Configuration!.Widgets.Single().Id=="latest-9999","Coalescing preserves required reflow and sends the newest configuration");
        bool ordered=true;
        for(int i=0;i<16;i++) {await reader.MoveNextAsync();ordered&=JsonSerializer.Deserialize<WidgetMessage>(reader.Current)!.RequestId==i.ToString();}
        check(ordered&&queue.Count==0,"Control messages retain their order and are not displaced by coalescing");
        var waiting=reader.MoveNextAsync().AsTask();queue.Complete();
        check(!await waiting.WaitAsync(TimeSpan.FromSeconds(2))&&!queue.TryWrite(new() {Type="sync"}),"Closing wakes the reader and rejects later writes");
        var abandoned=new WidgetMessageQueue();abandoned.TryWrite(new() {Type="sync",Configuration=new()});abandoned.Complete();
        check(abandoned.Count==0,"Stopping a host immediately releases queued configuration data");
        var concurrent=new WidgetMessageQueue();
        var consume=Task.Run(async()=> {int count=0;await foreach(var json in concurrent.ReadAllAsync()) {JsonSerializer.Deserialize<WidgetMessage>(json);count++;}return count;});
        await Task.Run(()=>Parallel.For(0,10000,i=>concurrent.TryWrite(new() {Type="sync",Id=i.ToString()})));
        check(concurrent.Count<=1,"Concurrent producers remain bounded while the host consumes messages");
        concurrent.Complete();await consume.WaitAsync(TimeSpan.FromSeconds(5));
        check(true,"Concurrent queue closes without deadlock or malformed messages");
    }
}
