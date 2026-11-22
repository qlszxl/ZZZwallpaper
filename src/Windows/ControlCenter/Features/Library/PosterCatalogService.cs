using System.Text.Json;
using LumaWall.Application;
using LumaWall.Contracts;

namespace LumaWall.ControlCenter;

public sealed record PosterChoice(string Mode,double Seconds,string SourceStamp);

// Coordinates the staged poster file and metadata under one writer lock.
internal sealed class PosterCatalogService(LibraryService library,SettingsService settings)
{
    internal event Action<string>? PosterCommitted;
    private const string PosterSelectionKey="poster.v4:";
    public static string PosterSourceStamp(string path)
    {
        var file=new FileInfo(path);
        return $"{file.FullName}:{file.Length}:{file.LastWriteTimeUtc.Ticks}";
    }
    public bool PosterSelectionCurrent(string id,string path) => File.Exists(library.Poster(id)) &&
        settings.Get(PosterSelectionKey+id)==PosterSourceStamp(path);
    public void RememberPosterSelection(string id,string path) => settings.Set(PosterSelectionKey+id,PosterSourceStamp(path));

    private readonly SemaphoreSlim posterWriteGate=new(1,1);
    private const string PosterChoiceKey="poster.choice.v1:";
    public PosterChoice? ReadPosterChoice(string id)
    {
        try {return JsonSerializer.Deserialize<PosterChoice>(settings.Get(PosterChoiceKey+id)??"null");}
        catch(JsonException) {return null;}
    }
    private void CommitPoster(string id,string source,string staged,string mode,double seconds)
    {
        File.Move(staged,library.Poster(id),true);
        PosterImageCache.Invalidate(library.Poster(id));
        settings.Set(PosterChoiceKey+id,JsonSerializer.Serialize(new PosterChoice(mode,seconds,PosterSourceStamp(source))));
        RememberPosterSelection(id,source);
        PosterCommitted?.Invoke(id);
    }
    // Metadata refresh and automatic repair use the same lock as manual edits.
    public async Task<ProbeResult> ProbePreservingPoster(string path,string id,CancellationToken token,bool automatic=false,bool requireExisting=false,Action? beforeCommit=null)
    {
        await posterWriteGate.WaitAsync(token);string stage=library.Poster(id)+"."+Guid.NewGuid().ToString("N")+".bmp";
        try {
            string stamp=PosterSourceStamp(path);
            var choice=ReadPosterChoice(id);
            bool manual=!automatic&&choice?.Mode=="manual"&&!MediaFormats.IsImage(path);
            var result=await NativePlayback.Probe(path,stage,token,metadataOnly:manual);
            token.ThrowIfCancellationRequested();
            void EnsureCurrent() {
                token.ThrowIfCancellationRequested();
                if(stamp!=PosterSourceStamp(path)||requireExisting&&library.FindAssetByPath(path)?.Id!=id)throw new IOException(UiLanguage.Text("素材已发生变化，请重新选择"));
                beforeCommit?.Invoke();
            }
            if(manual) {
                double time=Math.Clamp(choice!.Seconds,0,Math.Max(0,result.Duration-.000001));
                var selected=await MediaOperations.Frame(path,stage,time,0,token);
                EnsureCurrent();CommitPoster(id,path,stage,"manual",selected.Seconds);
            } else {EnsureCurrent();CommitPoster(id,path,stage,"auto",result.PosterSeconds);}
            return result;
        }finally {try {File.Delete(stage);}catch { }posterWriteGate.Release();}
    }
    public async Task<PosterChoice> SavePosterFrame(MediaAsset asset,double seconds,CancellationToken token,Action? beforeCommit=null)
    {
        if(asset.IsImage)throw new ArgumentException(UiLanguage.Text("静态图片不需要选择视频帧"));
        await posterWriteGate.WaitAsync(token);string stage=library.Poster(asset.Id)+"."+Guid.NewGuid().ToString("N")+".bmp";
        try {
            string stamp=PosterSourceStamp(asset.Path);
            var selected=await MediaOperations.Frame(asset.Path,stage,seconds,0,token);
            token.ThrowIfCancellationRequested();
            if(library.FindAssetByPath(asset.Path)?.Id!=asset.Id||stamp!=PosterSourceStamp(asset.Path))throw new IOException(UiLanguage.Text("素材已发生变化，请重新选择"));
            beforeCommit?.Invoke();
            CommitPoster(asset.Id,asset.Path,stage,"manual",selected.Seconds);
            return ReadPosterChoice(asset.Id)!;
        }finally {try {File.Delete(stage);}catch { }posterWriteGate.Release();}
    }
    public async Task<PosterChoice> ResetPoster(MediaAsset asset,CancellationToken token,Action? beforeCommit=null)
    {
        await ProbePreservingPoster(asset.Path,asset.Id,token,automatic:true,requireExisting:true,beforeCommit:beforeCommit);
        token.ThrowIfCancellationRequested();return ReadPosterChoice(asset.Id)!;
    }
}
