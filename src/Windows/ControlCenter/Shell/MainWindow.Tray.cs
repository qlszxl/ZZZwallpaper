using LumaWall.Contracts;
using LumaWall.DesktopIntegration;
using Screen=System.Windows.Forms.Screen;

namespace LumaWall.ControlCenter;
public sealed partial class MainWindow
{
    public TrayMenuState TrayState()
    {
        var active=wallpapers?.Running.Values.ToArray()??[];
        var screens=Screen.AllScreens;
        return new(active.Length>0,active.Length>0&&active.All(value=>value.Pause.Reasons.HasFlag(PauseReason.User)),
            active.Any(value=>wallpapers!.VideoSoundEnabled(value.Binding.ScreenId)),
            screens.Any(screen=>RuntimeSettingsReader.EffectsEnabled(store,screen.DeviceName)),
            screens.Any(screen=>RuntimeSettingsReader.WidgetsEnabled(store,screen.DeviceName)));
    }
    public void RunTrayCommand(int command)
    {
        try {
            string? page=command switch {20=>"library",21=>"display",22=>"widgets",23=>"presets",24=>"settings",_=>null};
            if(page!=null) {ShowControlCenter();Navigate(page);return;}
            var state=TrayState();
            if(command==25&&wallpapers!=null) {
                bool pause=!state.AllPaused;
                foreach(var item in wallpapers.Running.Values.ToArray())if(item.Pause.Reasons.HasFlag(PauseReason.User)!=pause)wallpapers.ToggleUserPause(item.Binding.ScreenId);
            } else if(command==26&&wallpapers!=null) {
                foreach(var item in wallpapers.Running.Values.ToArray())wallpapers.SetVideoSound(item.Binding.ScreenId,!state.AnySound);
            } else if(command is 27 or 28) {
                using var history=store.BeginUndoBatch(command==27?"切换全部屏幕效果":"切换全部屏幕组件");
                foreach(var screen in Screen.AllScreens)SetScreenFeatures(screen.DeviceName,command==27?!state.AnyEffects:null,command==28?!state.AnyWidgets:null);
                RefreshWidgets();
            }
            RefreshStatus();
        } catch(Exception error) {ShowError(UiLanguage.Text("托盘操作未完成：")+error.Message,error);}
    }
}
