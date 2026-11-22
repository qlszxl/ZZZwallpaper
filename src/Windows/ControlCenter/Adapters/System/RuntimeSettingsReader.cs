using LumaWall.Contracts;

namespace LumaWall.ControlCenter;

internal static class RuntimeSettingsReader
{
    internal static string EffectsEnabledKey(string screen)=>"screen.effects.enabled:"+screen;
    internal static string PositionKey(string screen,string? asset=null)=>LibraryStore.WallpaperPositionKey(screen,asset);
    internal static WallpaperPosition Position(LibraryStore store,string screen,string? asset=null)=>store.ReadWallpaperPosition(screen,asset);
    internal static string WidgetsEnabledKey(string screen)=>"screen.widgets.enabled:"+screen;
    internal static bool EffectsEnabled(LibraryStore store,string screen)=>store.GetSetting(EffectsEnabledKey(screen))!="false";
    internal static bool WidgetsEnabled(LibraryStore store,string screen)=>store.GetSetting(WidgetsEnabledKey(screen))!="false";
    internal static StartupDelayOptions StartupDelay(LibraryStore store)=>new(store.GetSetting("startup.delay.enabled")=="true",
        int.TryParse(store.GetSetting("startup.delay.seconds"),out var seconds)&&seconds is >=1 and <=3600?seconds:10);
    internal static string PauseKey(PauseReason reason)=>LumaWall.Application.SettingsService.PauseKey(reason);
    internal static PauseReason PausePolicy(LibraryStore store)=>new LumaWall.Application.SettingsService(store).PausePolicy();
    internal static int FpsLimit(LibraryStore store)=>new LumaWall.Application.SettingsService(store).FpsLimit();
    internal static DecoderMode Decoder(LibraryStore store)=>new LumaWall.Application.SettingsService(store).Decoder();
}
