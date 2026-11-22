using LumaWall.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Screen=System.Windows.Forms.Screen;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private bool syncingSettings;
    private bool startupSaveFailed;
    private Action? syncStartupSettings,flushStartupSettings;
    private void SetStartupConfiguration(bool enabled,StartupDelayOptions delay)
    {
        delay.Validate();StartupRegistration.SetEnabled(enabled,delay.EffectiveSeconds);
        using var history=store.BeginUndoBatch("修改启动延时");
        modules.Settings.Set("startup.delay.enabled",delay.Enabled?"true":"false");
        modules.Settings.Set("startup.delay.seconds",delay.Seconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
        startupSaveFailed=false;
        syncStartupSettings?.Invoke();
    }
    private StackPanel? menuScreenOptions;
    private readonly Dictionary<string,ToggleSwitch> menuScreenSwitches=new();
    private bool syncingMenuScreenSwitches;
    private void InitializeSettings()
    {
        InitializeLanguageSetting();
        InitializeHelpHints();
        InitializeHomePosition();
        InitializeSettingsNavigation();
        InitializeExpansionSettings();
        InitializeAudioProcessingSettings();
        InitializeDiagnosticsSettings();
        InitializeSharingSettings();
        InitializeAutomationSettings();
        InitializeUpdateHistory();
        InitializeResourceLoadPauseSettings();
        syncingSettings=true;
        var start=WidgetToggle(UiLanguage.Text("开机自动启动"),false);
        try {StartupRegistration.MigrateLegacyName();start.IsOn=StartupRegistration.IsEnabled();}catch(Exception ex) {AppLog.Warning("StartupRegistration","Cannot read startup registration",ex);ShowError(UiLanguage.Text("读取启动项失败：")+ex.Message,ex);}
        var startup=new StackPanel {Spacing=12};startup.Children.Add(UtilityText(UiLanguage.Text("启动"),"UtilityHeading"));startup.Children.Add(WidgetToggleRow(UiLanguage.Text("开机自动启动"),start));
        var startupOptions=RuntimeSettingsReader.StartupDelay(store);
        var delayStartup=WidgetToggle(UiLanguage.Text("延时启动"),startupOptions.Enabled);
        var startupDelay=new Slider {Style=HomeStyle("HomeSlider"),Minimum=1,Maximum=3600,StepFrequency=1,Value=startupOptions.Seconds};
        startup.Children.Add(WidgetToggleRow(UiLanguage.Text("延时启动"),delayStartup));startup.Children.Add(NumericSliderRow(UiLanguage.Text("延时"),startupDelay,UiLanguage.Text("秒"),0));
        void SyncStartupControls() {delayStartup.IsEnabled=start.IsOn;startupDelay.IsEnabled=start.IsOn&&delayStartup.IsOn;}
        SyncStartupControls();
        void SaveStartupDelay() {
            SyncStartupControls();if(syncingSettings)return;
            try {
                var options=new StartupDelayOptions(delayStartup.IsOn,(int)Math.Round(startupDelay.Value)).Validate();
                SetStartupConfiguration(start.IsOn,options);
            }catch(Exception ex) {startupSaveFailed=true;ShowError(UiLanguage.Text("启动延时未保存：")+ex.Message,ex);}
        }
        var startupSaveTimer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(250)};
        flushStartupSettings=()=> {if(!startupSaveTimer.IsEnabled)return;startupSaveTimer.Stop();SaveStartupDelay();};
        startupSaveTimer.Tick+=(_,_)=>flushStartupSettings();
        delayStartup.Toggled+=(_,_)=> {startupSaveTimer.Stop();SaveStartupDelay();};
        startupDelay.ValueChanged+=(_,_)=> {if(syncingSettings)return;startupSaveTimer.Stop();startupSaveTimer.Start();};
        Closed+=(_,_)=>flushStartupSettings();
        syncStartupSettings=()=> {
            startupSaveTimer.Stop();bool previous=syncingSettings;syncingSettings=true;
            try {var options=RuntimeSettingsReader.StartupDelay(store);start.IsOn=StartupRegistration.IsEnabled();delayStartup.IsOn=options.Enabled;startupDelay.Value=options.Seconds;SyncStartupControls();}
            finally {syncingSettings=previous;}
        };
        InitializeHelpHintsSetting(startup);SettingsStartupHost.Content=startup;
        start.Toggled+=(_,_)=> {
            if(syncingSettings)return;
            try {StartupRegistration.SetEnabled(start.IsOn,delayStartup.IsOn?(int)Math.Round(startupDelay.Value):0);SyncStartupControls();}
            catch(Exception ex) {syncingSettings=true;start.IsOn=!start.IsOn;syncingSettings=false;ShowError(UiLanguage.Text("启动设置未保存：")+ex.Message,ex);}
        };
        int saved=modules.Settings.FpsLimit();var limit=WidgetToggle(UiLanguage.Text("限制画面帧率"),saved>0);
        var fps=new Slider {Style=HomeStyle("HomeSlider"),Minimum=1,Maximum=240,StepFrequency=1,Value=saved>0?saved:30,IsEnabled=saved>0};
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(fps,UiLanguage.Text("画面 FPS 上限"));
        var performance=new StackPanel {Spacing=14};performance.Children.Add(UtilityText(UiLanguage.Text("画面帧率限制"),"UtilityHeading"));performance.Children.Add(WidgetToggleRow(UiLanguage.Text("限制画面帧率"),limit));
        performance.Children.Add(NumericSliderRow(UiLanguage.Text("FPS 上限"),fps,"FPS",0));
        
        InitializeDecodeSettings(performance);
        SettingsPerformanceHost.Content=performance;
        void SaveCap() {
            fps.IsEnabled=limit.IsOn;int cap=limit.IsOn?(int)Math.Round(fps.Value):0;
            if(syncingSettings)return;
            try {modules.Settings.Set("fpsLimit",cap.ToString(System.Globalization.CultureInfo.InvariantCulture));wallpapers?.SetFrameRateLimit(cap);}
            catch(Exception ex) {ShowError(UiLanguage.Text("帧率设置未保存：")+ex.Message,ex);}
        }
        limit.Toggled+=(_,_)=>SaveCap();fps.ValueChanged+=(_,_)=>SaveCap();SaveCap();
        syncAutomationFps=cap=> {
            syncingSettings=true;
            try {limit.IsOn=cap>0;fps.Value=cap>0?cap:30;SaveCap();}
            finally {syncingSettings=false;}
        };
        menuScreenOptions=new StackPanel {Spacing=12};SettingsScreenMenuHost.Content=menuScreenOptions;
        var pauseOptions=new StackPanel {Spacing=12};SettingsPauseHost.Content=pauseOptions;
        foreach(var item in new[]{(PauseReason.SessionLocked,UiLanguage.Text("锁屏时暂停")),(PauseReason.DisplayOff,UiLanguage.Text("熄屏时暂停")),(PauseReason.Suspend,UiLanguage.Text("休眠时暂停"))}) {
            var toggle=WidgetToggle(item.Item2,modules.Settings.Get(RuntimeSettingsReader.PauseKey(item.Item1))!="false");
            pauseOptions.Children.Add(WidgetToggleRow(item.Item2,toggle));
            toggle.Toggled+=(_,_)=> {
                if(syncingSettings)return;
                try {modules.Settings.Set(RuntimeSettingsReader.PauseKey(item.Item1),toggle.IsOn?"true":"false");wallpapers?.SetPausePolicy(modules.Settings.PausePolicy());}
                catch(Exception ex) {syncingSettings=true;toggle.IsOn=!toggle.IsOn;syncingSettings=false;ShowError(UiLanguage.Text("暂停设置未保存：")+ex.Message,ex);}
            };
        }
        syncingSettings=false;RefreshScreenMenuSettings();
    }
    private static string MenuScreenKey(string device)=>DesktopMenuPreference.Key(device);
    internal static string[] EnabledMenuScreens(LibraryStore store)=>Screen.AllScreens.Where(s=>DesktopMenuPreference.IsEnabled(store.GetSetting(MenuScreenKey(s.DeviceName)))).Select(s=>s.DeviceName).ToArray();
    public void ResumeDesktopMenu()
    {
        var screens=EnabledMenuScreens(store);
        if(screens.Length==0) {
            var screen=SelectedScreen??Screen.PrimaryScreen??Screen.AllScreens[0];
            modules.Settings.Set(MenuScreenKey(screen.DeviceName),"true");screens=[screen.DeviceName];
        }
        modules.Settings.Set("menuSafeDisabled","false");menu?.SetEnabledScreens(screens);menu?.SetEnabled(true);RefreshStatus();
    }
    private void RefreshScreenMenuSettings()
    {
        if(menuScreenOptions==null)return;menuScreenOptions.Children.Clear();menuScreenSwitches.Clear();
        foreach(var screen in Screen.AllScreens) {
            string id=screen.DeviceName;var toggle=WidgetToggle(new ScreenRow(screen).ToString(),DesktopMenuPreference.IsEnabled(modules.Settings.Get(MenuScreenKey(id))));
            menuScreenSwitches[id]=toggle;
            menuScreenOptions.Children.Add(WidgetToggleRow(new ScreenRow(screen).ToString(),toggle));
            toggle.Toggled+=(_,_)=> {
                if(syncingMenuScreenSwitches)return;
                try {
                    // All controls reflect the paused state. Resuming one screen
                    // must not silently re-enable other screens shown as off.
                    if(menu?.Enabled==false&&toggle.IsOn)
                        foreach(var entry in menuScreenSwitches)modules.Settings.Set(MenuScreenKey(entry.Key),entry.Value.IsOn?"true":"false");
                    modules.Settings.Set(MenuScreenKey(id),toggle.IsOn?"true":"false");menu?.SetEnabledScreens(EnabledMenuScreens(store));
                    if(toggle.IsOn) {modules.Settings.Set("menuSafeDisabled","false");menu?.SetEnabled(true);}
                    else if(EnabledMenuScreens(store).Length==0)menu?.SetEnabled(false);
                    RefreshStatus();
                }
                catch(Exception ex) {ShowError(UiLanguage.Text("屏幕右键设置未保存：")+ex.Message,ex);}
                finally {SyncMenuScreenSwitches();}
            };
        }
        menu?.SetEnabledScreens(EnabledMenuScreens(store));
        if(EnabledMenuScreens(store).Length==0)menu?.SetEnabled(false);
        SyncMenuScreenSwitches();
    }
    private void SyncMenuScreenSwitches()
    {
        syncingMenuScreenSwitches=true;
        try {foreach(var entry in menuScreenSwitches)entry.Value.IsOn=menu?.Enabled!=false&&DesktopMenuPreference.IsEnabled(modules.Settings.Get(MenuScreenKey(entry.Key)));}
        finally {syncingMenuScreenSwitches=false;}
    }
    private static FrameworkElement NumericSliderRow(string title,Slider slider,string unit,int precision)
    {
        var panel=new StackPanel {Spacing=6};var header=new Grid {ColumnSpacing=12};header.ColumnDefinitions.Add(new());header.ColumnDefinitions.Add(new() {Width=GridLength.Auto});
        var label=UtilityText(title,"UtilityLabel");label.VerticalAlignment=VerticalAlignment.Center;header.Children.Add(label);
        var value=new EffectValueInput {Target=slider,Unit=unit,Precision=precision,Width=172};Grid.SetColumn(value,1);header.Children.Add(value);panel.Children.Add(header);panel.Children.Add(slider);return panel;
    }
}
