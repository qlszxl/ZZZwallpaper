using LumaWall.Contracts;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private bool refreshingHomeVolume;
    private string? homeVolumeScreen;

    private void RefreshHomeVolume()
    {
        string? screen=SelectedScreen?.DeviceName;
        // A popup belongs to the screen on which it was opened. Never let a
        // remaining drag apply to a different screen after selection changes.
        if(homeVolumeScreen!=null&&homeVolumeScreen!=screen) {
            homeVolumeScreen=null;HomeVolumeFlyout.Hide();
        }
        int volume=screen==null?0:modules.Playback.Volume(screen);
        bool enabled=screen!=null&&modules.Settings.Get("videoSound:"+screen)!="0";
        int displayed=enabled?volume:0;
        string glyph=displayed==0?"\uE74F":displayed<34?"\uE993":displayed<67?"\uE994":"\uE767";
        HomeVolumeButton.IsEnabled=screen!=null;
        HomeVolumeIcon.Glyph=glyph;
        string description=screen==null?UiLanguage.Text("壁纸音量"):UiLanguage.Format($"{SelectedScreen} · 壁纸音量 {displayed}%");
        ToolTipService.SetToolTip(HomeVolumeButton,description);
        AutomationProperties.SetName(HomeVolumeButton,description);
        if(HomeVolumeSlider==null||HomeVolumeValue==null)return;
        refreshingHomeVolume=true;
        try {
            HomeVolumeSlider.Value=displayed;
            HomeVolumeValue.Text=$"{displayed}%";
            HomeVolumeMuteIcon.Glyph=glyph;
            string action=enabled?UiLanguage.Text("静音"):UiLanguage.Text("取消静音");
            AutomationProperties.SetName(HomeVolumeMuteButton,action);
            ToolTipService.SetToolTip(HomeVolumeMuteButton,action);
        } finally {refreshingHomeVolume=false;}
    }

    private void HomeVolume_Opening(object sender,object e)
    {
        BeginHistoryGesture();homeVolumeScreen=SelectedScreen?.DeviceName;RefreshHomeVolume();
    }
    private void HomeVolume_Closed(object sender,object e)
    {
        homeVolumeScreen=null;EndHistoryGesture();
    }
    private void HomeVolume_Changed(object sender,RangeBaseValueChangedEventArgs e)
    {
        if(refreshingHomeVolume||homeVolumeScreen is not { } screen||screen!=SelectedScreen?.DeviceName)return;
        int volume=(int)Math.Round(e.NewValue);
        if(wallpapers!=null)wallpapers.SetVideoSoundVolume(screen,volume);
        else {modules.Playback.SetVolume(screen,volume);RefreshStatus();}
    }
}
