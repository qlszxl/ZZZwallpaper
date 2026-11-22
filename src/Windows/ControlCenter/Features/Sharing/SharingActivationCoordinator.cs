using LumaWall.Contracts;

namespace LumaWall.ControlCenter;

// A committed configuration is durable even if either runtime cannot restart.
// UI refreshes subscribe separately; neither runtime requires a MainWindow.
internal sealed class SharingActivationCoordinator(Func<WidgetController?> widgets,Func<WallpaperController?> wallpapers)
{
    public event Action? WidgetsActivated;
    public event Action? EffectsActivated;
    public string Activate(WidgetConfiguration state,string? screen,WallpaperProfile? profile)
    {
        var warnings=new List<string>();
        try {widgets()?.AcceptImportedState(state);WidgetsActivated?.Invoke();}
        catch(Exception ex) {AppLog.Error("Sharing","Imported widget refresh failed",ex);warnings.Add(UiLanguage.Text("组件刷新失败，可重启软件恢复。"));}
        try {
            if(profile!=null&&screen!=null)wallpapers()?.RefreshLiveEffects(EffectChange.Screen(screen));
            EffectsActivated?.Invoke();
        } catch(Exception ex) {AppLog.Error("Sharing","Imported effect refresh failed",ex);warnings.Add(UiLanguage.Text("效果已保存，实时同步失败，请重新应用壁纸。"));}
        return string.Join(" ",warnings);
    }
}
