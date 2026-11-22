using LumaWall.Contracts;
namespace LumaWall.ControlCenter;
public sealed partial class WallpaperController
{
    public void RefreshPosition(string screen,WallpaperPosition? preview=null,string? asset=null)
    {
        if(disposed)return;
        if(running.TryGetValue(screen,out var current)&&(asset==null||current.Asset.Id==asset))
            current.Player.SetPosition(preview??RuntimeSettingsReader.Position(store,screen,current.Asset.Id));
        if(Preview is { } session&&session.ScreenId==screen&&(asset==null||session.Asset.Id==asset))
            session.Player.SetPosition(preview??RuntimeSettingsReader.Position(store,screen,session.Asset.Id));
    }
}
