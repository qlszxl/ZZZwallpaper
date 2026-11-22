using LumaWall.Contracts;
using Microsoft.UI.Xaml;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private void SetScreenFeatures(string screen,bool? effects,bool? components)
    {
        FlushHistoryEdits();
        using var history=store.BeginUndoBatch("修改显示器总开关");
        if(effects is { } effectEnabled) {
            modules.Settings.Set(RuntimeSettingsReader.EffectsEnabledKey(screen),effectEnabled?"true":"false");
            wallpapers?.RefreshScreenEffectsState(screen);
        }
        if(components is { } widgetEnabled) {
            modules.Settings.Set(RuntimeSettingsReader.WidgetsEnabledKey(screen),widgetEnabled?"true":"false");widgets?.Refresh();
        }
        RefreshScreenFeatureButtons();
    }
    private void RefreshScreenFeatureButtons()
    {
        var screen=SelectedScreen?.DeviceName;
        bool effects=screen!=null&&RuntimeSettingsReader.EffectsEnabled(store,screen),components=screen!=null&&RuntimeSettingsReader.WidgetsEnabled(store,screen);
        EffectsMasterButton.IsEnabled=screen!=null&&effects;EffectsEnableButton.IsEnabled=screen!=null&&!effects;
        WidgetsMasterButton.IsEnabled=screen!=null&&components;WidgetsEnableButton.IsEnabled=screen!=null&&!components;
    }
    private void EffectsEnable_Click(object sender,RoutedEventArgs e)=>ChangeScreenMaster(true,true);
    private void WidgetsEnable_Click(object sender,RoutedEventArgs e)=>ChangeScreenMaster(false,true);
    private void ChangeScreenMaster(bool effects,bool enabled)
    {
        if(SelectedScreen is not { } screen)return;
        try {SetScreenFeatures(screen.DeviceName,effects?enabled:null,effects?null:enabled);}
        catch(Exception error) {ShowError(UiLanguage.Text("总开关未完成：")+error.Message,error);}
    }
    private void EffectsMaster_Click(object sender,RoutedEventArgs e)
    {
        if(SelectedScreen is not { } screen)return;
        try {SetScreenFeatures(screen.DeviceName,false,null);}
        catch(Exception error) {ShowError(UiLanguage.Text("效果总开关未完成：")+error.Message,error);}
    }
    private void WidgetsMaster_Click(object sender,RoutedEventArgs e)
    {
        if(SelectedScreen is not { } screen)return;
        try {SetScreenFeatures(screen.DeviceName,null,false);}
        catch(Exception error) {ShowError(UiLanguage.Text("组件总开关未完成：")+error.Message,error);}
    }
}
