using LumaWall.Contracts;
using LumaWall.DesktopIntegration;

namespace LumaWall.ControlCenter;

public partial class App
{
    private void PreviewDesktopVolume(DesktopMenuVolume value)
    {
        if(quitting||wallpapers?.Running.GetValueOrDefault(value.ScreenId) is not { } current)return;
        // Live feedback never changes settings or the currently selected UI screen.
        current.Player.SetSoundVolume(value.Volume);
        current.Player.SetSound(value.SoundEnabled);
    }
    private void CommitDesktopVolume(DesktopMenuVolume value,PlaybackSettingsService playback)
    {
        if(quitting||wallpapers==null)return;
        try {
            // A drag through zero remembers its last audible level, just like
            // the home volume control, but saves only when the gesture ends.
            if(!value.SoundEnabled&&playback.Volume(value.ScreenId)!=value.Volume)playback.SetVolume(value.ScreenId,value.Volume);
            wallpapers.SetVideoSoundVolume(value.ScreenId,value.SoundEnabled?value.Volume:0);
        } catch(Exception error) {
            wallpapers.ReloadVideoSound(value.ScreenId);
            window?.ShowError(UiLanguage.Text("音量未保存：")+error.Message,error);
        }
    }
}
