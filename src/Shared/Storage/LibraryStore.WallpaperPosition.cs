using LumaWall.Contracts;

namespace LumaWall.Storage;

public sealed partial class LibraryStore
{
    public static string WallpaperPositionKey(string screen,string? asset=null)
        =>"wallpaper.position:"+screen+(string.IsNullOrEmpty(asset)?"":"|"+asset);

    public WallpaperPosition ReadWallpaperPosition(string screen,string? asset=null)
    {
        asset??=Bindings().FirstOrDefault(binding=>binding.ScreenId==screen)?.AssetId;
        return WallpaperPosition.FromJson(GetSetting(WallpaperPositionKey(screen,asset)));
    }

    private void MigrateWallpaperPositions()
    {
        if(GetSetting("wallpaper.positions.migrated.v1")=="true")return;
        // Preserve the current wallpaper on upgrade; a different asset starts centered.
        // Construction runs before history is enabled, so migration is not an edit.
        foreach(var binding in Bindings())
            if(GetSetting(WallpaperPositionKey(binding.ScreenId)) is { } value
                &&GetSetting(WallpaperPositionKey(binding.ScreenId,binding.AssetId))==null)
                SetSetting(WallpaperPositionKey(binding.ScreenId,binding.AssetId),value);
        SetSetting("wallpaper.positions.migrated.v1","true");
    }
}
