using Microsoft.UI.Dispatching;

namespace LumaWall.ControlCenter;

internal sealed class DispatcherEditTimer : IEditTimer
{
    private readonly DispatcherQueueTimer timer;
    private readonly Action callback;
    public DispatcherEditTimer(DispatcherQueue queue,TimeSpan interval,Action callback)
    {
        this.callback=callback;timer=queue.CreateTimer();timer.Interval=interval;timer.IsRepeating=false;
        timer.Tick+=Tick;
    }
    private void Tick(DispatcherQueueTimer sender,object args)=>callback();
    public bool IsRunning=>timer.IsRunning;
    public void Start()=>timer.Start();
    public void Stop()=>timer.Stop();
    public void Dispose() {timer.Stop();timer.Tick-=Tick;}
}
