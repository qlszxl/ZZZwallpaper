using LumaWall.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private const double LoadPauseThreshold=80,LoadResumeThreshold=50;
    private const long LoadPauseDelay=3000,LoadResumeDelay=5000,LoadUnavailableDelay=10000;
    // Independent of the visible performance page: keep monitoring while hidden in the tray.
    private readonly DispatcherTimer resourceLoadTimer=new() {Interval=TimeSpan.FromSeconds(1)};
    private ExternalResourceSampler? resourceLoadSampler,resourceLoadSamplingInstance;
    private ToggleSwitch? resourceLoadSwitch;
    private TextBlock? resourceLoadStatus;
    private bool syncingResourceLoad, resourceLoadBusy,resourceLoadPaused,resourceLoadDisposed;
    private long? loadHighSince,loadLowSince,loadMissingSince;
    private long loadLastSample;

    private void InitializeResourceLoadPauseSettings()
    {
        resourceLoadSwitch=WidgetToggle(UiLanguage.Text("其他程序高 CPU / GPU 占用时暂停壁纸"),false);
        resourceLoadStatus=UtilityText(UiLanguage.Text("已关闭"));resourceLoadStatus.TextWrapping=TextWrapping.Wrap;
        var panel=new StackPanel {Spacing=12};
        panel.Children.Add(UtilityText(UiLanguage.Text("高负载自动暂停"),"UtilityHeading"));
        panel.Children.Add(WidgetToggleRow(UiLanguage.Text("其他程序高 CPU / GPU 占用时暂停壁纸"),resourceLoadSwitch));
        panel.Children.Add(resourceLoadStatus);
        PerformanceResourcePauseHost.Content=panel;
        resourceLoadTimer.Tick+=ResourceLoadTimer_Tick;
        resourceLoadSwitch.Toggled+=(_,_)=> {
            if(syncingResourceLoad)return;
            try {
                modules.Settings.Set(RuntimeSettingsReader.PauseKey(PauseReason.HighResourceLoad),resourceLoadSwitch.IsOn?"true":"false");
                SyncResourceLoadPauseSettings();
            }catch(Exception ex) {SyncResourceLoadPauseSettings();ShowError(UiLanguage.Text("高负载自动暂停设置未保存：")+ex.Message,ex);}
        };
        SyncResourceLoadPauseSettings();
    }

    private void SyncResourceLoadPauseSettings()
    {
        bool enabled=modules.Settings.PausePolicy().HasFlag(PauseReason.HighResourceLoad);
        syncingResourceLoad=true;
        try {if(resourceLoadSwitch!=null)resourceLoadSwitch.IsOn=enabled;}
        finally {syncingResourceLoad=false;}
        wallpapers?.SetPausePolicy(modules.Settings.PausePolicy());
        if(enabled&&!resourceLoadDisposed&&wallpapers!=null) {
            if(resourceLoadSampler!=null)return;
            resourceLoadSampler=new ExternalResourceSampler();
            ResetResourceLoadTiming();SetResourceLoadStatus(UiLanguage.Text("等待 CPU / GPU 采样…"));
            resourceLoadTimer.Start();_ = SampleResourceLoadAsync();
        } else {
            StopResourceLoadMonitoring();SetResourceLoadStatus(enabled?UiLanguage.Text("等待 CPU / GPU 采样…"):UiLanguage.Text("已关闭"));
        }
    }

    private void StopResourceLoadMonitoring()
    {
        resourceLoadTimer.Stop();var sampler=resourceLoadSampler;resourceLoadSampler=null;
        // A worker exclusively owns its PDH query until the pending read returns.
        if(!ReferenceEquals(sampler,resourceLoadSamplingInstance))sampler?.Dispose();
        ResetResourceLoadTiming();SetResourceLoadPaused(false);
    }
    private void DisposeResourceLoadPauseMonitoring()
    {
        if(resourceLoadDisposed)return;
        resourceLoadDisposed=true;resourceLoadTimer.Tick-=ResourceLoadTimer_Tick;StopResourceLoadMonitoring();
    }
    private void ResetResourceLoadTiming() {loadHighSince=loadLowSince=loadMissingSince=null;loadLastSample=0;}
    private void SetResourceLoadStatus(string text) {if(resourceLoadStatus!=null)resourceLoadStatus.Text=text;}
    private void SetResourceLoadPaused(bool paused)
    {
        if(resourceLoadPaused==paused)return;
        resourceLoadPaused=paused;
        // Only clear our own pause reason; user/fullscreen/lock pauses remain intact.
        wallpapers?.SetEnvironmentPause(PauseReason.HighResourceLoad,paused);
    }
    private async void ResourceLoadTimer_Tick(object? sender,object e)=>await SampleResourceLoadAsync();
    private async Task SampleResourceLoadAsync()
    {
        if(resourceLoadDisposed||resourceLoadBusy||resourceLoadSampler is not { } sampler)return;
        resourceLoadBusy=true;resourceLoadSamplingInstance=sampler;
        try {
            // Include committed/preparing/preview wallpaper, widgets, spectrum and import workers.
            var ownProcesses=SnapshotPerformanceTargets();
            var sample=await Task.Run(()=>sampler.Sample(ownedAuxiliaryProcesses.Include(ownProcesses)));
            if(resourceLoadDisposed||!ReferenceEquals(sampler,resourceLoadSampler))return;
            ApplyResourceLoadSample(sample);
        }catch(Exception ex) {
            if(!resourceLoadDisposed&&ReferenceEquals(sampler,resourceLoadSampler)) {
                AppLog.Warning("ResourceLoadPause","Resource sample unavailable",ex);ApplyResourceLoadSample(new(null,null));
            }
        }finally {
            resourceLoadBusy=false;resourceLoadSamplingInstance=null;
            if(!ReferenceEquals(sampler,resourceLoadSampler))sampler.Dispose();
        }
    }
    private void ApplyResourceLoadSample(ExternalResourceSample sample)
    {
        long now=Environment.TickCount64;
        // Sleep or a delayed dispatcher tick does not count as sustained load.
        if(loadLastSample!=0&&now-loadLastSample>5000)loadHighSince=loadLowSince=loadMissingSince=null;
        loadLastSample=now;
        bool high=sample.Cpu>=LoadPauseThreshold||sample.Gpu>=LoadPauseThreshold;
        if(!high&&(sample.Cpu==null||sample.Gpu==null)) {
            loadHighSince=loadLowSince=null;loadMissingSince??=now;
            // A missing driver counter must not leave wallpaper permanently paused.
            if(now-loadMissingSince.Value>=LoadUnavailableDelay)SetResourceLoadPaused(false);
            SetResourceLoadStatus(LoadText(sample)+UiLanguage.Text(" · 部分数据暂不可用，正在重试"));return;
        }
        loadMissingSince=null;
        if(high) {
            loadLowSince=null;loadHighSince??=now;
            if(now-loadHighSince.Value>=LoadPauseDelay)SetResourceLoadPaused(true);
        } else if(sample.Cpu<=LoadResumeThreshold&&sample.Gpu<=LoadResumeThreshold) {
            loadHighSince=null;loadLowSince??=now;
            if(now-loadLowSince.Value>=LoadResumeDelay)SetResourceLoadPaused(false);
        } else loadHighSince=loadLowSince=null;
        SetResourceLoadStatus(LoadText(sample)+" · "+(resourceLoadPaused?UiLanguage.Text("已自动暂停壁纸"):UiLanguage.Text("监测中")));
    }
    private static string LoadText(ExternalResourceSample sample)=>UiLanguage.Format($"其他程序 CPU：{LoadPercent(sample.Cpu)} · GPU：{LoadPercent(sample.Gpu)}");
    private static string LoadPercent(double? value)=>value is { } percent?$"{percent:F0}%":"—";
}
