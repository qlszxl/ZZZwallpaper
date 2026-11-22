using LumaWall.Contracts;
using LumaWall.DesktopIntegration;
using Screen=System.Windows.Forms.Screen;

namespace LumaWall.ControlCenter;

public sealed class RunningWallpaper
{
    public required MediaAsset Asset { get; init; }
    public required NativePlayback Player { get; init; }
    public required ScreenBinding Binding { get; set; }
    public PauseState Pause { get; }=new();
}

public sealed partial class WallpaperController : IDisposable
{
    private readonly LibraryStore store;
    private readonly ControlCenterServices modules;
    private readonly Action<Action> dispatch;
    private readonly Dictionary<string,RunningWallpaper> running=new();
    private readonly Dictionary<string,CancellationTokenSource> pending=new();
    private readonly HashSet<NativePlayback> preparingPlayers=new();
    private sealed class PendingEffectSettings
    {
        public required string AssetId {get;init;}
        public required FillMode Fill {get;set;}
        public required VideoEffects Effects {get;set;}
        public bool CompareOriginal {get;set;}
        public long ProjectionRevision {get;set;}
    }
    private readonly Dictionary<string,PendingEffectSettings> pendingEffects=new();
    private readonly Dictionary<string,long> revisions=new();
    private readonly Dictionary<string,int> recovery=new();
    private readonly Dictionary<string,DateTime> lastFailure=new();
    private PauseReason environmentReasons,observedEnvironmentReasons,pausePolicy;
    private bool disposed;
    public event Action? Changed;
    public event Action<string>? Error;
    public IReadOnlyDictionary<string,RunningWallpaper> Running => running;
    public bool FullscreenPause { get; set; }=true;
    public Func<int?>? WidgetProcessId {get;set;}
    public long CancellationEpoch {get;private set;}
    public IReadOnlyList<NativePlayback> AllPlayersSnapshot() => running.Values.Select(item=>item.Player)
        .Concat(preparingPlayers).Concat(Preview==null?Array.Empty<NativePlayback>():[Preview.Player])
        .Where(player=>!player.HasExited).Distinct().ToArray();
    public int FrameRateLimit {get;private set;}
    public DecoderMode DecoderPreference {get;private set;}
    public void SetDecoderMode(DecoderMode mode) {DecoderPreference=mode;foreach(var player in AllPlayersSnapshot())player.SetDecoderMode(mode);Changed?.Invoke();}
    public bool VideoSoundEnabled(string screen) => modules.Playback.SoundEnabled(screen);
    public void SetVideoSound(string screen,bool enabled)
    {
        modules.Playback.SetSound(screen,enabled);
        ReloadVideoSound(screen);Changed?.Invoke();
    }
    public void SetVideoSoundVolume(string screen,int volume)
    {
        modules.Playback.SetVolume(screen,volume);ReloadVideoSound(screen);Changed?.Invoke();
    }
    public void ReloadVideoSound(string screen)
    {
        if(running.GetValueOrDefault(screen) is not { } current)return;
        current.Player.SetSoundVolume(modules.Playback.Volume(screen));
        current.Player.SetSound(VideoSoundEnabled(screen));
    }
    public void SetFrameRateLimit(int cap) {FrameRateLimit=cap is >=1 and <=240?cap:0;foreach(var player in AllPlayersSnapshot())player.SetFrameRateLimit(FrameRateLimit);}
    public WallpaperController(LibraryStore store,Action<Action> dispatch,ControlCenterServices modules) {this.store=store;this.dispatch=dispatch;this.modules=modules;FrameRateLimit=modules.Settings.FpsLimit();DecoderPreference=modules.Settings.Decoder();pausePolicy=modules.Settings.PausePolicy();}

    public async Task Apply(MediaAsset asset,Screen screen,FillMode fill,bool userPaused=false,VideoEffects? effects=null,bool saveProfile=true,bool playbackTransition=false,CancellationToken cancellationToken=default,Action? beforeCommit=null)
    {
        if(disposed)return;
        CheckSourceFileAvailable(asset.Path);
        if(!File.Exists(asset.Path))throw new IOException(UiLanguage.Text("来源失效：请重新关联原视频。"));
        string key=screen.DeviceName;
        var previousAtStart=running.GetValueOrDefault(key);bool previousUserPause=previousAtStart?.Binding.UserPaused??false;
        // Cancel a preview still preparing before it can promote itself over this apply.
        if(preparingPreview?.ScreenId==key||Preview is {Ready:false} preparing&&preparing.ScreenId==key)EndPreview(key);
        if(pending.Remove(key,out var oldRequest))oldRequest.Cancel();
        var token=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);pending[key]=token;
        long revision=revisions.GetValueOrDefault(key)+1;revisions[key]=revision;
        PlaybackApplyStarting(key,playbackTransition,revision);
        NativePlayback? candidate=null;
        NativePlayback? preparingPlayer=null;
        PendingEffectSettings? settings=null;
        try
        {
            var screenProfile=store.ScreenProfile(key);
            fill=screenProfile?.Fill??fill;
            var baseline=(screenProfile?.Effects??effects??VideoEffects.Original).Validate();
            var appliedEffects=store.AssetProfile(asset.Id).Resolve(baseline);
            settings=new() {AssetId=asset.Id,Fill=fill,Effects=appliedEffects};pendingEffects[key]=settings;
            int transitionMs=previousAtStart!=null&&previousAtStart.Asset.Id!=asset.Id&&PlaybackFor(key).Transition==PlaybackTransition.CrossFade?420:0;

            token.Token.ThrowIfCancellationRequested();
            if(disposed||revisions.GetValueOrDefault(key)!=revision||pending.GetValueOrDefault(key)!=token)throw new OperationCanceledException(token.Token);
            candidate=new NativePlayback(asset.Path,screen.Bounds,fill,dispatch,effects:ProjectedEffects(key,asset.Id,appliedEffects),behindWindow:OutputBeforeApply(key),loop:NativeLooping(key),fpsLimit:FrameRateLimit,transitionMs:transitionMs,decoderMode:DecoderPreference);
            preparingPlayer=candidate;preparingPlayers.Add(candidate);
            candidate.SetAudioCaptureDevice(store.GetSetting("audioCaptureDevice:"+key)??"default");
            await candidate.Prepare(token.Token);
            token.Token.ThrowIfCancellationRequested();
            candidate.SetPosition(RuntimeSettingsReader.Position(store,key,asset.Id));
            // Parameter edits made while decoding prepares must win over the initial snapshot.
            var sentFill=settings.Fill;var sentEffects=settings.Effects;bool sentCompare=settings.CompareOriginal;long sentProjection=settings.ProjectionRevision;
            if(!candidate.ConfigureEffects(sentFill,ProjectedEffects(key,asset.Id,sentEffects),sentCompare))throw new IOException(UiLanguage.Text("画面参数发送失败，请重新应用壁纸。"));
            candidate.SetLooping(NativeLooping(key));
            await candidate.Show(token.Token);
            token.Token.ThrowIfCancellationRequested();
            if(disposed||revisions[key]!=revision)throw new OperationCanceledException();
            if(settings.Fill!=sentFill||settings.Effects!=sentEffects||settings.CompareOriginal!=sentCompare||settings.ProjectionRevision!=sentProjection)
                if(!candidate.ConfigureEffects(settings.Fill,ProjectedEffects(key,asset.Id,settings.Effects),settings.CompareOriginal))throw new IOException(UiLanguage.Text("画面参数发送失败，请重新应用壁纸。"));
            candidate.SetLooping(NativeLooping(key));
            candidate.SetPosition(RuntimeSettingsReader.Position(store,key,asset.Id));
            if(running.TryGetValue(key,out var stillPlaying)&&stillPlaying==previousAtStart&&
                (playbackTransition||stillPlaying.Binding.UserPaused!=previousUserPause))userPaused=stillPlaying.Binding.UserPaused;
            var binding=new ScreenBinding(key,asset.Id,settings.Fill,userPaused,revision) {Effects=settings.Effects};
            // Native ready follows completed rendering, promotion above the predecessor and DWM flush.
            // A persistence failure also preserves the previous player.
            beforeCommit?.Invoke();
            store.RememberAssetAudio(asset,candidate.HasAudioTrack);
            store.SaveBinding(binding,saveProfile);
            var next=new RunningWallpaper {Asset=asset,Player=candidate,Binding=binding};
            next.Pause.Set(environmentReasons,true);next.Pause.Set(PauseReason.User,userPaused);
            if(running.TryGetValue(key,out var previousPause))next.Pause.Set(PauseReason.Fullscreen,previousPause.Pause.Reasons.HasFlag(PauseReason.Fullscreen));
            candidate.Pause(next.Pause.IsPaused);
            var previous=running.GetValueOrDefault(key);running[key]=next;
            PlaybackApplied(key,next,previous);
            candidate.Failed+=error=>OnPlayerFailed(key,next,error);candidate.Changed+=()=> {SyncTaskbars();Changed?.Invoke();};
            // Prepared players stay silent until the screen binding has committed.
            previous?.Player.SetSound(false);
            candidate.SetSoundVolume(store.WallpaperVolume(key));candidate.SetSound(VideoSoundEnabled(key));
            candidate=null;EndPreview(key);previous?.Player.Dispose();SyncTaskbars();Changed?.Invoke();
        }
        finally
        {
            if(preparingPlayer!=null)preparingPlayers.Remove(preparingPlayer);
            if(settings!=null&&pendingEffects.GetValueOrDefault(key)==settings)pendingEffects.Remove(key);
            candidate?.Dispose();if(pending.GetValueOrDefault(key)==token)pending.Remove(key);token.Dispose();
        }
    }
    public bool UpdateLiveEffects(string screenId,string assetId,FillMode fill,VideoEffects effects,bool compareOriginal=false,bool persist=true)
    {
        ObjectDisposedException.ThrowIf(disposed,this);
        effects=effects.Validate();
        if(persist)store.SaveEffectDraft(screenId,assetId,new(fill,effects),store.AssetProfile(assetId).Overrides);
        RefreshLiveEffects(new EffectChange([screenId],[assetId]));
        return running.Values.Any(r=>r.Asset.Id==assetId)||pendingEffects.Values.Any(p=>p.AssetId==assetId);
    }
    public void RefreshLiveEffects(EffectChange? change=null)
    {
        SendLiveEffects(change,null);
    }

    internal async Task ActivatePresetEffects(string[] screens)
    {
        var receipts=new List<(NativePlayback Player,Task<bool> Confirmed)>();
        SendLiveEffects(new EffectChange(screens,[]),receipts);
        await Task.WhenAll(receipts.Select(r=>r.Confirmed));
        if(receipts.Any(r=>!r.Confirmed.Result&&!r.Player.HasExited&&
            (running.Values.Any(w=>w.Player==r.Player)||Preview?.Player==r.Player)))
            throw new IOException(UiLanguage.Text("桌面渲染器未确认预设画面。"));
    }

    private void SendLiveEffects(EffectChange? change,List<(NativePlayback Player,Task<bool> Confirmed)>? receipts)
    {
        bool preset=receipts!=null;
        ObjectDisposedException.ThrowIf(disposed,this);
        bool failed=false;
        foreach(var pair in pendingEffects) {
            if(change!=null&&!change.Affects(pair.Key,pair.Value.AssetId))continue;
            var profile=store.EffectiveProfile(pair.Key,pair.Value.AssetId);
            pair.Value.Fill=profile.Fill;pair.Value.Effects=profile.Effects;
        }
        foreach(var pair in running) {
            if(change!=null&&!change.Affects(pair.Key,pair.Value.Asset.Id))continue;
            var current=pair.Value;var profile=store.EffectiveProfile(pair.Key,current.Asset.Id);
            var binding=current.Binding with {Fill=profile.Fill,Effects=profile.Effects};
            if(!preset)store.SaveBinding(binding);
            current.Binding=binding;
            if(!current.Player.ConfigureEffects(profile.Fill,ProjectedEffects(pair.Key,current.Asset.Id,profile.Effects),false,preset))failed=true;
            else if(preset)receipts!.Add((current.Player,current.Player.WaitForConfiguration(CancellationToken.None)));
        }
        if(Preview is { } preview&&(change==null||change.Affects(preview.ScreenId,preview.Asset.Id))) {
            var profile=store.EffectiveProfile(preview.ScreenId,preview.Asset.Id);
            if(!preview.Player.ConfigureEffects(profile.Fill,ProjectedEffects(preview.ScreenId,preview.Asset.Id,profile.Effects),false,preset))failed=true;
            else if(preset)receipts!.Add((preview.Player,preview.Player.WaitForConfiguration(CancellationToken.None)));
        }
        SyncTaskbars();Changed?.Invoke();
        if(failed)throw new IOException(UiLanguage.Text("参数已保存，部分壁纸未确认更新，请重新应用壁纸。"));
    }
    public bool SetCompareOriginal(string screenId,string assetId,bool compareOriginal)
    {
        if(disposed)return false;
        var current=running.GetValueOrDefault(screenId);
        if(current?.Asset.Id!=assetId)current=null;
        var preparing=pendingEffects.GetValueOrDefault(screenId);
        if(preparing?.AssetId!=assetId||!pending.TryGetValue(screenId,out var request)||request.IsCancellationRequested)preparing=null;
        if(preparing!=null)preparing.CompareOriginal=compareOriginal;
        if(current!=null&&!current.Player.ConfigureEffects(current.Binding.Fill,ProjectedEffects(screenId,assetId,current.Binding.Effects),compareOriginal))
            throw new IOException(UiLanguage.Text("原画对比未送达壁纸；请重新应用壁纸。"));
        return current!=null||preparing!=null;
    }
    public async Task Restore()
    {
        var assets=store.Assets().ToDictionary(x=>x.Id);
        foreach(var binding in store.Bindings())
        {
            if(!store.Bindings().Any(b=>b.ScreenId==binding.ScreenId&&b.Revision==binding.Revision))continue;
            var screen=Screen.AllScreens.FirstOrDefault(s=>s.DeviceName==binding.ScreenId);
            if(screen==null)continue;
            var asset=InitialPlaybackAsset(binding.ScreenId,assets.GetValueOrDefault(binding.AssetId));if(asset==null)continue;
            try {await Apply(asset,screen,binding.Fill,binding.UserPaused,binding.Effects,false);}
            catch(OperationCanceledException) {}
            catch(Exception ex) {AppLog.Error("WallpaperController","Operation failed",ex);Error?.Invoke(UiLanguage.Text("恢复壁纸失败：")+ex.Message);}
        }
    }
    public void ToggleUserPause(string screen)
    {
        AccumulatePlaybackTime();
        if(!running.TryGetValue(screen,out var item)) {
            if(Preview is { } onlyPreview&&onlyPreview.ScreenId==screen) {
                onlyPreview.Pause.Set(PauseReason.User,!onlyPreview.Pause.Reasons.HasFlag(PauseReason.User));
                if(onlyPreview.Ready)onlyPreview.Player.Pause(onlyPreview.Pause.IsPaused);SyncTaskbars();Changed?.Invoke();
            }
            return;
        }
        bool pause=!item.Pause.Reasons.HasFlag(PauseReason.User);
        var binding=item.Binding with {UserPaused=pause};store.SaveBinding(binding);
        item.Binding=binding;item.Pause.Set(PauseReason.User,pause);item.Player.Pause(item.Pause.IsPaused);
        if(Preview is { } preview&&preview.ScreenId==screen) {preview.Pause.Set(PauseReason.User,pause);if(preview.Ready)preview.Player.Pause(preview.Pause.IsPaused);}
        SyncTaskbars();Changed?.Invoke();
    }
    public void SetPausePolicy(PauseReason policy)
    {
        pausePolicy=policy;ApplyEnvironmentPause();
    }
    public void SetEnvironmentPause(PauseReason reason,bool active)
    {
        observedEnvironmentReasons=active?observedEnvironmentReasons|reason:observedEnvironmentReasons&~reason;
        ApplyEnvironmentPause();
    }
    private void ApplyEnvironmentPause()
    {
        var next=observedEnvironmentReasons&pausePolicy;if(next==environmentReasons)return;
        AccumulatePlaybackTime();environmentReasons=next;
        foreach(var reason in new[]{PauseReason.SessionLocked,PauseReason.DisplayOff,PauseReason.Suspend,PauseReason.HighResourceLoad}) {
            bool active=(next&reason)!=0;
            foreach(var item in running.Values)item.Pause.Set(reason,active);
            if(Preview is { } preview)preview.Pause.Set(reason,active);
        }
        foreach(var item in running.Values)item.Player.Pause(item.Pause.IsPaused);
        if(Preview is {Ready:true} ready)ready.Player.Pause(ready.Pause.IsPaused);
        SyncTaskbars();Changed?.Invoke();
    }
    public void CheckFullscreen()
    {
        SyncTaskbars();
        AccumulatePlaybackTime();
        var full=FullscreenPause?DesktopShell.FullscreenScreens(WidgetProcessId?.Invoke()):new HashSet<string>(StringComparer.Ordinal);
        if(Preview is { } preview&&preview.Pause.Reasons.HasFlag(PauseReason.Fullscreen)!=full.Contains(preview.ScreenId)) {
            preview.Pause.Set(PauseReason.Fullscreen,full.Contains(preview.ScreenId));if(preview.Ready)preview.Player.Pause(preview.Pause.IsPaused);
        }
        foreach(var pair in running)
        {
            bool value=full.Contains(pair.Key);
            if(pair.Value.Pause.Reasons.HasFlag(PauseReason.Fullscreen)==value)continue;
            pair.Value.Pause.Set(PauseReason.Fullscreen,value);pair.Value.Player.Pause(pair.Value.Pause.IsPaused);SyncTaskbars();Changed?.Invoke();
        }
    }
    public async Task Rebind()
    {
        EndPreview();
        foreach(var request in pending.Values)request.Cancel();
        foreach(var pair in running.ToArray())
        {
            if(running.GetValueOrDefault(pair.Key)!=pair.Value)continue;
            var screen=Screen.AllScreens.FirstOrDefault(s=>s.DeviceName==pair.Key);
            if(screen==null) {StopPlaybackClock(pair.Key);pair.Value.Player.Dispose();running.Remove(pair.Key);continue;}
            try {await Apply(pair.Value.Asset,screen,pair.Value.Binding.Fill,pair.Value.Binding.UserPaused,pair.Value.Binding.Effects,false);}
            catch(OperationCanceledException) {}
            catch(Exception ex) {AppLog.Error("WallpaperController","Operation failed",ex);Error?.Invoke(UiLanguage.Text("重新绑定屏幕失败：")+ex.Message);}
        }
        var assets=store.Assets().ToDictionary(x=>x.Id);
        foreach(var binding in store.Bindings())
        {
            if(disposed||running.ContainsKey(binding.ScreenId)||pending.ContainsKey(binding.ScreenId))continue;
            if(!store.Bindings().Any(b=>b.ScreenId==binding.ScreenId&&b.Revision==binding.Revision))continue;
            var screen=Screen.AllScreens.FirstOrDefault(x=>x.DeviceName==binding.ScreenId);
            if(screen==null)continue;
            var asset=InitialPlaybackAsset(binding.ScreenId,assets.GetValueOrDefault(binding.AssetId));if(asset==null)continue;
            try {await Apply(asset,screen,binding.Fill,binding.UserPaused,binding.Effects,false);}
            catch(OperationCanceledException) {}
            catch(Exception ex) {AppLog.Error("WallpaperController","Operation failed",ex);Error?.Invoke(UiLanguage.Text("重连屏幕失败：")+ex.Message);}
        }
        SyncTaskbars();Changed?.Invoke();
    }
    private async void OnPlayerFailed(string screen,RunningWallpaper expected,string error)
    {
        if(disposed||running.GetValueOrDefault(screen)!=expected)return;
        running.Remove(screen);expected.Player.Dispose();SyncTaskbars();Changed?.Invoke();
        if(!lastFailure.TryGetValue(screen,out var time)||DateTime.UtcNow-time>TimeSpan.FromMinutes(2))recovery[screen]=0;
        lastFailure[screen]=DateTime.UtcNow;
        int attempts=recovery.GetValueOrDefault(screen)+1;recovery[screen]=attempts;
        if(attempts>3) {Error?.Invoke(UiLanguage.Text("渲染连续失败，已停止自动重试。")+error);return;}
        await Task.Delay(attempts*1000);
        if(disposed||running.ContainsKey(screen)||pending.ContainsKey(screen))return;
        if(revisions.GetValueOrDefault(screen)!=expected.Binding.Revision||!store.Bindings().Any(b=>b.ScreenId==screen&&b.Revision==expected.Binding.Revision))return;
        var target=Screen.AllScreens.FirstOrDefault(s=>s.DeviceName==screen);if(target==null)return;
        try {await Apply(expected.Asset,target,expected.Binding.Fill,expected.Binding.UserPaused,expected.Binding.Effects,false);}
        catch(OperationCanceledException) {}
        catch(Exception ex) {AppLog.Error("WallpaperController","Operation failed",ex);Error?.Invoke(UiLanguage.Text("壁纸恢复失败：")+ex.Message);}
    }
    public void RemoveAsset(string id)
    {
        if(Preview?.Asset.Id==id||preparingPreview?.AssetId==id)EndPreview();
        // Cancel candidates as well as committed sessions before forgetting a library reference.
        foreach(var pair in pendingEffects.Where(p=>p.Value.AssetId==id).ToArray())
            if(pending.TryGetValue(pair.Key,out var request))request.Cancel();
        foreach(var pair in running.Where(x=>x.Value.Asset.Id==id).ToArray()) {StopPlaybackClock(pair.Key);pair.Value.Player.Dispose();running.Remove(pair.Key);}
        store.Remove(id);SyncTaskbars();Changed?.Invoke();
    }
    public bool CanCancel(string? screen=null)=>screen==null?preparingPreview!=null||Preview!=null||running.Count>0||pending.Count>0||store.Bindings().Count>0:preparingPreview?.ScreenId==screen||Preview?.ScreenId==screen||running.ContainsKey(screen)||pending.ContainsKey(screen)||store.Bindings().Any(b=>b.ScreenId==screen);
    public void CancelWallpaper(string? screen=null,bool preserveBinding=false)
    {
        // Persist first: storage failure must leave the existing wallpaper active.
        if(!preserveBinding)store.RemoveBindings(screen);
        EndPreview(screen);
        CancellationEpoch++;
        var keys=screen==null?revisions.Keys.Concat(running.Keys).Concat(pending.Keys).Distinct().ToArray():[screen];
        foreach(string key in keys) {
            StopPlaybackClock(key);revisions[key]=revisions.GetValueOrDefault(key)+1;
            if(pending.Remove(key,out var request))request.Cancel();
            if(running.Remove(key,out var current))current.Player.Dispose();
            recovery.Remove(key);lastFailure.Remove(key);
        }
        SyncTaskbars();Changed?.Invoke();
    }
    public void Dispose()
    {
        ClearEffectSolo();taskbars.Dispose();disposed=true;foreach(var state in playbackClocks.Values)state.Generation++;playbackClocks.Clear();
        EndPreview();foreach(var request in pending.Values)request.Cancel();
        foreach(var item in running.Values)item.Player.Dispose();running.Clear();
    }
}
