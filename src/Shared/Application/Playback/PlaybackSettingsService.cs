using LumaWall.Contracts;
using LumaWall.Contracts.Application;

namespace LumaWall.Application;

public sealed class PlaybackSettingsService(IPlaybackStore store, ISettingsStore settings)
{
    public long LibraryRevision => store.PlaybackLibraryRevision;
    public PlaybackSettings Read(string screen) => store.Playback(screen).Validate();
    public void Save(string screen, PlaybackSettings value) => store.SavePlayback(screen, value.Validate());
    public int Volume(string screen) => store.WallpaperVolume(screen);
    public void SetVolume(string screen, int volume) => store.SaveWallpaperVolume(screen, volume);
    public bool SoundEnabled(string screen) => settings.GetSetting("videoSound:" + screen) != "0";
    public void SetSound(string screen, bool enabled) => settings.SetSetting("videoSound:" + screen, enabled ? "1" : "0");
}
