using LumaWall.Application;
using LumaWall.Contracts;
using LumaWall.Contracts.Application;

internal static class CatalogSettingsChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var data=new MemoryCatalog();
        MediaAsset Asset(string id,string name,string path)=>new(id,path,name,1,1,1,1,"fixture","","fixture");
        var first=Asset("a","alpha","C:/fixtures/a.mp4");
        var second=Asset("b","beta","C:/fixtures/b.mp4");
        var duplicateName=Asset("c","alpha","C:/fixtures/c.mp4");
        data.Catalog.AddRange([second,duplicateName,first]);
        var library=new LibraryService(data);
        var explicitQueue=library.ResolveQueue(new() {AssetIds=["b","missing","a"]});
        check(explicitQueue.Select(a=>a.Id).SequenceEqual(["b","a"]),"Explicit queues preserve requested order and omit missing materials");
        var folderQueue=library.ResolveQueue(new() {FolderId="folder",AssetIds=["b"]});
        check(folderQueue.Select(a=>a.Id).SequenceEqual(["a","c","b"]),"Folder queues sort by display name and then stable path");

        var settings=new SettingsService(data);
        var defaults=PauseReason.SessionLocked|PauseReason.DisplayOff|PauseReason.Suspend;
        check(settings.PausePolicy()==defaults,"Default pause policy retains lock, display-off and suspend");
        data.SetSetting(SettingsService.PauseKey(PauseReason.DisplayOff),"false");
        data.SetSetting(SettingsService.PauseKey(PauseReason.HighResourceLoad),"true");
        check(settings.PausePolicy()==((defaults&~PauseReason.DisplayOff)|PauseReason.HighResourceLoad),"Stored pause overrides change only their corresponding reasons");
        data.SetSetting("fpsLimit","241");data.SetSetting("decoderMode","999");
        check(settings.FpsLimit()==0&&settings.Decoder()==DecoderMode.Auto,"Invalid stored runtime options fall back to supported defaults");
        data.SetSetting("fpsLimit","240");data.SetSetting("decoderMode","Software");
        check(settings.FpsLimit()==240&&settings.Decoder()==DecoderMode.Software,"Supported runtime option boundaries remain valid");
        data.SetSetting("poster.v4:a","first");data.SetSetting("poster.v4:b","second");
        var stamps=settings.GetByPrefix("poster.v4:");
        data.SetSetting("poster.v4:a","changed");
        check(stamps.Count==2&&stamps["poster.v4:a"]=="first"&&settings.GetByPrefix("poster.v4:")["poster.v4:a"]=="changed",
            "Settings snapshots contain only their prefix and remain stable during later writes");

        var playback=new PlaybackSettingsService(data,data);
        playback.Save("screen",new() {Version=1,Mode=(PlaybackMode)999,IntervalSeconds=0,AssetIds=["a","a","","b"]});
        var saved=data.Playback("screen");
        check(saved.Version==2&&saved.Mode==PlaybackMode.Automatic&&saved.IntervalSeconds==10&&saved.AssetIds.SequenceEqual(["a","b"]),"Playback writes normalize legacy settings before reaching the store");
        check(saved.CustomPlaylists is {Count:1}&&saved.CustomPlaylists[0].AssetIds.SequenceEqual(["a","b"]),"Legacy queues gain a named playlist without losing material order");
        check(playback.SoundEnabled("screen"),"Missing sound preference retains the existing enabled default");
        playback.SetSound("screen",false);
        check(!playback.SoundEnabled("screen")&&data.GetSetting("videoSound:screen")=="0","Sound state remains compatible with persisted legacy values");
    }

    private sealed class MemoryCatalog : ILibraryStore,ISettingsStore,IPlaybackStore
    {
        public List<MediaAsset> Catalog {get;}=[];
        private readonly Dictionary<string,string> settings=new();
        private readonly Dictionary<string,PlaybackSettings> playback=new();
        public long PlaybackLibraryRevision=>0;
        public List<MediaAsset> Assets()=>Catalog.ToList();
        public List<MediaAsset> FolderAssets(string folderId)=>Catalog.ToList();
        public MediaAsset? FindAssetByPath(string path)=>Catalog.FirstOrDefault(a=>a.Path==path);
        public void Upsert(MediaAsset asset) {Catalog.RemoveAll(a=>a.Id==asset.Id);Catalog.Add(asset);}
        public void Remove(string assetId)=>Catalog.RemoveAll(a=>a.Id==assetId);
        public List<MediaFolder> Folders()=>[];
        public MediaFolder SaveFolder(MediaFolder folder)=>folder;
        public void ReplaceFolderAssets(string folderId,IEnumerable<string> assetIds)=>throw new NotSupportedException();
        public void RemoveFolder(string folderId)=>throw new NotSupportedException();
        public string Poster(string id)=>id+".bmp";
        public bool? AssetHasAudio(MediaAsset asset)=>null;
        public void RememberAssetAudio(MediaAsset asset,bool? hasAudio)=>throw new NotSupportedException();
        public string? GetSetting(string key)=>settings.GetValueOrDefault(key);
        public IReadOnlyDictionary<string,string> GetSettingsByPrefix(string prefix)=>settings
            .Where(pair=>pair.Key.StartsWith(prefix,StringComparison.Ordinal)).ToDictionary(pair=>pair.Key,pair=>pair.Value,StringComparer.Ordinal);
        public void SetSetting(string key,string value)=>settings[key]=value;
        public PlaybackSettings Playback(string screen)=>playback.GetValueOrDefault(screen)??new();
        public void SavePlayback(string screen,PlaybackSettings value)=>playback[screen]=value;
        public int WallpaperVolume(string screen)=>100;
        public void SaveWallpaperVolume(string screen,int volume)=>throw new NotSupportedException();
    }
}
