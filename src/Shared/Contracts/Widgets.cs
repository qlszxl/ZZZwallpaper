using System.Text.Json;

namespace LumaWall.Contracts;

public enum WidgetKind { Search, Shortcuts, Performance, Spectrum, Text, Sticker, Mask, Clock, Countdown, FocusTimer, Warehouse, Extension }
public enum ShortcutKind { Website, Folder, Command }
public enum WidgetGroupFlow { Horizontal, Vertical, Grid }
public enum WidgetAlignment { Start, Center, End }
[Flags]
public enum PerformanceMetrics { Cpu=1,Memory=2,Gpu=4,GpuMemory=8,DiskRead=16,DiskWrite=32,NetworkReceive=64,NetworkSend=128,CpuClock=256,CpuTemperature=512,CpuPower=1024,GpuTemperature=2048,GpuPower=4096,GpuClock=8192,GpuMemoryClock=16384,GpuFan=32768,GpuMemoryTotal=65536,MemoryAvailable=131072,MemoryCommitted=262144,DiskBusy=524288,DiskReadLatency=1048576,DiskWriteLatency=2097152,NetworkTotal=4194304,ProcessCount=8388608,ThreadCount=16777216,HandleCount=33554432,SystemUptime=67108864 }
public static class PerformanceFields
{
    public const PerformanceMetrics Default=PerformanceMetrics.Cpu|PerformanceMetrics.Memory;
    public const PerformanceMetrics All=(PerformanceMetrics)134217727;
    public static PerformanceMetrics[] Selected(PerformanceMetrics metrics)=>Enum.GetValues<PerformanceMetrics>().Where(m=>(metrics&m)!=0).ToArray();
    public static string Label(PerformanceMetrics metric)=>metric switch {
        PerformanceMetrics.Cpu=>"CPU",
        PerformanceMetrics.Memory=>UiLanguage.Text("内存"),
        PerformanceMetrics.Gpu=>"GPU",
        PerformanceMetrics.GpuMemory=>UiLanguage.Text("显存"),
        PerformanceMetrics.DiskRead=>UiLanguage.Text("磁盘读取"),
        PerformanceMetrics.DiskWrite=>UiLanguage.Text("磁盘写入"),
        PerformanceMetrics.NetworkReceive=>UiLanguage.Text("网络下行"),
        PerformanceMetrics.NetworkSend=>UiLanguage.Text("网络上行"),
        PerformanceMetrics.CpuClock=>UiLanguage.Text("CPU 频率"),
        PerformanceMetrics.CpuTemperature=>UiLanguage.Text("CPU 温度"),
        PerformanceMetrics.CpuPower=>UiLanguage.Text("CPU 功耗"),
        PerformanceMetrics.GpuTemperature=>UiLanguage.Text("GPU 温度"),
        PerformanceMetrics.GpuPower=>UiLanguage.Text("GPU 功耗"),
        PerformanceMetrics.GpuClock=>UiLanguage.Text("GPU 核心频率"),
        PerformanceMetrics.GpuMemoryClock=>UiLanguage.Text("显存频率"),
        PerformanceMetrics.GpuFan=>UiLanguage.Text("GPU 风扇"),
        PerformanceMetrics.GpuMemoryTotal=>UiLanguage.Text("显存容量"),
        PerformanceMetrics.MemoryAvailable=>UiLanguage.Text("可用内存"),
        PerformanceMetrics.MemoryCommitted=>UiLanguage.Text("已提交内存"),
        PerformanceMetrics.DiskBusy=>UiLanguage.Text("磁盘活动"),
        PerformanceMetrics.DiskReadLatency=>UiLanguage.Text("磁盘读取延迟"),
        PerformanceMetrics.DiskWriteLatency=>UiLanguage.Text("磁盘写入延迟"),
        PerformanceMetrics.NetworkTotal=>UiLanguage.Text("网络总速率"),
        PerformanceMetrics.ProcessCount=>UiLanguage.Text("进程数"),
        PerformanceMetrics.ThreadCount=>UiLanguage.Text("线程数"),
        PerformanceMetrics.HandleCount=>UiLanguage.Text("句柄数"),
        PerformanceMetrics.SystemUptime=>UiLanguage.Text("系统运行时间"),
        _=>UiLanguage.Text("未知")
    };
    public static string Category(PerformanceMetrics metric)=>metric switch {
        PerformanceMetrics.Cpu or PerformanceMetrics.CpuClock or PerformanceMetrics.CpuTemperature or PerformanceMetrics.CpuPower=>"CPU",
        PerformanceMetrics.Gpu or PerformanceMetrics.GpuMemory or PerformanceMetrics.GpuTemperature or PerformanceMetrics.GpuPower or PerformanceMetrics.GpuClock or PerformanceMetrics.GpuMemoryClock or PerformanceMetrics.GpuFan or PerformanceMetrics.GpuMemoryTotal=>"GPU",
        PerformanceMetrics.Memory or PerformanceMetrics.MemoryAvailable or PerformanceMetrics.MemoryCommitted=>UiLanguage.Text("内存"),
        PerformanceMetrics.DiskRead or PerformanceMetrics.DiskWrite or PerformanceMetrics.DiskBusy or PerformanceMetrics.DiskReadLatency or PerformanceMetrics.DiskWriteLatency=>UiLanguage.Text("磁盘"),
        PerformanceMetrics.NetworkReceive or PerformanceMetrics.NetworkSend or PerformanceMetrics.NetworkTotal=>UiLanguage.Text("网络"),
        PerformanceMetrics.ProcessCount or PerformanceMetrics.ThreadCount or PerformanceMetrics.HandleCount or PerformanceMetrics.SystemUptime=>UiLanguage.Text("系统"),
        _=>UiLanguage.Text("系统")
    };
}
public sealed record WidgetLayoutGroup
{
    public string Id {get;init;}=Guid.NewGuid().ToString("N");
    public string Name {get;init;}="组件组";
    public string Screen {get;init;}="";
    public double X {get;init;}=.5;
    public double Y {get;init;}=.5;
    public WidgetGroupFlow Flow {get;init;}=WidgetGroupFlow.Horizontal;
    public WidgetAlignment HorizontalAlignment {get;init;}=WidgetAlignment.Center;
    public WidgetAlignment VerticalAlignment {get;init;}=WidgetAlignment.Center;
    public double Gap {get;init;}=12;
    public int Columns {get;init;}=3;
    public bool Locked {get;init;}
}
public sealed record WidgetInstance
{
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? PackageWidgetId {get;init;}
    public string Id {get;init;}=Guid.NewGuid().ToString("N");
    public WidgetKind Kind {get;init;}
    public bool Enabled {get;init;}=true;
    public string Screen {get;init;}="";
    public double X {get;init;}=.62;
    public double Y {get;init;}=.1;
    public double Width {get;init;}=.28;
    public double Height {get;init;}=.2;
    public double Opacity {get;init;}=.94;
    public string Group {get;init;}="common";
    public string? ShortcutId {get;init;}
    public bool IconOnly {get;init;}
    public string? Icon {get;init;}
    public int RefreshSeconds {get;init;}=1;
    public string? LayoutGroupId {get;init;}
    public int LayoutOrder {get;init;}
    public int Layer {get;init;}
    public bool AlwaysOnTop {get;init;}
    public bool Locked {get;init;}
    public bool InteractionLocked {get;init;}
    public PerformanceMetrics Metrics {get;init;}=PerformanceFields.Default;
    public PerformanceWidgetOptions Performance {get;init;}=new();
    public SearchWidgetOptions Search {get;init;}=new();
    public VisualWidgetOptions Visual {get;init;}=new();
    public SpectrumOptions Spectrum {get;init;}=new();
    public ClockWidgetOptions Clock {get;init;}=new();
    public CountdownWidgetOptions Countdown {get;init;}=new();
    public FocusWidgetOptions Focus {get;init;}=new();
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public GridWidgetOptions? GridOptions {get;init;}
    // Read the retired per-widget field only when migrating the old numeric kind.
    [System.Text.Json.Serialization.JsonPropertyName("Warehouse")]
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public GridWidgetOptions? OldGrid {get=>null;init{if(Kind==WidgetKind.Warehouse)GridOptions=value;}}
}
public sealed record ShortcutGroup(string Id,string Name,int Order);
// Url is retained as the serialized target field for existing saved cards.
public sealed record Shortcut(string Id,string Name,string Url,string Group,int Order,string? Icon=null,ShortcutKind Kind=ShortcutKind.Website,string? WorkingDirectory=null,bool ShowCommandWindow=false)
{
    public Shortcut ContentKey()=>this with {Id="",Group="",Order=0};
}
public sealed record WidgetConfiguration
{
    public int Version {get;init;}=18;
    public WidgetInstance[] Widgets {get;init;}=[];
    public WidgetLayoutGroup[] LayoutGroups {get;init;}=[];
    public ShortcutGroup[] Groups {get;init;}=[new("common","常用",0)];
    public Shortcut[] Shortcuts {get;init;}=[];
    public bool IsGroupDragLocked(string id)=>LayoutGroups.Any(g=>g.Id==id&&g.Locked)||Widgets.Any(w=>w.LayoutGroupId==id&&w.Locked);
    public bool IsEditingLocked(WidgetInstance widget)=>widget.Locked||LayoutGroups.Any(g=>g.Id==widget.LayoutGroupId&&g.Locked);
    public bool IsInteractionLocked(WidgetInstance widget)=>widget.InteractionLocked;
    public bool IsDragLocked(WidgetInstance widget)=>widget.Locked||(widget.LayoutGroupId is { } id&&IsGroupDragLocked(id));
    public WidgetConfiguration Validate()
    {
        static void Require(bool value,string reason) {if(!value)throw new ArgumentException(reason);}
        static bool Name(string value,int max)=>!string.IsNullOrWhiteSpace(value)&&value.Length<=max&&!value.Any(char.IsControl);
        static void Icon(string? value) {
            if(value==null)return;
            Require(value.Length<=65536,"组件图标过大");var bytes=Convert.FromBase64String(value);
            Require(bytes.Length>=24&&bytes.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}),"图标须为本地 PNG");
            int width=System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16,4)),height=System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20,4));
            Require(width is >0 and <=128&&height is >0 and <=128,"图标像素尺寸过大");
        }
        Require(Version==18,"不支持的组件配置版本");
        if(Widgets==null||Groups==null||Shortcuts==null||LayoutGroups==null)throw new ArgumentException("组件配置不完整");
        
        Require(LayoutGroups.All(g=>g!=null&&Name(g.Id,64)&&Name(g.Name,40))&&LayoutGroups.Select(g=>g.Id).Distinct().Count()==LayoutGroups.Length,"组件组名称或标识无效");
        foreach(var g in LayoutGroups) {
            Require(g.Screen!=null&&g.Screen.Length<=128&&new[]{g.X,g.Y,g.Gap}.All(double.IsFinite),"组件组位置无效");
            Require(g.X>=0&&g.X<=1&&g.Y>=0&&g.Y<=1&&g.Gap>=0&&g.Gap<=80&&g.Columns is >=1 and <=8,"组件组位置或排列超出范围");
            Require(Enum.IsDefined(g.Flow)&&Enum.IsDefined(g.HorizontalAlignment)&&Enum.IsDefined(g.VerticalAlignment),"组件组对齐无效");
        }
        Require(Groups.Length>=1,"至少保留一个快捷分组");
        Require(Groups.Any(g=>g is {Id:"common"}),"缺少常用分组");
        Require(Groups.All(g=>g!=null&&Name(g.Id,64)&&Name(g.Name,40))&&Groups.Select(g=>g.Id).Distinct().Count()==Groups.Length,"分组无效或重复");
        Require(Widgets.All(w=>w!=null&&Name(w.Id,64)&&Enum.IsDefined(w.Kind))&&Widgets.Select(w=>w.Id).Distinct().Count()==Widgets.Length,"组件标识无效或重复");
        Require(Widgets.Sum(w=>(long)(w.Visual?.Image?.Length??0))<=10_000_000,"贴纸图片总量超过限制，请压缩图片后重试");
        foreach(var w in Widgets) {
            if(w.Kind==WidgetKind.Extension)Require(Name(w.PackageWidgetId??"",129)&&w.PackageWidgetId!.Contains('/'),"扩展组件标识无效");
            if(w.Performance==null)throw new ArgumentException("缺少性能排列设置");w.Performance.Validate();
            if(w.Search==null)throw new ArgumentException("缺少搜索框设置");w.Search.Validate();
            if(w.Visual==null)throw new ArgumentException("缺少组件外观设置");w.Visual.Validate();
            if(w.Clock==null||w.Countdown==null||w.Focus==null)throw new ArgumentException("缺少时间组件设置");w.Clock.Validate();w.Countdown.Validate();w.Focus.Validate();
            if(w.Spectrum==null)throw new ArgumentException("缺少频谱设置");w.Spectrum.Validate();
            if(w.GridOptions is {} grid)Require(grid.Width is >=1 and <=16384&&grid.Rows is >=1 and <=128,"扩展组件尺寸超出范围");
            Require(new[]{w.X,w.Y,w.Width,w.Height,w.Opacity}.All(double.IsFinite),"组件位置或外观无效");
            Require(w.X>=0&&w.X<=1&&w.Y>=0&&w.Y<=1&&w.Width>=.05&&w.Width<=1&&w.Height>=.03&&w.Height<=1&&w.Opacity>=.35&&w.Opacity<=1,"组件位置或外观超出范围");
            Require(w.Screen!=null&&w.Screen.Length<=128&&Groups.Any(g=>g.Id==w.Group),"组件屏幕或分组无效");
            Require(w.ShortcutId==null||Name(w.ShortcutId,64),"快捷卡片引用无效");
            Require(w.RefreshSeconds is 1 or 2 or 5,"刷新间隔须为 1、2 或 5 秒");Icon(w.Icon);
            Require(w.LayoutGroupId==null||LayoutGroups.Any(g=>g.Id==w.LayoutGroupId),"组件所属组不存在");
            Require(w.LayoutOrder is >=0 and <=10000,"组件顺序无效");
            Require(w.Layer is >=-999 and <=999,"组件层级须为 -999–999");
            Require(w.Metrics!=0&&(w.Metrics&~PerformanceFields.All)==0,"请至少选择一项有效的性能指标");
        }
        Require(Shortcuts.All(c=>c!=null&&Name(c.Id,64)&&Name(c.Name,40))&&Shortcuts.Select(c=>c.Id).Distinct().Count()==Shortcuts.Length,"卡片名称或标识无效");
        foreach(var c in Shortcuts) {
            _=ShortcutTargets.Normalize(c.Kind,c.Url);Require(Groups.Any(g=>g.Id==c.Group),"卡片分组不存在");
            if(!string.IsNullOrEmpty(c.WorkingDirectory))_=ShortcutTargets.Folder(c.WorkingDirectory);
            Icon(c.Icon);
        }
        return this;
    }
    public const string LegacyMaterialsWidget="warehouse-particles/Warehouse";
    public WidgetConfiguration WithoutUnusedShortcuts()
    {
        if (Shortcuts.Length == 0) return this;
        var used = Widgets.Where(w => w.Kind == WidgetKind.Shortcuts && w.ShortcutId != null)
            .Select(w => w.ShortcutId!).ToHashSet(StringComparer.Ordinal);
        if (Shortcuts.All(c => used.Contains(c.Id))) return this;
        return this with { Shortcuts = Shortcuts.Where(c => used.Contains(c.Id)).ToArray() };
    }

    public WidgetConfiguration ReconcileLocalState() => ReconcileMaterials().WithoutUnusedShortcuts();

    public WidgetConfiguration ReconcileMaterials(){
        if(!Widgets.Any(w=>w.Kind==WidgetKind.Warehouse||w.PackageWidgetId==LegacyMaterialsWidget))return this;
        bool installed=FeaturePacks.Widgets.Any(p=>p.Key==LegacyMaterialsWidget)||LegacyMaterials.Current.IsInstalled;
        if(installed&&!Widgets.Any(w=>w.Kind==WidgetKind.Warehouse))return this;
        return this with {Widgets=Widgets.Where(w=>installed||(w.Kind!=WidgetKind.Warehouse&&w.PackageWidgetId!=LegacyMaterialsWidget))
            .Select(w=>w.Kind==WidgetKind.Warehouse?w with {Kind=WidgetKind.Extension,PackageWidgetId=LegacyMaterialsWidget}:w).ToArray()};
    }
    public string ToJson()=>JsonSerializer.Serialize(Validate());
    public static WidgetConfiguration FromJson(string? text)
    {
        if(string.IsNullOrWhiteSpace(text))return new();
        var state=JsonSerializer.Deserialize<WidgetConfiguration>(text)??throw new ArgumentException("组件配置为空");
        int sourceVersion=state.Version;
        if(state.Version==1) {
            state=(state with {Version=18,LayoutGroups=[],Widgets=state.Widgets.Select(w=>w with {LayoutGroupId=null,LayoutOrder=0,Locked=false,Metrics=PerformanceFields.Default}).ToArray()}).Validate();
            state=state with {Widgets=state.Widgets.Select(w=>w.Kind==WidgetKind.Shortcuts?w with {ShortcutId=state.Shortcuts.Where(c=>c.Group==w.Group).OrderBy(c=>c.Order).FirstOrDefault()?.Id}:w).ToArray()};
        }
        if(state.Version is 2 or 3)state=state with {Version=5,LayoutGroups=[],Widgets=state.Widgets.Select(w=>w with {LayoutGroupId=null,LayoutOrder=0,Locked=false,Metrics=PerformanceFields.Default}).ToArray()};
        if(state.Version==4)state=state with {Version=5,LayoutGroups=state.LayoutGroups.Select(g=>g with {Locked=false}).ToArray(),Widgets=state.Widgets.Select(w=>w with {Locked=false,Metrics=PerformanceFields.Default}).ToArray()};
        if(state.Version==5)state=state with {Version=18};
        if(state.Version==6)state=state with {Version=18};
        if(state.Version==7)state=state with {Version=18};
        if(state.Version==8)state=state with {Version=18};
        if(state.Version==9)state=state with {Version=18};
        if(state.Version==10)state=state with {Version=18,Widgets=state.Widgets.Select(w=>w with {Spectrum=w.Spectrum with {SubSpectra=w.Spectrum.SubSpectra.Select(layer=>layer with {Width=w.Spectrum.Width,Height=w.Spectrum.Height}).ToArray()}}).ToArray()};
        if(state.Version is 11 or 12 or 13)state=state with {Version=18};
        if(state.Version is 14 or 15 or 16 or 17)state=state with {Version=18};
        if(sourceVersion is >=1 and <=13) {
            static SpectrumOptions UpgradeWeighting(SpectrumOptions layer)=>layer with {
                CenterEmphasis=false,Weighting=new SpectrumWeighting {Enabled=layer.CenterEmphasis},
                SubSpectra=layer.SubSpectra.Select(UpgradeWeighting).ToArray()
            };
            state=state with {Widgets=state.Widgets.Select(w=>w with {Spectrum=UpgradeWeighting(w.Spectrum)}).ToArray()};
        }
        return state.Validate();
    }
}
public static class ShortcutTargets
{
    public static string Label(ShortcutKind kind)=>kind switch {ShortcutKind.Website=>"网址",ShortcutKind.Folder=>"文件夹",ShortcutKind.Command=>"命令",_=>"未知"};
    public static string Normalize(ShortcutKind kind,string text)
    {
        if(!Enum.IsDefined(kind)||string.IsNullOrWhiteSpace(text)||text.Length>4096||text.Any(char.IsControl))throw new ArgumentException("请输入有效的卡片目标（最多 4096 个字符，单行）");
        return kind switch {ShortcutKind.Website=>NavigationLinks.Website(text).AbsoluteUri,ShortcutKind.Folder=>Folder(text),_=>text.Trim()};
    }
    public static string Folder(string text)
    {
        if(string.IsNullOrWhiteSpace(text)||text.Length>4096||text.Any(char.IsControl))throw new ArgumentException("请输入完整文件夹路径");
        string path=Environment.ExpandEnvironmentVariables(text.Trim());
        if(!Path.IsPathFullyQualified(path)||path.IndexOfAny(Path.GetInvalidPathChars())>=0)throw new ArgumentException("请填写完整文件夹路径，例如 C:\\Users 或 \\\\server\\share");
        return Path.GetFullPath(path);
    }
    public static string Describe(Shortcut card)=>Label(card.Kind)+" · "+(card.Kind==ShortcutKind.Website?NavigationLinks.Website(card.Url).IdnHost:card.Url);
}
public static class NavigationLinks
{
    public static Uri Website(string text)
    {
        if(string.IsNullOrWhiteSpace(text)||text.Length>4096||text.Any(char.IsControl))throw new ArgumentException("请输入完整的 HTTP 或 HTTPS 网址");
        if(!Uri.TryCreate(text.Trim(),UriKind.Absolute,out var uri)||(uri.Scheme!=Uri.UriSchemeHttp&&uri.Scheme!=Uri.UriSchemeHttps)||string.IsNullOrWhiteSpace(uri.Host)||!string.IsNullOrEmpty(uri.UserInfo))throw new ArgumentException("仅支持不含账号密码的 HTTP / HTTPS 网址");
        return uri;
    }
    public static Uri? Search(string text,SearchWidgetOptions? options=null)
    {
        string keyword=text.Trim();if(keyword.Length==0)return null;
        if(keyword.EnumerateRunes().Count()>500)throw new ArgumentException("搜索词最多 500 个字符");
        options??=new();options.Validate();
        return Website(options.Template.Replace("{query}",Uri.EscapeDataString(keyword),StringComparison.Ordinal));
    }
}
// Internal host transport only; persisted configuration remains version 18.
public sealed record WidgetDelta
{
    public WidgetInstance[] Widgets {get;init;}=[];
    public string[] WidgetOrder {get;init;}=[];
    public WidgetLayoutGroup[]? LayoutGroups {get;init;}
    public ShortcutGroup[]? Groups {get;init;}
    public Shortcut[]? Shortcuts {get;init;}
    public WidgetConfiguration Apply(WidgetConfiguration previous)
    {
        var values=previous.Widgets.ToDictionary(w=>w.Id);
        foreach(var widget in Widgets)values[widget.Id]=widget;
        return (previous with {Widgets=WidgetOrder.Select(id=>values.TryGetValue(id,out var widget)?widget:throw new InvalidDataException("组件增量缺少基准配置")).ToArray(),
            LayoutGroups=LayoutGroups??previous.LayoutGroups,Groups=Groups??previous.Groups,Shortcuts=Shortcuts??previous.Shortcuts}).Validate();
    }
    public static WidgetDelta Between(WidgetConfiguration previous,WidgetConfiguration next)
    {
        var old=previous.Widgets.ToDictionary(w=>w.Id);
        return new() {Widgets=next.Widgets.Where(w=>!old.TryGetValue(w.Id,out var value)||value!=w).ToArray(),WidgetOrder=next.Widgets.Select(w=>w.Id).ToArray(),
            LayoutGroups=previous.LayoutGroups.SequenceEqual(next.LayoutGroups)?null:next.LayoutGroups,
            Groups=previous.Groups.SequenceEqual(next.Groups)?null:next.Groups,Shortcuts=previous.Shortcuts.SequenceEqual(next.Shortcuts)?null:next.Shortcuts};
    }
}
public sealed record WidgetMessage
{
    public string? Revision {get;init;}
    public JsonElement? Telemetry {get;init;}
    public AppearanceOptions? Appearance {get;init;}
    public int? ProcessId {get;init;}
    public string Type {get;init;}="sync";
    public WidgetConfiguration? Configuration {get;init;}
    public WidgetDelta? Delta {get;init;}
    public WidgetInstance? Layout {get;init;}
    public WidgetLayoutGroup? GroupLayout {get;init;}
    public string? Id {get;init;}
    public bool? Enabled {get;init;}
    public bool? Locked {get;init;}
    public bool? InteractionLocked {get;init;}
    public string? TimerAction {get;init;}
    public string? RequestId {get;init;}
    public string? TargetUri {get;init;}
    public string? ShortcutId {get;init;}
    public string? Error {get;init;}
    public bool Succeeded {get;init;}
    public string? FaultStage {get;init;}
    public string? FaultType {get;init;}
    public int? FaultHResult {get;init;}
}
