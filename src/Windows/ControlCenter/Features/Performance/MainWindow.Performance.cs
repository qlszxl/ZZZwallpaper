using LumaWall.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private readonly DispatcherTimer performanceTimer=new() {Interval=TimeSpan.FromSeconds(1)};
    private readonly HashSet<NativePlayback> performancePlayers=[];
    private ProcessPerformanceSampler? performanceSampler;
    private readonly OwnedAuxiliaryProcesses ownedAuxiliaryProcesses=new();
    private ProcessPerformanceSampler? performanceSamplingInstance;
    private bool performanceActive,performanceBusy,performanceTimerConnected,performanceDisposed;
    private string performanceStatus="";
    private long performanceGeneration;
    private PerformanceTotals? performanceTotals,performanceBaseline;
    private readonly Dictionary<int,(NativePlayback Player,PerformanceFrame Frame)> performanceFrameBaseline=[];

    private sealed record PerformanceTotals(DateTimeOffset At,double? Cpu,long? Memory,long? Private,double? Read,double? Write,bool Complete)
    {
        public double? Gpu {get;init;}
        public long? DedicatedGpu {get;init;}
        public long? SharedGpu {get;init;}
    }
    private sealed record PerformanceWallpaper(NativePlayback Player,string Name,string Detail,VideoEffects? Effects);

    private void StartPerformanceMonitoring()
    {
        if(performanceDisposed||performanceActive)return;
        performanceActive=true;performanceGeneration++;
        performanceSampler=new ProcessPerformanceSampler();performanceTotals=null;
        ResetResourceFooter();
        if(performancePageSelected) {
            PerformanceWallpaperPanel.Children.Clear();UpdatePerformanceBaseline();
            PerformanceBaselineButton.IsEnabled=false;
        }
        SetPerformanceStatus(UiLanguage.Text("正在采样…"));
        if(!performanceTimerConnected) {performanceTimer.Tick+=PerformanceTimer_Tick;performanceTimerConnected=true;}
        performanceTimer.Start();_ = SamplePerformanceAsync();
    }

    private void StopPerformanceMonitoring()
    {
        if(!performanceActive&&performanceSampler==null)return;
        performanceActive=false;performanceGeneration++;performanceTimer.Stop();
        StopDetailedPlayerMonitoring();
        var sampler=performanceSampler;performanceSampler=null;
        // An in-flight worker owns its sampler until it returns; never clear its baseline concurrently.
        if(!ReferenceEquals(sampler,performanceSamplingInstance))sampler?.Dispose();
        SetPerformanceStatus(UiLanguage.Text("监控已暂停"));
    }

    public void DisposePerformanceMonitoring()
    {
        performanceDisposed=true;StopPerformanceMonitoring();
        if(performanceTimerConnected) {performanceTimer.Tick-=PerformanceTimer_Tick;performanceTimerConnected=false;}
    }

    private async void PerformanceTimer_Tick(object? sender,object e)=>await SamplePerformanceAsync();

    private void StopDetailedPlayerMonitoring()
    {
        foreach(var player in performancePlayers)SetPlayerPerformance(player,false);
        performancePlayers.Clear();
    }

    private void RefreshPerformanceDetails()
    {
        // The footer keeps collecting data while this page is hidden. Apply its
        // latest presentation once on entry, without touching hidden controls.
        SetPerformanceStatus(performanceStatus);
        PerformanceBaselineButton.IsEnabled=performanceTotals is {Complete:true};
        RenderPerformanceWallpapers(SnapshotPerformanceWallpapers());
        UpdatePerformanceBaseline();
    }

    private async Task SamplePerformanceAsync()
    {
        if(!performanceActive||performanceBusy||performanceSampler is not { } sampler)return;
        performanceBusy=true;performanceSamplingInstance=sampler;long generation=performanceGeneration;
        try {
            var players=SnapshotPerformanceWallpapers();
            // The persistent footer needs OS counters only. Native frame timings
            // remain limited to the performance page.
            var current=performancePageSelected?players.Select(p=>p.Player).ToHashSet():new HashSet<NativePlayback>();
            foreach(var old in performancePlayers.Where(p=>!current.Contains(p)).ToArray()) {SetPlayerPerformance(old,false);performancePlayers.Remove(old);}
            foreach(var player in current)if(performancePlayers.Add(player))SetPlayerPerformance(player,true);

            var unique=SnapshotPerformanceTargets(players);
            var (samples,gpu)=await Task.Run(()=> {
                var processSamples=sampler.Sample(ownedAuxiliaryProcesses.Include(unique));
                return (processSamples,sampler.GpuTotals);
            });
            if(!performanceActive||generation!=performanceGeneration||performanceDisposed)return;
            UpdatePerformanceSummary(samples,gpu);
            UpdateResourceFooter(samples,gpu.Percent);
            if(performancePageSelected) {
                RenderPerformanceWallpapers(players);
            }
        }
        catch(Exception ex) {AppLog.Error("MainWindow.Performance","Operation failed",ex);
            if(performanceActive&&generation==performanceGeneration) {
                ResetResourceFooter();
                SetPerformanceStatus(UiLanguage.Text("采样失败，正在重试：")+ex.Message);
            }
        }
        finally {
            performanceBusy=false;
            performanceSamplingInstance=null;
            if(!ReferenceEquals(performanceSampler,sampler))sampler.Dispose();
        }
    }

    private PerformanceProcessTarget[] SnapshotPerformanceTargets(IReadOnlyList<PerformanceWallpaper>? players=null)
    {
        players??=SnapshotPerformanceWallpapers();
            var targets=new List<PerformanceProcessTarget> {
                new(Environment.ProcessId,"control-center",UiLanguage.Text("控制中心"),"")
            };
            foreach(var player in players)targets.Add(new(player.Player.ProcessId,"wallpaper:"+player.Player.ProcessId,player.Name,player.Detail));
            if(widgets is {ProcessId:int widgetPid} widgetController) {
                int search=widgetController.State.Widgets.Count(w=>w.Enabled&&w.Kind==WidgetKind.Search);
                int shortcuts=widgetController.State.Widgets.Count(w=>w.Enabled&&w.Kind==WidgetKind.Shortcuts);
                int spectra=widgetController.State.Widgets.Count(w=>w.Enabled&&w.Kind==WidgetKind.Spectrum);
                targets.Add(new(widgetPid,"widgets",UiLanguage.Text("桌面组件"),UiLanguage.Format($"搜索 {search} · 快捷卡片 {shortcuts} · 频谱 {spectra}")));
                if(widgetController.SpectrumProcessId is int spectrumPid)targets.Add(new(spectrumPid,"spectrum",UiLanguage.Text("频谱声音分析"),""));
            }
            foreach(var probe in NativePlayback.ActiveProbes)targets.Add(new(probe.ProcessId,"probe:"+probe.ProcessId,UiLanguage.Text("视频导入 / 首帧读取"),probe.Name));
            if(effectImageClient?.ProcessId is int imagePid)targets.Add(new(imagePid,"image-preview",UiLanguage.Text("效果图片预览"),""));

        targets.AddRange(MediaOperations.ActiveWorkers);
        if(wallpapers?.TaskbarProcessId is int taskbarPid)targets.Add(new(taskbarPid,"taskbar",UiLanguage.Text("任务栏管理"),""));
        return targets.DistinctBy(p=>p.ProcessId).ToArray();
    }

    private static void SetPlayerPerformance(NativePlayback player,bool enabled)
    {
        try {player.SetPerformanceMonitoring(enabled);}catch(ObjectDisposedException) {}catch(IOException) {}
    }

    private PerformanceWallpaper[] SnapshotPerformanceWallpapers()
    {
        if(wallpapers==null)return [];
        var running=wallpapers.Running.ToArray();
        return wallpapers.AllPlayersSnapshot().Select(player=> {
            var match=running.FirstOrDefault(p=>ReferenceEquals(p.Value.Player,player));
            if(match.Value==null)return new PerformanceWallpaper(player,player.SourceName+UiLanguage.Text(" · 准备中"),player.ActualState,null);
            string screen=match.Key.Replace(@"\\.\DISPLAY","");
            string detail=UiLanguage.Format($"屏幕 {screen} · {match.Value.Asset.Width} × {match.Value.Asset.Height} · {player.ActualState}");
            return new PerformanceWallpaper(player,match.Value.Asset.Name,detail,match.Value.Binding.Effects);
        }).ToArray();
    }

    private void UpdatePerformanceSummary(IReadOnlyList<ProcessPerformanceSample> samples,ProcessGpuUsage gpu)
    {
        static double? SumDouble(IEnumerable<double?> values) {var known=values.Where(v=>v.HasValue).Select(v=>v!.Value).ToArray();return known.Length==0?null:known.Sum();}
        static long? SumLong(IEnumerable<long?> values) {var known=values.Where(v=>v.HasValue).Select(v=>v!.Value).ToArray();return known.Length==0?null:known.Sum();}
        bool complete=samples.Count>0&&samples.All(s=>s.CpuPercent.HasValue&&s.WorkingSetBytes.HasValue&&s.ReadBytesPerSecond.HasValue&&s.WriteBytesPerSecond.HasValue);
        var totals=new PerformanceTotals(DateTimeOffset.Now,SumDouble(samples.Select(s=>s.CpuPercent)),SumLong(samples.Select(s=>s.WorkingSetBytes)),SumLong(samples.Select(s=>s.PrivateBytes)),SumDouble(samples.Select(s=>s.ReadBytesPerSecond)),SumDouble(samples.Select(s=>s.WriteBytesPerSecond)),complete) {Gpu=gpu.Percent,DedicatedGpu=gpu.DedicatedBytes,SharedGpu=gpu.SharedBytes};
        performanceTotals=totals;
        SetPerformanceStatus(complete?"":UiLanguage.Text("采样尚不完整"));
        if(performancePageSelected) {
            PerformanceBaselineButton.IsEnabled=complete;
            UpdatePerformanceBaseline();
        }
    }

    private void PerformanceBaseline_Click(object sender,RoutedEventArgs e)
    {
        if(performanceTotals is not {Complete:true} current)return;
        performanceBaseline=current;performanceFrameBaseline.Clear();
        foreach(var player in wallpapers?.AllPlayersSnapshot()??[]) {
            if(player.Performance is { } frame&&(DateTimeOffset.Now-frame.SampledAt).TotalSeconds is >=0 and <=3)
                performanceFrameBaseline[player.ProcessId]=(player,frame);
        }
        UpdatePerformanceBaseline();RenderPerformanceWallpapers(SnapshotPerformanceWallpapers());
    }

    private void UpdatePerformanceBaseline()
    {
        if(performanceBaseline is not { } baseline) {
            PerformanceBaselineText.Text="";PerformanceBaselineText.Visibility=Visibility.Collapsed;UpdatePerformanceNoticeVisibility();return;
        }
        string captured=UiLanguage.Format($"对照 {baseline.At:HH:mm:ss}：CPU {Percent(baseline.Cpu)} · GPU {Percent(baseline.Gpu)} · 专用显存 {Bytes(baseline.DedicatedGpu)} · 共享显存 {Bytes(baseline.SharedGpu)} · 驻留 {Bytes(baseline.Memory)} · 读 / 写 {Rate(baseline.Read)} / {Rate(baseline.Write)}");
        PerformanceBaselineText.Text=performanceTotals is {Complete:true} current
            ?captured+UiLanguage.Format($"\n当前变化：CPU {Signed(current.Cpu-baseline.Cpu,"0.0")} 个百分点 · GPU {Signed(current.Gpu-baseline.Gpu,"0.0")} 个百分点 · 驻留 {Signed((current.Memory-baseline.Memory)/1048576.0,"0.0")} MiB。")
            :captured+UiLanguage.Text("。等待当前完整采样…");
        PerformanceBaselineText.Visibility=Visibility.Visible;UpdatePerformanceNoticeVisibility();
    }

    private void SetPerformanceStatus(string text)
    {
        performanceStatus=text;
        if(!performancePageSelected)return;
        PerformanceStatusText.Text=text;
        PerformanceStatusText.Visibility=string.IsNullOrEmpty(text)?Visibility.Collapsed:Visibility.Visible;
        UpdatePerformanceNoticeVisibility();
    }

    private void UpdatePerformanceNoticeVisibility()=>PerformanceNoticeCard.Visibility=
        PerformanceStatusText.Visibility==Visibility.Visible||PerformanceBaselineText.Visibility==Visibility.Visible?Visibility.Visible:Visibility.Collapsed;

    private void RenderPerformanceWallpapers(IReadOnlyList<PerformanceWallpaper> players)
    {
        PerformanceWallpaperPanel.Children.Clear();
        if(players.Count==0) {PerformanceWallpaperPanel.Children.Add(UtilityCard(UtilityText(UiLanguage.Text("暂无运行中的视频壁纸"))));return;}
        foreach(var wallpaper in players) {
            var content=new StackPanel {Spacing=12};
            content.Children.Add(PerformanceText(wallpaper.Name,16,true));
            content.Children.Add(PerformanceText(wallpaper.Detail+$" · PID {wallpaper.Player.ProcessId}",12,false,.65));
            if(wallpaper.Player.Performance is not { } frame) {
                content.Children.Add(PerformanceText(UiLanguage.Text("等待首组处理数据…"),13,false,.7));
            } else {
                bool stale=(DateTimeOffset.Now-frame.SampledAt).TotalSeconds>3;
                var metrics=new Grid {ColumnSpacing=12};
                AddPerformanceMetric(metrics,UiLanguage.Text("实际提交帧率"),$"{frame.PresentedFps:0.0} fps",0);
                AddPerformanceMetric(metrics,UiLanguage.Text("GPU 平均每帧"),frame.GpuValid?Milliseconds(frame.GpuTotalMs):"—",1);
                AddPerformanceMetric(metrics,UiLanguage.Text("绘制通道"),frame.Passes.ToString(),2);
                content.Children.Add(metrics);
                content.Children.Add(PerformanceText(UiLanguage.Format($"输出 {frame.Width} × {frame.Height} · 视频 {frame.VideoWidth} × {frame.VideoHeight} · {frame.Decoder}")+(frame.Paused?UiLanguage.Text(" · 已暂停"):"")+(stale?UiLanguage.Format($" · 显示 {frame.SampledAt.ToLocalTime():HH:mm:ss} 的最后数据"):""),11,false,.6));
                var columns=new Grid {ColumnSpacing=22};columns.ColumnDefinitions.Add(new());columns.ColumnDefinitions.Add(new());
                var cpu=new StackPanel {Spacing=8};cpu.Children.Add(PerformanceText(UiLanguage.Text("平均阶段耗时 · ms / 次"),13,true));
                AddPerformanceStage(cpu,UiLanguage.Text("视频解码"),frame.DecodeMs);
                AddPerformanceStage(cpu,UiLanguage.Text("音频联动处理"),frame.AudioMs);
                AddPerformanceStage(cpu,UiLanguage.Text("渲染提交"),frame.RenderCpuMs);
                AddPerformanceStage(cpu,UiLanguage.Text("显示等待"),frame.PresentMs);
                columns.Children.Add(cpu);
                var gpu=new StackPanel {Spacing=8};gpu.Children.Add(PerformanceText(UiLanguage.Text("GPU 分阶段 · ms / 帧"),13,true));
                AddPerformanceStage(gpu,UiLanguage.Text("基础画面 / 共享效果"),frame.GpuValid?frame.GpuBaseMs:null);
                if(!frame.GpuValid)gpu.Children.Add(PerformanceText(UiLanguage.Text("GPU 计时不可用"),11,false,.55));
                Grid.SetColumn(gpu,1);columns.Children.Add(gpu);UtilityAdaptiveGrid(columns,2,580);content.Children.Add(new Border {Height=1,Background=PerformanceBrush(48,48,48)});content.Children.Add(columns);
                if(performanceBaseline!=null) {
                    string comparison=UiLanguage.Text("无本进程对照数据");
                    if(performanceFrameBaseline.TryGetValue(wallpaper.Player.ProcessId,out var baseline)&&ReferenceEquals(baseline.Player,wallpaper.Player)) {
                        if(stale||frame.SampledAt<=baseline.Frame.SampledAt)comparison=UiLanguage.Text("等待下一组采样");
                        else {
                            double? gpuDelta=frame.GpuValid&&baseline.Frame.GpuValid?frame.GpuTotalMs-baseline.Frame.GpuTotalMs:null;
                            comparison=UiLanguage.Format($"与 {baseline.Frame.SampledAt.ToLocalTime():HH:mm:ss} 对照相比：GPU {Signed(gpuDelta,"0.00")} ms / 帧 · 解码 {Signed(frame.DecodeMs-baseline.Frame.DecodeMs,"0.00")} ms / 次 · 音频 {Signed(frame.AudioMs-baseline.Frame.AudioMs,"0.00")} ms / 次");
                        }
                    }
                    content.Children.Add(PerformanceText(comparison,12,false,.85));
                }
            }
            if(wallpaper.Effects is { } effects)AddPerformanceEffects(content,effects,wallpaper.Player.Performance);
            PerformanceWallpaperPanel.Children.Add(PerformanceCard(content));
        }
    }

    private static void AddPerformanceStage(StackPanel parent,string title,double? milliseconds)
    {
        var row=new Grid {ColumnSpacing=10};row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new() {Width=GridLength.Auto});
        row.Children.Add(PerformanceText(title,12,false,.75));var number=PerformanceText(Milliseconds(milliseconds),12);Grid.SetColumn(number,1);row.Children.Add(number);parent.Children.Add(row);
    }

    private static void AddPerformanceMetric(Grid grid,string title,string value,int column)
    {
        var panel=new StackPanel {Spacing=5};panel.Children.Add(PerformanceText(title,12,false,.65));panel.Children.Add(PerformanceText(value,16,true));grid.Children.Add(panel);
        if(column==2)UtilityAdaptiveGrid(grid,3,620);
    }

    private static TextBlock PerformanceText(string value,double size,bool bold=false,double opacity=1)=>new() {Text=value,FontSize=Math.Max(12,size),FontFamily=bold?UiFonts.Display:UiFonts.Body,FontWeight=bold?Microsoft.UI.Text.FontWeights.SemiBold:Microsoft.UI.Text.FontWeights.Normal,Opacity=Math.Max(.65,opacity),TextWrapping=TextWrapping.Wrap,TextLineBounds=TextLineBounds.Full,VerticalAlignment=VerticalAlignment.Center};
    private static SolidColorBrush PerformanceBrush(byte r,byte g,byte b)=>new(Windows.UI.Color.FromArgb(255,r,g,b));
    private static ContentControl PerformanceCard(UIElement child)=>UtilityCard(child);
    private static string Percent(double? value)=>value.HasValue?$"{value:0.0}%":"—";
    private static string Milliseconds(double? value)=>value.HasValue?$"{value:0.00} ms":"—";
    private static string Signed(double? value,string format)=>value.HasValue?(value>=0?"+":"")+value.Value.ToString(format):"—";
    private static string Bytes(long? value)=>value.HasValue?value>=1073741824?$"{value/1073741824.0:0.00} GiB":$"{value/1048576.0:0.0} MiB":"—";
    private static string Rate(double? value)=>value.HasValue?value>=1048576?$"{value/1048576.0:0.0} MiB/s":$"{value/1024:0.0} KiB/s":"—";

    private static void AddPerformanceEffects(StackPanel parent,VideoEffects effects,PerformanceFrame? frame)
    {
        parent.Children.Add(new Border {Height=1,Background=PerformanceBrush(48,48,48)});
        parent.Children.Add(PerformanceText(UiLanguage.Text("已启用效果"),13,true));

        // Reuse the host's existing asynchronous timing samples. A shared shader
        // cannot supply truthful per-effect CPU/GPU percentages or memory usage.
        var e=effects.ForRendering();
        var rows=new StackPanel {Spacing=10};
        var shown=new HashSet<string>();
        void Add(string title,string? detail=null) {
            detail ??= UiLanguage.Text("共享计时");
            shown.Add(title);
            var row=new Grid {ColumnSpacing=16,RowSpacing=4};
            row.Children.Add(PerformanceText(title,12,true));
            row.Children.Add(PerformanceText(detail,12,false,.75));
            UtilityAdaptiveGrid(row,2,620);rows.Children.Add(row);
        }
        string Independent(double? value) {
            if(frame is null)return UiLanguage.Text("等待首组 GPU 采样");
            if(frame.Paused)return UiLanguage.Text("已暂停");
            double age=(DateTimeOffset.Now-frame.SampledAt).TotalSeconds;
            if(age<0||age>3)return UiLanguage.Text("采样已过期");
            if(!frame.GpuValid||frame.GpuSamples==0||!value.HasValue)return UiLanguage.Text("等待有效 GPU 采样");
            return UiLanguage.Format($"独立处理 {Milliseconds(value)} / 帧");
        }

        if(e.Intensity>0) {
        if(e.Scanlines&&e.ScanlineStrength>0)Add(UiLanguage.Text("扫描线")+(e.ScanlineBends?UiLanguage.Text("与折线"):""));
        bool glitchActive=e.Glitch&&(e.GlitchCluster
            ?e.ClusterBlend>0&&e.Layers.Take(e.ClusterSmallCount+1).Any(layer=>layer.Active)
            :e.GlitchStrength>0&&e.GlitchCoverage>0);
        if(glitchActive)Add(e.GlitchCluster?UiLanguage.Text("故障集群"):UiLanguage.Text("数字故障"));
        if(e.LargeGlitch&&e.LargeGlitchStrength>0&&e.LargeGlitchBlend>0)Add(UiLanguage.Text("故障突发"));
        if(e.RgbSplit&&e.RgbOffset>0)Add(UiLanguage.Text("RGB 色散"));
        if(e.Dots&&e.DotStrength>0)Add(UiLanguage.Text("动态点阵"));
        if(e.DotRipple&&e.RippleDotStrength>0)Add(UiLanguage.Text("点阵涟漪"));
        if(e.Blur&&e.BlurStrength>0&&e.BlurRadius>0&&(!e.BlurMotion||e.BlurCoverage>0))
            Add(new[]{UiLanguage.Text("高斯模糊"),UiLanguage.Text("方框模糊"),UiLanguage.Text("方向模糊"),UiLanguage.Text("径向模糊"),UiLanguage.Text("旋转模糊")}[e.BlurMethod],Independent(frame?.GpuBlurMs));
        if(e.Distortion&&e.DistortionStrength!=0)Add(UiLanguage.Text("镜头扭曲"));
        if(e.Glow&&e.GlowStrength>0)Add(UiLanguage.Text("辉光"),Independent(frame?.GpuGlowMs));
        if(e.Noise&&e.NoiseStrength>0)Add(e.ColoredNoise?UiLanguage.Text("彩色胶片颗粒"):UiLanguage.Text("胶片颗粒"));
        if(e.Vignette&&e.VignetteStrength>0)Add(UiLanguage.Text("暗角"));
        if(e.Brightness!=0||e.Contrast!=1||e.Saturation!=1)Add(UiLanguage.Text("色彩调整"));
        if(e.AudioReactive&&e.AudioAmount>0&&e.AudioRules.Any(rule=>rule.Enabled))
            Add(e.AudioSource==0?UiLanguage.Text("音频联动（系统播放）"):UiLanguage.Text("音频联动（视频音轨）"),UiLanguage.Text("共享计时"));
        if(e.ZoomTunnel.Enabled&&e.ZoomTunnel.Strength>0)Add(UiLanguage.Text("缩放穿梭"));
        bool rainActive=e.RainRipples.Enabled&&(e.RainRipples.Strength>0||(e.RainRipples.Outline&&e.RainRipples.OutlineStrength>0));
        bool particleActive=e.Particles.Enabled&&((!e.Particles.IsRain&&e.Particles.Opacity>0)||rainActive);
        if(particleActive)Add(e.Particles.IsRain?UiLanguage.Text("粒子 · 雨滴涟漪"):UiLanguage.Text("粒子"));
        if(e.Particles.Enabled&&e.Particles.SubLayer.Enabled&&e.Particles.SubLayer.Opacity>0)Add(UiLanguage.Text("子粒子"));
        if(e.WaterWave.Enabled&&e.WaterWave.Strength>0&&e.WaterWave.Amplitude>0)Add(UiLanguage.Text("水波扭曲"));
        if(e.EdgeOutline.Enabled&&e.EdgeOutline.Strength>0)Add(UiLanguage.Text("边缘描边"));
        if(e.Pixelate.Enabled&&e.Pixelate.Strength>0)Add(UiLanguage.Text("马赛克"));
        if(e.SunPrism.Enabled&&e.SunPrism.Strength>0)Add(UiLanguage.Text("阳光色散"));
        if(e.Transform.Active)Add(UiLanguage.Text("变换"));
        if(e.LightFlare.Enabled&&e.LightFlare.Strength>0&&e.LightFlare.Brightness>0)Add(UiLanguage.Text("光芒"));
        if(e.EffectMasks.Count>0)Add(UiLanguage.Text("引用遮罩 ")+e.EffectMasks.Values.Distinct().Count()+UiLanguage.Text(" 个"));
        }
        // Keep enabled cards visible even when neutral parameters make their
        // current render contribution zero. Audio mappings can drive them away
        // from neutral values without changing their persisted parameters.
        var collection=effects.GetCollection();
        var animated=e.Intensity>0&&e.AudioReactive&&e.AudioAmount>0
            ?e.AudioRules.Where(rule=>rule.Enabled).Select(rule=>AudioParameters.Find(rule.Parameter)).Select(parameter=>parameter.Effect).ToHashSet()
            :new HashSet<EffectKind>();
        foreach(var (kind,title,enabled) in new[] {
            (EffectKind.Scanlines,UiLanguage.Text("扫描线")+(effects.ScanlineBends?UiLanguage.Text("与折线"):""),effects.Scanlines),
            (EffectKind.Glitch,effects.GlitchCluster?UiLanguage.Text("故障集群"):UiLanguage.Text("数字故障"),effects.Glitch),
            (EffectKind.LargeGlitch,UiLanguage.Text("故障突发"),effects.LargeGlitch),
            (EffectKind.RgbSplit,UiLanguage.Text("RGB 色散"),effects.RgbSplit),
            (EffectKind.Dots,UiLanguage.Text("动态点阵"),effects.Dots),
            (EffectKind.DotRipple,UiLanguage.Text("点阵涟漪"),effects.DotRipple),
            (EffectKind.Blur,new[]{UiLanguage.Text("高斯模糊"),UiLanguage.Text("方框模糊"),UiLanguage.Text("方向模糊"),UiLanguage.Text("径向模糊"),UiLanguage.Text("旋转模糊")}[effects.BlurMethod],effects.Blur),
            (EffectKind.Distortion,UiLanguage.Text("镜头扭曲"),effects.Distortion),
            (EffectKind.Glow,UiLanguage.Text("辉光"),effects.Glow),
            (EffectKind.Noise,effects.ColoredNoise?UiLanguage.Text("彩色胶片颗粒"):UiLanguage.Text("胶片颗粒"),effects.Noise),
            (EffectKind.Vignette,UiLanguage.Text("暗角"),effects.Vignette),
            (EffectKind.Color,UiLanguage.Text("色彩调整"),collection.ColorEnabled),
            (EffectKind.AudioReactive,effects.AudioSource==0?UiLanguage.Text("音频联动（系统播放）"):UiLanguage.Text("音频联动（视频音轨）"),effects.AudioReactive),
            (EffectKind.ZoomTunnel,UiLanguage.Text("缩放穿梭"),effects.ZoomTunnel.Enabled),
            (EffectKind.Particles,effects.Particles.IsRain?UiLanguage.Text("粒子 · 雨滴涟漪"):UiLanguage.Text("粒子"),effects.Particles.Enabled),
            (EffectKind.Particles,UiLanguage.Text("子粒子"),effects.Particles.Enabled&&effects.Particles.SubLayer.Enabled),
            (EffectKind.WaterWave,UiLanguage.Text("水波扭曲"),effects.WaterWave.Enabled),
            (EffectKind.EdgeOutline,UiLanguage.Text("边缘描边"),effects.EdgeOutline.Enabled),
            (EffectKind.Pixelate,UiLanguage.Text("马赛克"),effects.Pixelate.Enabled),
            (EffectKind.SunPrism,UiLanguage.Text("阳光色散"),effects.SunPrism.Enabled),
            (EffectKind.LightFlare,UiLanguage.Text("光芒"),effects.LightFlare.Enabled),
            (EffectKind.Transform,UiLanguage.Text("变换"),effects.Transform.Enabled)
        }) {
            if(!enabled||(collection.Added&kind)==0||shown.Contains(title))continue;
            string detail=UiLanguage.Text("当前未参与渲染");
            if(animated.Contains(kind))detail=kind switch {
                EffectKind.Glow=>Independent(frame?.GpuGlowMs),
                EffectKind.Blur=>Independent(frame?.GpuBlurMs),
                _=>UiLanguage.Text("音频联动 · 共享计时")
            };
            Add(title,detail);
        }
        if((collection.Added&EffectKind.HideTaskbar)!=0&&collection.HideTaskbarEnabled)
            Add(UiLanguage.Text("任务栏设置"),effects.TaskbarAppearance is { } taskbar?taskbar switch {TaskbarStyle.Transparent=>UiLanguage.Text("透明"),TaskbarStyle.Frosted=>UiLanguage.Text("磨砂"),_=>UiLanguage.Text("隐藏")}:UiLanguage.Text("强度为零"));
        if((collection.Added&EffectKind.MaskLibrary)!=0&&effects.EffectMasks.Count==0)
            Add(UiLanguage.Text("遮罩库"),UiLanguage.Text("未引用"));
        if(rows.Children.Count==0)rows.Children.Add(PerformanceText(UiLanguage.Text("暂无已启用效果"),12,false,.7));
        parent.Children.Add(rows);
    }
}
