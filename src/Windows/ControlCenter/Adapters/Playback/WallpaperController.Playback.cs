using System.Diagnostics;
using LumaWall.Contracts;
using Screen=System.Windows.Forms.Screen;

namespace LumaWall.ControlCenter;

public sealed partial class WallpaperController
{
    private sealed class PlaybackClock
    {
        public long LastTick=Stopwatch.GetTimestamp();
        public long CachedLibraryRevision=-1;
        public PlaybackSettings CachedSettings=new();
        public List<MediaAsset>? CachedQueue;
        public double Elapsed;
        public long Generation;
        public long AutomaticRevision;
        public bool Busy;
        public bool EndPending;
        public bool? Looping;
        public long RetryAfter;
        public long LastNotice;
        public readonly Dictionary<string,long> UnavailableUntil=new();
        public readonly List<string> History=new();
    }
    private readonly Dictionary<string,PlaybackClock> playbackClocks=new();
    private PlaybackClock PlaybackClockFor(string screen)
    {
        if(!playbackClocks.TryGetValue(screen,out var state))playbackClocks[screen]=state=new();
        return state;
    }
    private PlaybackSettings PlaybackFor(string screen)
    {
        var state=PlaybackClockFor(screen);
        if(state.CachedLibraryRevision!=modules.Playback.LibraryRevision) {
            state.CachedSettings=modules.Playback.Read(screen);state.CachedQueue=null;
            state.UnavailableUntil.Clear();
            state.CachedLibraryRevision=modules.Playback.LibraryRevision;
        }
        return state.CachedSettings;
    }
    // Return a defensive copy of the mutable ID list to settings editors.
    public PlaybackSettings GetPlayback(string screen)=>PlaybackFor(screen).Validate();
    private PlaybackMode ResolvedPlaybackMode(string screen)=>PlaybackFor(screen).ResolveMode(PlaybackQueue(screen).Count);
    private bool NativeLooping(string screen)
    {
        var settings=PlaybackFor(screen);var mode=ResolvedPlaybackMode(screen);
        return mode==PlaybackMode.SingleLoop||
            (mode is PlaybackMode.Sequential or PlaybackMode.Shuffle&&settings.Trigger==PlaybackTrigger.Interval);
    }
    private List<MediaAsset> PlaybackQueue(string screen)
    {
        var settings=PlaybackFor(screen);var state=PlaybackClockFor(screen);
        if(state.CachedQueue!=null)return state.CachedQueue;
        return state.CachedQueue=modules.Library.ResolveQueue(settings);
    }
    public void ConfigurePlayback(string screen,PlaybackSettings settings)
    {
        ObjectDisposedException.ThrowIf(disposed,this);
        modules.Playback.Save(screen,settings);
        ReloadPlayback(screen);
    }
    internal void ReloadPlayback(string screen)
    {
        var state=PlaybackClockFor(screen);state.Generation++;
        // Only interrupt a carousel candidate; parameter edits must not cancel a manual Apply.
        if(state.AutomaticRevision!=0&&revisions.GetValueOrDefault(screen)==state.AutomaticRevision&&pending.TryGetValue(screen,out var request))request.Cancel();
        state.Elapsed=0;state.LastTick=Stopwatch.GetTimestamp();state.RetryAfter=0;state.UnavailableUntil.Clear();
        bool loop=NativeLooping(screen);state.Looping=loop;
        if(running.TryGetValue(screen,out var current)) {
            state.EndPending=current.Player.HasEnded&&!loop;current.Player.SetLooping(loop);
        }
        Changed?.Invoke();
    }
    private void PlaybackApplyStarting(string screen,bool automatic,long revision)
    {
        var state=PlaybackClockFor(screen);
        if(!automatic) {state.Generation++;state.RetryAfter=0;state.UnavailableUntil.Clear();}
        state.AutomaticRevision=automatic?revision:0;
        state.LastTick=Stopwatch.GetTimestamp();
    }
    private void PlaybackApplied(string screen,RunningWallpaper current,RunningWallpaper? previous)
    {
        var state=PlaybackClockFor(screen);state.Elapsed=0;state.LastTick=Stopwatch.GetTimestamp();state.RetryAfter=0;
        state.AutomaticRevision=0;state.EndPending=current.Player.HasEnded;
        state.Looping=NativeLooping(screen);
        if(state.History.Count==0&&previous!=null)state.History.Add(previous.Asset.Id);
        if(state.History.Count==0||state.History[^1]!=current.Asset.Id)state.History.Add(current.Asset.Id);
        if(state.History.Count>100)state.History.RemoveRange(0,state.History.Count-100);
        current.Player.Ended+=()=> {
            if(disposed||running.GetValueOrDefault(screen)!=current)return;
            state.EndPending=true;Changed?.Invoke();
        };
    }
    private void StopPlaybackClock(string screen)
    {
        if(playbackClocks.Remove(screen,out var state))state.Generation++;
    }
    // Called before pause state changes, so suspended time is never added to a timed rotation.
    private void AccumulatePlaybackTime()
    {
        long now=Stopwatch.GetTimestamp();
        foreach(var pair in running) {
            var state=PlaybackClockFor(pair.Key);
            if(!pair.Value.Pause.IsPaused&&!pair.Value.Player.HasEnded&&!pending.ContainsKey(pair.Key))
                state.Elapsed+=Stopwatch.GetElapsedTime(state.LastTick,now).TotalSeconds;
            state.LastTick=now;
        }
    }
    public void TickPlayback()
    {
        if(disposed)return;
        AccumulatePlaybackTime();long now=Stopwatch.GetTimestamp();
        foreach(var pair in running.ToArray()) {
            var state=PlaybackClockFor(pair.Key);
            if(pair.Value.Pause.IsPaused||state.Busy||pending.ContainsKey(pair.Key)||now<state.RetryAfter)continue;
            var settings=PlaybackFor(pair.Key);bool loop=NativeLooping(pair.Key);
            if(state.Looping!=loop) {state.Looping=loop;pair.Value.Player.SetLooping(loop);if(loop)state.EndPending=false;}
            if(ResolvedPlaybackMode(pair.Key) is not (PlaybackMode.Sequential or PlaybackMode.Shuffle))continue;
            // An intentionally empty list waits for the user to add entries; it is not a playback fault.
            if(PlaybackQueue(pair.Key).Count==0)continue;
            bool due=settings.Trigger==PlaybackTrigger.Interval||pair.Value.Asset.IsImage?state.Elapsed>=settings.IntervalSeconds:state.EndPending;
            if(due)_=StepPlayback(pair.Key,1);
        }
    }
    public bool CanStepPlayback(string screen)
    {
        if(disposed||!running.ContainsKey(screen)||pending.ContainsKey(screen)||PlaybackClockFor(screen).Busy)return false;
        return ResolvedPlaybackMode(screen) is PlaybackMode.Sequential or PlaybackMode.Shuffle&&PlaybackQueue(screen).Any(asset=>File.Exists(asset.Path));
    }
    public async Task<bool> StepPlayback(string screen,int direction,CancellationToken cancellationToken=default,Action? beforeCommit=null)
    {
        if(disposed||!running.TryGetValue(screen,out var current)||pending.ContainsKey(screen))return false;
        var state=PlaybackClockFor(screen);if(state.Busy)return false;
        state.Busy=true;long generation=state.Generation;
        try {
            var target=Screen.AllScreens.FirstOrDefault(item=>item.DeviceName==screen);if(target==null)return false;
            var settings=PlaybackFor(screen);var mode=ResolvedPlaybackMode(screen);bool multiple=mode is PlaybackMode.Sequential or PlaybackMode.Shuffle;
            var queue=multiple?PlaybackQueue(screen):[current.Asset];
            long now=Stopwatch.GetTimestamp();
            foreach(var expired in state.UnavailableUntil.Where(p=>p.Value<=now).Select(p=>p.Key).ToArray())state.UnavailableUntil.Remove(expired);
            var eligible=queue.Where(asset=>File.Exists(asset.Path)&&(!state.UnavailableUntil.TryGetValue(asset.Id,out long until)||now>=until)).ToList();
            List<MediaAsset> candidates;
            int historyIndex=-1;
            if(mode==PlaybackMode.Shuffle) {
                candidates=eligible.Where(asset=>eligible.Count==1||asset.Id!=current.Asset.Id).OrderBy(_=>Random.Shared.Next()).ToList();
                if(direction<0&&state.History.Count>=2) {
                    historyIndex=state.History.FindLastIndex(state.History.Count-2,id=>id!=current.Asset.Id&&eligible.Any(asset=>asset.Id==id));
                    if(historyIndex>=0) {
                        var previous=eligible.First(asset=>asset.Id==state.History[historyIndex]);
                        candidates.RemoveAll(asset=>asset.Id==previous.Id);candidates.Insert(0,previous);
                    }
                }
            } else {
                int currentIndex=queue.FindIndex(asset=>asset.Id==current.Asset.Id);int step=direction<0?-1:1;
                int start=currentIndex<0?(step>0?-1:0):currentIndex;
                candidates=Enumerable.Range(1,queue.Count).Select(offset=>queue[((start+step*offset)%queue.Count+queue.Count)%queue.Count])
                    .Where(asset=>eligible.Any(item=>item.Id==asset.Id)).ToList();
                if(candidates.Count>1)candidates.RemoveAll(asset=>asset.Id==current.Asset.Id);
            }
            string? failure=null;
            foreach(var asset in candidates.Take(8)) {
                if(disposed||state.Generation!=generation||running.GetValueOrDefault(screen)!=current)return false;
                try {
                    if(asset.Id==current.Asset.Id&&current.Player.HasEnded) {
                        current.Player.RestartEnded();state.EndPending=false;state.Elapsed=0;state.LastTick=Stopwatch.GetTimestamp();state.RetryAfter=0;
                        Changed?.Invoke();return true;
                    }
                    if(multiple&&asset.Id==current.Asset.Id&&settings.Trigger==PlaybackTrigger.Interval) {
                        state.Elapsed=0;state.LastTick=Stopwatch.GetTimestamp();state.RetryAfter=0;
                        Changed?.Invoke();return true;
                    }
                    var profile=store.Profile(screen,asset.Id);
                    await Apply(asset,target,profile?.Fill??current.Binding.Fill,current.Binding.UserPaused,
                        profile?.Effects??current.Binding.Effects,false,true,cancellationToken,beforeCommit);
                    if(state.Generation==generation&&historyIndex>=0&&state.History.Count>historyIndex&&state.History[historyIndex]==asset.Id)
                        state.History.RemoveRange(historyIndex+1,state.History.Count-historyIndex-1);
                    return true;
                }
                catch(LumaWall.Automation.OperationFault) {throw;}
                catch(OperationCanceledException) {return false;}
                catch(Exception error) {AppLog.Error("WallpaperController.Playback","Operation failed",error);
                    failure=error.Message;state.UnavailableUntil[asset.Id]=Stopwatch.GetTimestamp()+60*Stopwatch.Frequency;
                }
            }
            state.RetryAfter=Stopwatch.GetTimestamp()+30*Stopwatch.Frequency;
            if(state.LastNotice==0||Stopwatch.GetElapsedTime(state.LastNotice).TotalSeconds>=30) {
                state.LastNotice=Stopwatch.GetTimestamp();
                Error?.Invoke(candidates.Count==0?UiLanguage.Text("播放列表暂时没有可用视频；保留当前画面，请检查文件夹或来源路径。"):UiLanguage.Text("下一项未能播放，保留当前画面，稍后重试。")+failure);
            }
        }
        catch(LumaWall.Automation.OperationFault) {throw;}
        catch(Exception error) {AppLog.Error("WallpaperController.Playback","Operation failed",error);
            state.RetryAfter=Stopwatch.GetTimestamp()+30*Stopwatch.Frequency;
            Error?.Invoke(UiLanguage.Text("轮播设置暂不可用：")+error.Message);
        }
        finally {state.Busy=false;}
        return false;
    }
    private MediaAsset? InitialPlaybackAsset(string screen,MediaAsset? bound)
    {
        if(bound!=null&&File.Exists(bound.Path))return bound;
        var settings=PlaybackFor(screen);
        return settings.Mode is PlaybackMode.Automatic or PlaybackMode.Sequential or PlaybackMode.Shuffle
            ?PlaybackQueue(screen).FirstOrDefault(asset=>File.Exists(asset.Path)):bound;
    }
    public string DescribePlayback(string screen)
    {
        var settings=PlaybackFor(screen);
        var resolved=ResolvedPlaybackMode(screen);
        string prefix=settings.Mode==PlaybackMode.Automatic?UiLanguage.Text("自动 · "):"";
        if(resolved==PlaybackMode.SingleLoop)return prefix+UiLanguage.Text("单视频循环");
        if(resolved==PlaybackMode.SingleOnce)return UiLanguage.Text("单次播放 · 结束后保持末帧");
        string mode=prefix+(resolved==PlaybackMode.Shuffle?UiLanguage.Text("随机轮播"):UiLanguage.Text("顺序轮播"));
        int count=PlaybackQueue(screen).Count;
        string source=settings.FolderId==null?UiLanguage.Text("播放列表"):UiLanguage.Text("文件夹");
        if(settings.Trigger==PlaybackTrigger.EndOfVideo)return UiLanguage.Format($"{mode} · {source} {count} 项 · 播完切换");
        var state=playbackClocks.GetValueOrDefault(screen);
        int remaining=Math.Max(0,(int)Math.Ceiling(settings.IntervalSeconds-(state?.Elapsed??0)));
        return UiLanguage.Format($"{mode} · {source} {count} 项 · 每 {settings.IntervalSeconds} 秒切换（剩余 {remaining} 秒，暂停时不计时）");
    }
}
