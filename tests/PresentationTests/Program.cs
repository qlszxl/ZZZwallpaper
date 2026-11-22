using LumaWall.ControlCenter;
using LumaWall.Contracts;

var checks=0;
void Check(bool condition,string name)
{
    if(!condition)throw new Exception(name);
    checks++;
}
void Throws<T>(Action action,string name) where T:Exception
{
    try {action();}catch(T) {checks++;return;}
    throw new Exception(name);
}
EffectLayerPresentationChecks.Run(Check);
EffectCardReorderChecks.Run(Check);

// Continuous drag must keep previewing while persistence waits for the user to pause.
var clock=new ManualClock();using var session=new EffectEditSession();
int previews=0,saves=0;
session.PreviewRequested+=()=> {previews++;if(session.Pending is { } pending)session.MarkPreviewed(pending);};
session.SaveRequested+=()=>saves++;
session.Initialize(clock.Create);
session.ChangeTarget("DISPLAY1|asset");
session.AdoptOriginal(VideoEffects.Original);
session.Queue("DISPLAY1","asset",FillMode.Cover,VideoEffects.Original with {Brightness=.1},true);
clock.Advance(60);
session.Queue("DISPLAY1","asset",FillMode.Cover,VideoEffects.Original with {Brightness=.2},true);
clock.Advance(20);
Check(previews==1&&saves==0,"drag keeps the original 80 ms preview deadline");
Check(session.Pending?.Effects.Brightness==.2,"preview uses latest drag value");
clock.Advance(20);
session.Queue("DISPLAY1","asset",FillMode.Cover,VideoEffects.Original with {Brightness=.3},true);
clock.Advance(80);
Check(previews==2&&saves==0,"continued drag schedules a second live preview");
clock.Advance(319);
Check(saves==0,"save waits for the full idle interval");
clock.Advance(1);
Check(saves==1,"one save occurs after 400 ms of idle time");
Check(session.LatestFor("DISPLAY1","asset")?.Effects.Brightness==.3,"wallpaper apply can replay latest draft");
Check(session.ConsumeUiChange()&&!session.ConsumeUiChange(),"UI refresh is coalesced");

// A failing old background write must not replace a newer edit or another target.
var writing=session.TakePending()!;
Check(session.Pending==null,"a submitted write leaves the pending queue");
session.RetryIfCurrent(writing);
Check(session.Pending==writing,"current failed write remains retryable");
session.TakePending();
session.Queue("DISPLAY1","asset",FillMode.Cover,VideoEffects.Original with {Brightness=.4},true);
var newer=session.Pending;
session.RetryIfCurrent(writing);
Check(session.Pending==newer,"failed old write cannot replace a new pending edit");
session.TakePending();session.ChangeTarget("DISPLAY2|screen");
session.RetryIfCurrent(writing);
Check(session.Pending==null,"failed old write cannot revive a draft after target switch");
session.Queue("DISPLAY2",null,FillMode.Contain,VideoEffects.Original with {Contrast=1.3},true);
session.ClearRetained("DISPLAY1");
Check(session.LatestFor("DISPLAY1","asset")==null&&session.LatestFor("DISPLAY2",null)!=null,"screen invalidation preserves unrelated drafts");
session.ClearRetained();
Check(session.LatestFor("DISPLAY2",null)==null,"history recovery clears retained drafts");
session.StopSchedule();clock.Advance(1000);
Check(saves==1,"flush stops the deferred save callback");

// A slow write must not force the UI to wait or merge two released gestures.
using(var queued=new EffectEditSession()) {
    queued.ChangeTarget("DISPLAY1|asset");queued.AdoptOriginal(VideoEffects.Original);
    queued.Queue("DISPLAY1","asset",FillMode.Cover,VideoEffects.Original with {Brightness=.1},false,10);
    queued.QueuePendingSave();
    queued.Queue("DISPLAY1","asset",FillMode.Cover,VideoEffects.Original with {Brightness=.2},false,10);
    queued.QueuePendingSave();
    queued.Queue("DISPLAY1","asset",FillMode.Cover,VideoEffects.Original with {Brightness=.3},false,11);
    queued.QueuePendingSave();
    var first=queued.TakeQueuedSave()!;
    Check(first.Gesture==10&&first.Effects.Brightness==.2,"waiting saves coalesce the same drag to its latest value");
    var second=queued.TakeQueuedSave()!;
    Check(second.Gesture==11&&second.Effects.Brightness==.3&&!queued.HasQueuedSaves,"separate released gestures remain ordered behind a slow write");
    queued.RestoreQueuedSave(second);
    Check(queued.TakeQueuedSave()==second,"a rejected submission retains its snapshot for retry");
    queued.Queue("DISPLAY1","asset",FillMode.Cover,VideoEffects.Original with {Brightness=.4},false,12);
    queued.RestoreQueuedSave(second);
    Check(queued.Pending?.Effects.Brightness==.4&&queued.TakeQueuedSave()==second,"retrying an older submitted gesture does not overwrite the active drag");
    queued.QueuePendingSave();queued.RestoreQueuedSave(first);
    Check(queued.TakeQueuedSave()==first&&queued.TakeQueuedSave()?.Gesture==12,"a failed old gesture stays ahead of later queued deltas despite a newer editor revision");
}

var sharing=new SharingState {GeneratedCode="old",GeneratedQr=[1,2,3]};
var firstExport=sharing.BeginExport();
Check(sharing.GeneratedCode==null&&sharing.GeneratedQr==null,"starting export invalidates previous output");
var nextExport=sharing.BeginExport();
Check(firstExport!=nextExport,"late export completion is distinguishable");
var firstImport=sharing.BeginImport();
var nextImport=sharing.BeginImport();
Check(firstImport!=nextImport&&sharing.ExportEpoch==nextExport,"text and QR import invalidation share an independent epoch");

var imports=new LibraryImportLifetime();
var active=imports.Begin();
Throws<InvalidOperationException>(()=>imports.Begin(),"overlapping imports must be rejected");
using(var unrelated=new CancellationTokenSource())imports.Complete(unrelated);
Check(imports.Active==active,"completion from another operation cannot release the current import");
imports.Complete(active);
Check(imports.Active==null,"completed import releases the slot");
active=imports.Begin();imports.Shutdown();
Check(active.IsCancellationRequested,"shutdown cancels the active import");
Throws<OperationCanceledException>(()=>imports.Check(CancellationToken.None),"shutdown rejects continuation even with an external token");
Throws<InvalidOperationException>(()=>imports.Begin(),"shutdown prevents new imports");
imports.Complete(active);
Console.WriteLine($"Presentation behavior checks passed: {checks}");

sealed class ManualClock
{
    private long now;
    private readonly List<Timer> timers=[];
    public IEditTimer Create(TimeSpan interval,Action callback)
    {
        var timer=new Timer(this,(long)interval.TotalMilliseconds,callback);timers.Add(timer);return timer;
    }
    public void Advance(int milliseconds)
    {
        long end=now+milliseconds;
        while(timers.Where(timer=>timer.IsRunning&&timer.Due<=end).OrderBy(timer=>timer.Due).FirstOrDefault() is { } timer) {
            now=timer.Due;timer.Fire();
        }
        now=end;
    }
    private sealed class Timer(ManualClock clock,long delay,Action callback):IEditTimer
    {
        public bool IsRunning {get;private set;}
        public long Due {get;private set;}
        public void Start() {Due=clock.now+delay;IsRunning=true;}
        public void Stop()=>IsRunning=false;
        public void Fire() {Stop();callback();}
        public void Dispose()=>Stop();
    }
}
