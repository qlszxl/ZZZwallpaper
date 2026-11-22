using LumaWall.Contracts;
using LumaWall.Diagnostics;

namespace LumaWall.ControlCenter;

internal interface IEditTimer : IDisposable
{
    bool IsRunning { get; }
    void Start();
    void Stop();
}

internal sealed record EffectUpdate(string ScreenId,string? AssetId,FillMode Fill,VideoEffects Effects,VideoEffects Before,long Revision,long Gesture=0,OperationContext? Operation=null);

// One editor owns its draft, revision, outstanding save and preview schedule.
// Persistence and runtime delivery remain separate so a failed save can be retried.
internal sealed class EffectEditSession : IDisposable
{
    private IEditTimer? previewTimer,saveTimer;
    private readonly Dictionary<(string Screen,string? Asset),EffectUpdate> retained=new();
    private readonly List<EffectUpdate> queuedSaves=[];
    private bool uiDirty;
    public string? Key { get; private set; }
    public long Revision { get; private set; }
    public long PreviewedRevision { get; private set; }=-1;
    public VideoEffects Original { get; private set; }=VideoEffects.Original;
    public VideoEffects ExtendedDraft { get; set; }=VideoEffects.Original;
    public EffectCollection Collection { get; set; }=new();
    public double LegacyIntensity { get; set; }=1;
    public EffectUpdate? Pending { get; private set; }
    public event Action? PreviewRequested;
    public event Action? SaveRequested;

    public void Initialize(Func<TimeSpan,Action,IEditTimer> createTimer)
    {
        if(previewTimer!=null)throw new InvalidOperationException("Effect editor already initialized.");
        previewTimer=createTimer(TimeSpan.FromMilliseconds(80),()=>PreviewRequested?.Invoke());
        saveTimer=createTimer(TimeSpan.FromMilliseconds(400),()=>SaveRequested?.Invoke());
    }
    public void ChangeTarget(string? key) {Key=key;Revision++;}
    public void AdvanceRevision()=>Revision++;
    public void NotifyDraftChanged() {Revision++;uiDirty=true;}
    public void AdoptOriginal(VideoEffects effects)=>Original=effects;
    public void Queue(string? screen,string? asset,FillMode fill,VideoEffects effects,bool retainForApply,long gesture=0,OperationContext? operation=null)
    {
        NotifyDraftChanged();
        if(screen==null)return;
        Pending=new(screen,asset,fill,effects,Original,Revision,gesture,operation);
        if(retainForApply)retained[(screen,asset)]=Pending;
        // Preview is throttled; persistence is debounced.
        if(previewTimer?.IsRunning==false)previewTimer.Start();
        saveTimer?.Stop();saveTimer?.Start();
    }
    public bool ConsumeUiChange() {bool changed=uiDirty;uiDirty=false;return changed;}
    public void StopPreview()=>previewTimer?.Stop();
    public void StopSchedule() {previewTimer?.Stop();saveTimer?.Stop();}
    public bool NeedsPreview=>Pending is { } update&&PreviewedRevision!=update.Revision;
    public void MarkPreviewed(EffectUpdate update)=>PreviewedRevision=update.Revision;
    public EffectUpdate? TakePending() {var update=Pending;Pending=null;return update;}
    public bool HasQueuedSaves=>queuedSaves.Count>0;
    public void QueuePendingSave()
    {
        if(TakePending() is not { } update)return;
        // A worker may outlive a pointer release. Preserve separate gestures
        // while coalescing repeated saves within the same gesture and target.
        if(queuedSaves.LastOrDefault() is { } previous&&previous.Gesture==update.Gesture&&previous.ScreenId==update.ScreenId&&previous.AssetId==update.AssetId)
            queuedSaves[^1]=update with {Before=previous.Before};
        else queuedSaves.Add(update);
    }
    public EffectUpdate? TakeQueuedSave()
    {
        if(queuedSaves.Count==0)return null;
        var update=queuedSaves[0];queuedSaves.RemoveAt(0);return update;
    }
    public void RestoreQueuedSave(EffectUpdate update)=>queuedSaves.Insert(0,update);
    public void RestorePending(EffectUpdate update)=>Pending=update;
    public void RetryIfCurrent(EffectUpdate update) {if(Pending==null&&Revision==update.Revision)Pending=update;}
    public EffectUpdate? LatestFor(string screen,string? asset)=>retained.GetValueOrDefault((screen,asset));
    public void ClearRetained()=>retained.Clear();
    public void ClearRetained(string screen)
    {
        foreach(var key in retained.Keys.Where(key=>key.Screen==screen).ToArray())retained.Remove(key);
    }
    public void Dispose() {previewTimer?.Dispose();saveTimer?.Dispose();}
}
