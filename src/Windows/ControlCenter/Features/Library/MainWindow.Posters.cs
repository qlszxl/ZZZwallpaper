using LumaWall.Contracts;
using Microsoft.UI.Xaml.Media.Imaging;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private readonly CancellationTokenSource posterRepairCancellation=new();
    private static BitmapImage? LoadPosterImage(string? path,int width=640)=>PosterImageCache.Load(path,width);

    private const string PosterSelectionKey="poster.v4:";
    private static string PosterSourceStamp(string path)=>PosterCatalogService.PosterSourceStamp(path);
    private bool PosterSelectionCurrent(string id,string path)=>posterCatalog.PosterSelectionCurrent(id,path);
    private void RememberPosterSelection(string id,string path)=>posterCatalog.RememberPosterSelection(id,path);

    internal static Task<(MediaAsset Asset,string Poster)[]> FindStaleLibraryPostersAsync(
        IEnumerable<(MediaAsset Asset,string Poster)> sources,IReadOnlyDictionary<string,string> stamps,CancellationToken token)
        =>Task.Run(()=>sources.Where(source=> {
            token.ThrowIfCancellationRequested();
            try {
                if(!File.Exists(source.Asset.Path))return false;
                return !File.Exists(source.Poster)||stamps.GetValueOrDefault(PosterSelectionKey+source.Asset.Id)!=PosterSourceStamp(source.Asset.Path);
            } catch(Exception error) when(error is IOException or UnauthorizedAccessException or System.Security.SecurityException) {
                AppLog.Warning("Poster",$"Preview check skipped: {source.Asset.Name}",error);return false;
            }
        }).ToArray(),token);

    private async Task RepairLibraryPosters()
    {
        var token=posterRepairCancellation.Token;
        try {
            await Task.Delay(1200,token);
            // SQLite stays on the UI thread. Healthy libraries are then checked
            // by one cancellable worker instead of blocking input once per file.
            var sources=modules.Library.Assets().Select(asset=>(Asset:asset,Poster:modules.Library.Poster(asset.Id))).ToArray();
            var stamps=modules.Settings.GetByPrefix(PosterSelectionKey);
            var stale=await FindStaleLibraryPostersAsync(sources,stamps,token);
            foreach(var source in stale) {
                var asset=source.Asset;
                token.ThrowIfCancellationRequested();
                while(importLifetime.Active!=null)await Task.Delay(300,token);
                if(modules.Library.FindAssetByPath(asset.Path)?.Id!=asset.Id)continue;
                try {
                    // A manual poster choice may have completed during the scan.
                    string? currentStamp=modules.Settings.Get(PosterSelectionKey+asset.Id);
                    if(await Task.Run(()=>File.Exists(source.Poster)&&currentStamp==PosterSourceStamp(asset.Path),token))continue;
                    token.ThrowIfCancellationRequested();
                    await ProbePreservingPoster(asset.Path,asset.Id,token,requireExisting:true);
                    token.ThrowIfCancellationRequested();
                    if(modules.Library.FindAssetByPath(asset.Path)?.Id!=asset.Id)continue;
                    AppLog.Info("Poster",$"Rebuilt cover with brightness and sharpness selection: {asset.Name}");
                } catch(OperationCanceledException) when(token.IsCancellationRequested) {throw;}
                  catch(Exception error) {AppLog.Warning("Poster",$"Preview repair failed: {asset.Name}",error);}
            }
        } catch(OperationCanceledException) { }
          catch(Exception error) {AppLog.Warning("Poster","Preview repair stopped",error);}
    }
}
