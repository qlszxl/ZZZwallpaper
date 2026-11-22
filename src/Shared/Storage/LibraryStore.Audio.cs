using System.Text.Json;
using LumaWall.Contracts;

namespace LumaWall.Storage;

public sealed partial class LibraryStore
{
    public int WallpaperVolume(string screen) =>
        int.TryParse(GetSetting("videoVolume:"+screen),System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture,out int value)&&value is >=1 and <=100?value:100;

    public void SaveWallpaperVolume(string screen,int volume)
    {
        volume=Math.Clamp(volume,0,100);
        using var history=CaptureUndo("调节壁纸音量","wallpaper-volume:"+screen,
            new UndoScope("settings","key","videoVolume:"+screen),new UndoScope("settings","key","videoSound:"+screen));
        // Zero mutes while retaining the last audible level for unmute.
        if(volume>0&&WallpaperVolume(screen)!=volume)
            SetSetting("videoVolume:"+screen,volume.ToString(System.Globalization.CultureInfo.InvariantCulture));
        string enabled=volume>0?"1":"0";
        if(GetSetting("videoSound:"+screen)!=enabled)SetSetting("videoSound:"+screen,enabled);
    }

    private sealed record AudioMetadata(string Path,long Bytes,bool HasAudio);
    private readonly Dictionary<MediaAsset,bool?> audioMetadataCache=new();

    public bool? AssetHasAudio(MediaAsset asset)
    {
        if(audioMetadataCache.TryGetValue(asset,out var cached))return cached;
        bool? result=null;
        try
        {
            var json=GetSetting("assetAudio:"+asset.Id);
            var value=json==null?null:JsonSerializer.Deserialize<AudioMetadata>(json);
            if(value?.Path==asset.Path&&value.Bytes==asset.Bytes)result=value.HasAudio;
        }
        catch(JsonException) { }
        audioMetadataCache[asset]=result;return result;
    }

    public void RememberAssetAudio(MediaAsset asset,bool? hasAudio)
    {
        if(hasAudio==null)return;
        if(AssetHasAudio(asset)!=hasAudio)
            SetSetting("assetAudio:"+asset.Id,JsonSerializer.Serialize(new AudioMetadata(asset.Path,asset.Bytes,hasAudio.Value)));
        audioMetadataCache[asset]=hasAudio;
    }
}
