namespace LumaWall.Contracts.Application;

public interface IPlaybackStore
{
    long PlaybackLibraryRevision { get; }
    PlaybackSettings Playback(string screen);
    void SavePlayback(string screen, PlaybackSettings settings);
    int WallpaperVolume(string screen);
    void SaveWallpaperVolume(string screen, int volume);
}
