using LumaWall.Contracts;
using Screen=System.Windows.Forms.Screen;

namespace LumaWall.ControlCenter;

public sealed class DesktopPreviewSession
{
    public required string ScreenId {get;init;}
    public required MediaAsset Asset {get;init;}
    public required NativePlayback Player {get;init;}
    public bool Ready {get;internal set;}
    public PauseState Pause {get;}=new();
    internal CancellationTokenSource Cancellation {get;}=new();
}

public sealed partial class WallpaperController
{
    private sealed record PreviewPreparation(string ScreenId,string AssetId,CancellationTokenSource Cancellation);
    private PreviewPreparation? preparingPreview;
    public DesktopPreviewSession? Preview {get;private set;}
    // Preview is an ephemeral desktop layer. The committed player and its binding
    // stay intact underneath it, so cancellation never requires reopening a video.
    public async Task StartPreview(MediaAsset asset,Screen screen,FillMode fill,VideoEffects effects,bool compareOriginal)
    {
        ObjectDisposedException.ThrowIf(disposed,this);
        CheckSourceFileAvailable(asset.Path);
        if(pending.ContainsKey(screen.DeviceName))throw new InvalidOperationException(UiLanguage.Text("请等待当前屏幕应用完成后再开始预览。"));
        if(!File.Exists(asset.Path))throw new IOException(UiLanguage.Text("来源失效：请重新关联原视频。"));
        effects.Validate();EndPreview();
        using var preparationCancellation=new CancellationTokenSource();
        var preparation=new PreviewPreparation(screen.DeviceName,asset.Id,preparationCancellation);preparingPreview=preparation;
        try {
            preparationCancellation.Token.ThrowIfCancellationRequested();
            if(disposed||preparingPreview!=preparation||pending.ContainsKey(screen.DeviceName))throw new OperationCanceledException(preparationCancellation.Token);
            var player=new NativePlayback(asset.Path,screen.Bounds,fill,dispatch,effects:ProjectedEffects(screen.DeviceName,asset.Id,effects),compareOriginal:compareOriginal,
                behindWindow:running.GetValueOrDefault(screen.DeviceName)?.Player.OutputWindow??0,fpsLimit:FrameRateLimit,decoderMode:DecoderPreference);
            var session=new DesktopPreviewSession {ScreenId=screen.DeviceName,Asset=asset,Player=player};
            var token=session.Cancellation.Token;Preview=session;preparingPreview=null;
            player.Changed+=()=> {if(Preview==session&&!disposed)Changed?.Invoke();};
            player.SetPosition(RuntimeSettingsReader.Position(store,screen.DeviceName,asset.Id));
            session.Pause.Set(environmentReasons,true);
            session.Pause.Set(PauseReason.User,running.GetValueOrDefault(screen.DeviceName)?.Pause.Reasons.HasFlag(PauseReason.User)==true);
            player.Failed+=error=> {
                if(Preview!=session||!session.Ready)return;
                EndPreview();Error?.Invoke(UiLanguage.Text("桌面预览已结束，原壁纸已恢复：")+error);
            };
            Changed?.Invoke();
            try {
                await player.Prepare(token);token.ThrowIfCancellationRequested();
                await player.Show(token);token.ThrowIfCancellationRequested();
                if(Preview!=session||disposed)throw new OperationCanceledException();
                session.Ready=true;player.Pause(session.Pause.IsPaused);Changed?.Invoke();
            } catch(OperationCanceledException) {throw;}
            catch(Exception) when(token.IsCancellationRequested) {throw new OperationCanceledException(token);}
            catch {if(Preview==session)EndPreview();throw;}
        } finally {
            if(preparingPreview==preparation) {preparingPreview=null;Changed?.Invoke();}
        }
    }
    public void UpdatePreview(FillMode fill,VideoEffects effects,bool compareOriginal)
    {
        if(Preview is {Ready:true} session)session.Player.PreviewEffects(fill,ProjectedEffects(session.ScreenId,session.Asset.Id,effects),compareOriginal);
    }
    public void EndPreview(string? screen=null)
    {
        bool canceledPreparation=false;
        if(preparingPreview is { } preparation&&(screen==null||preparation.ScreenId==screen)) {
            preparingPreview=null;preparation.Cancellation.Cancel();canceledPreparation=true;
        }
        var session=Preview;
        if(session==null||(screen!=null&&session.ScreenId!=screen)) {if(canceledPreparation)Changed?.Invoke();return;}
        Preview=null;session.Cancellation.Cancel();session.Player.Dispose();session.Cancellation.Dispose();Changed?.Invoke();
    }
    private long OutputBeforeApply(string screen)=>Preview is {Ready:true} session&&session.ScreenId==screen
        ?session.Player.OutputWindow:running.GetValueOrDefault(screen)?.Player.OutputWindow??0;
}
