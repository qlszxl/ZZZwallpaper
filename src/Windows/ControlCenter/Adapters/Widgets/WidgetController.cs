using System.Diagnostics;
using System.Text.Json;
using LumaWall.Contracts;
using LumaWall.WidgetSupport;

namespace LumaWall.ControlCenter;

public sealed class WidgetController : IDisposable
{
    private readonly LibraryStore store;
    private readonly LumaWall.Application.WidgetConfigurationService configuration;
    private readonly Action<Action> dispatch;
    private Session? session;
    private bool disposed;
    private readonly DesktopTargetLauncher targetLauncher=new();
    private string? loadError;
    private string? legacyState;
    public string? AppliedRevision {get;private set;}
    public string ConfigurationRevision=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(PresentationState().ToJson())));
    private string WidgetScreen(WidgetInstance widget)=>WidgetPlacement.Resolve(widget.LayoutGroupId is { } id?State.LayoutGroups.FirstOrDefault(group=>group.Id==id)?.Screen??widget.Screen:widget.Screen).DeviceName;
    private bool IsPresented(WidgetInstance widget)=>widget.Enabled&&ExpansionCatalog.WidgetAvailable(widget)&&RuntimeSettingsReader.WidgetsEnabled(store,WidgetScreen(widget));
    private WidgetConfiguration PresentationState()
    {
        var enabledScreens=new Dictionary<string,bool>(StringComparer.OrdinalIgnoreCase);
        bool Enabled(WidgetInstance widget) {
            if(!ExpansionCatalog.WidgetAvailable(widget))return false;
            var screen=WidgetScreen(widget);
            if(!enabledScreens.TryGetValue(screen,out var enabled))enabledScreens[screen]=enabled=RuntimeSettingsReader.WidgetsEnabled(store,screen);
            return enabled;
        }
        var disabled=State.Widgets.Where(widget=>widget.Enabled&&!Enabled(widget)).Select(widget=>widget.Id).ToHashSet();
        return disabled.Count==0?State:State with {Widgets=State.Widgets.Select(widget=>disabled.Contains(widget.Id)?widget with {Enabled=false}:widget).ToArray()};
    }
    private readonly Dictionary<string,TaskCompletionSource<WidgetMessage>> inspections=new();
    public async Task<WidgetMessage?> Inspect(string id,CancellationToken token) {
        if(session==null)return null;string request=Guid.NewGuid().ToString("N");var result=new TaskCompletionSource<WidgetMessage>(TaskCreationOptions.RunContinuationsAsynchronously);inspections[request]=result;
        try {if(!session.Messages.TryWrite(new WidgetMessage {Type="inspect",Id=id,RequestId=request}))return null;return await result.Task.WaitAsync(TimeSpan.FromSeconds(2),token);}
        catch(TimeoutException) {return null;}finally {inspections.Remove(request);}
    }
    private readonly System.Threading.Timer liveTimer,saveTimer,focusTimer;
    private bool pendingLiveSync,pendingSave;
    private OperationContext? pendingWidgetOperation,lastWidgetOperation;
    public string SaveStatus {get;private set;}=UiLanguage.Text("更改后实时生效并自动保存");
    public WidgetConfiguration State {get;private set;}=new();
    public string Status {get;private set;}=UiLanguage.Text("尚未添加组件");
    public int? SpectrumProcessId=>ProcessId!=null?session?.SpectrumProcessId:null;
    public int? ProcessId {get {try {return session is { } active&&!active.Process.HasExited?active.Process.Id:null;}catch {return null;}}}
    public event Action? Changed;
    public event Action<string>? Error;
    public event Action<string?>? ManageRequested;
    private sealed class Session(Process process)
    {
        public Process Process {get;}=process;
        public WidgetMessageQueue Messages {get;}=new();
        public string? LastFault {get;set;}
        public int? SpectrumProcessId {get;set;}
        public CancellationTokenSource SendingCancellation {get;}=new();
        public Task Sending {get;set;}=Task.CompletedTask;
        public Task Reading {get;set;}=Task.CompletedTask;
        public Task Errors {get;set;}=Task.CompletedTask;
    }
    public WidgetController(LibraryStore store,Action<Action> dispatch,ControlCenterServices modules)
    {
        this.store=store;this.dispatch=dispatch;configuration=modules.Widgets;
        liveTimer=new(_=>dispatch(()=> {if(disposed||!pendingLiveSync)return;pendingLiveSync=false;using var trace=OperationTrace.Enter(pendingWidgetOperation);Refresh();}),null,Timeout.Infinite,Timeout.Infinite);
        saveTimer=new(_=>dispatch(FlushPendingSave),null,Timeout.Infinite,Timeout.Infinite);
        focusTimer=new(_=>dispatch(AdvanceFocusTimers),null,1000,1000);
        try {
            FeaturePacks.CollectUnused();store.ReconcileWidgetSettings();
            var loaded=configuration.Read();State=loaded.State;legacyState=loaded.LegacySnapshot;
        }
        catch(Exception ex) {AppLog.Error("WidgetController","Operation failed",ex);loadError=UiLanguage.Text("组件配置读取失败，原数据已保留：")+ex.Message;Status=loadError;}
    }
    internal void RefreshInstalledWidgets(){
        store.ReconcileWidgetSettings();
        var next=State.ReconcileLocalState();
        if(!ReferenceEquals(next,State))AcceptImportedState(next);
    }
    public void Save(WidgetConfiguration state)
    {
        if(disposed)return;if(loadError!=null)throw new IOException(loadError);
        state=state.ReconcileLocalState();Persist(state);State=state;pendingSave=pendingLiveSync=false;
        saveTimer.Change(Timeout.Infinite,Timeout.Infinite);liveTimer.Change(Timeout.Infinite,Timeout.Infinite);
        SaveStatus=UiLanguage.Text("已自动保存");Refresh();Changed?.Invoke();
    }
    internal void ReloadHistoryState()
    {
        var state=configuration.Read().State;
        AcceptImportedState(state);SaveStatus=UiLanguage.Text("已恢复组件配置");Changed?.Invoke();
    }
    internal void AcceptImportedState(WidgetConfiguration state)
    {
        ObjectDisposedException.ThrowIf(disposed,this);state=state.ReconcileLocalState().Validate();
        saveTimer.Change(Timeout.Infinite,Timeout.Infinite);liveTimer.Change(Timeout.Infinite,Timeout.Infinite);
        pendingSave=pendingLiveSync=false;loadError=legacyState=null;
        Stop();State=state;SaveStatus=UiLanguage.Text("分享配置已保存");Refresh(true);Changed?.Invoke();
    }
    private void Persist(WidgetConfiguration state)
    {
        var operation=OperationTrace.Current;OperationTrace.Record(operation,"widget_save","started");
        configuration.Save(state,legacyState);legacyState=null;
        OperationTrace.Record(operation,"widget_save","committed");
    }
    // Keep the current model immediate; coalesce desktop updates to 30 fps and
    // persist after a short pause, without delaying the visible adjustment.
    public void UpdateLive(WidgetConfiguration state)
    {
        if(disposed)return;if(loadError!=null)throw new IOException(loadError);
        pendingWidgetOperation=OperationTrace.Current??pendingWidgetOperation;
        State=state.Validate().WithoutUnusedShortcuts();pendingSave=true;SaveStatus=UiLanguage.Text("实时生效 · 正在自动保存");
        if(!pendingLiveSync) {pendingLiveSync=true;liveTimer.Change(32,Timeout.Infinite);}
        saveTimer.Change(250,Timeout.Infinite);Changed?.Invoke();
    }
    public bool HasPendingSave=>pendingSave;
    public void FlushPendingSave()
    {
        if(disposed)return;
        using var trace=OperationTrace.Enter(pendingWidgetOperation);
        if(pendingLiveSync) {pendingLiveSync=false;liveTimer.Change(Timeout.Infinite,Timeout.Infinite);Refresh();}
        if(!pendingSave)return;
        try {Persist(State);pendingSave=false;saveTimer.Change(Timeout.Infinite,Timeout.Infinite);SaveStatus=UiLanguage.Text("已自动保存");}
        catch(Exception ex) {AppLog.Error("WidgetController","Operation failed",ex);SaveStatus=UiLanguage.Text("自动保存失败，当前改动尚未写入磁盘");Error?.Invoke(SaveStatus+"："+ex.Message);}
        Changed?.Invoke();
    }
    public void SetEnabled(string id,bool enabled)=>Save(State with {Widgets=State.Widgets.Select(w=>w.Id==id?w with {Enabled=enabled}:w).ToArray()});
    private AppearanceOptions appearance=new();
    public void SetAppearance(AppearanceOptions value) {appearance=value.Normalize();if(session!=null)Refresh();}
    public void Refresh(bool reflow=false)
    {
        if(disposed||loadError!=null)return;
        var presentation=PresentationState();
        if(!presentation.Widgets.Any(w=>w.Enabled)) {Stop();Status=State.Widgets.Length==0?UiLanguage.Text("尚未添加组件"):UiLanguage.Text("组件已全部隐藏");Changed?.Invoke();return;}
        try {
            lastWidgetOperation=OperationTrace.Current;
            if(session==null)Start();
            if(!session!.Messages.TryWrite(new WidgetMessage {Type=reflow?"reflow":"sync",Configuration=presentation,Appearance=appearance}))throw new IOException(UiLanguage.Text("组件通信已停止，请重新显示组件。"));
        } catch(Exception ex) {AppLog.Error("WidgetController","Operation failed",ex);Stop();Status=UiLanguage.Text("组件启动失败");Error?.Invoke(Status+"："+ex.Message);Changed?.Invoke();}
    }
    public void Restart() {Stop();Refresh(true);}
    private void Start()
    {
        var info=new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory,"Widgets","ZZZwallpaper.WidgetHost.exe")) {
            UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,
            StandardInputEncoding=new System.Text.UTF8Encoding(false),StandardOutputEncoding=new System.Text.UTF8Encoding(false)
        };
        AppLog.ConfigureChild(info);
        info.Environment["ZZZWALLPAPER_UI_LANGUAGE"] = UiLanguage.Current;
        info.Environment["ZZZWALLPAPER_BASE_DIRECTORY"]=LumaWall.Contracts.FeaturePacks.BaseDirectory;
        info.Environment["ZZZWALLPAPER_FEATURE_PACK_ROOT"]=LumaWall.Contracts.FeaturePacks.Root;
        info.Environment["ZZZWALLPAPER_CONTROLLER_PID"]=Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var process=new Process {StartInfo=info};process.Start();App.ChildProcesses.Add(process);
        var active=new Session(process);session=active;Status=UiLanguage.Text("正在准备组件");
        AppLog.Info("WidgetHost",$"Started; pid={process.Id}");
        active.Errors=AppLog.CaptureStandardErrorAsync(process,"WidgetHost");
        active.Sending=Task.Run(()=>Write(active));active.Reading=Read(active);Changed?.Invoke();
    }
    private async Task Write(Session active)
    {
        try {await foreach(var message in active.Messages.ReadAllAsync()) {
            await active.Process.StandardInput.WriteLineAsync(message.AsMemory(),active.SendingCancellation.Token).ConfigureAwait(false);
            await active.Process.StandardInput.FlushAsync(active.SendingCancellation.Token).ConfigureAwait(false);
        }}
        catch(OperationCanceledException) when(active.SendingCancellation.IsCancellationRequested) { }
        catch(Exception ex) {if(session==active&&!disposed)AppLog.Warning("WidgetHost","Could not send widget update",ex);}
        finally {active.Messages.Complete();}
    }
    private async Task Read(Session active)
    {
        try {
            while(await active.Process.StandardOutput.ReadLineAsync() is { } line) {
                var message=JsonSerializer.Deserialize<WidgetMessage>(line);if(message==null)continue;
                dispatch(()=> {if(session!=active||disposed)return;try {Receive(active,message);}catch(Exception ex) {AppLog.Error("WidgetController","Operation failed",ex);Error?.Invoke(UiLanguage.Text("组件操作未保存：")+ex.Message);Refresh();}});
            }
        } catch(Exception ex) {if(session==active&&!disposed)AppLog.Error("WidgetHost","Could not read widget events",ex);}
        finally {
            string exitDetail="";
            try {await active.Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2));exitDetail=UiLanguage.Format($"退出码 0x{active.Process.ExitCode:X8}");}catch { }
            dispatch(()=> {
                if(session!=active||disposed)return;
                string detail=active.LastFault??exitDetail;
                AppLog.Error("WidgetHost","Unexpected exit; "+exitDetail+"; "+detail);
                Stop();Status=UiLanguage.Text("组件已停止，点击“重新显示”可恢复");Changed?.Invoke();
                Error?.Invoke(UiLanguage.Text("组件宿主已停止，壁纸继续运行。")+(detail.Length==0?"":UiLanguage.Text("原因：")+detail));
            });
        }
    }
    private void Receive(Session active,WidgetMessage message)
    {
        if(message.Type is "hide" or "visibility" or "lock" or "interaction-lock" or "group-lock") {
            if(WidgetInteraction.ApplyDesktopCommand(State,message) is { } updated)Save(updated);
            return;
        }
        if(message.Type is "edit" or "edit-widget") {var sender=State.Widgets.FirstOrDefault(w=>w.Id==message.Id);if(sender==null||!WidgetInteraction.CanEdit(State,sender))return;}
        if(message.Type=="edit-group"&&!State.LayoutGroups.Any(g=>g.Id==message.Id&&!g.Locked))return;
        switch(message.Type) {
            case "inspection":if(message.RequestId is { } request&&inspections.TryGetValue(request,out var inspection))inspection.TrySetResult(message);break;
            case "timer-action":if(message.Id!=null&&State.Widgets.Any(w=>w.Id==message.Id&&w.Enabled&&w.Kind==WidgetKind.FocusTimer&&WidgetInteraction.CanInteract(State,w)))ControlFocus(message.Id,message.TimerAction??"");break;
            case "spectrum-process":active.SpectrumProcessId=message.ProcessId is >0?message.ProcessId:null;break;
            case "ready": OperationTrace.Record(lastWidgetOperation,"widget_renderer","ready",$"revision={message.Revision}; association=latest_sync_candidate",sampledEvent:true);AppliedRevision=message.Revision;active.LastFault=null;Status=UiLanguage.Text("组件已显示 · 拖动图标或卡片移动，右键设置");Changed?.Invoke();break;
            case "resync":Refresh(true);break;
            case "manage":ManageRequested?.Invoke(null);break;
            case "edit":ManageRequested?.Invoke(message.Id);break;
            case "edit-widget":ManageRequested?.Invoke(message.Id);break;
            case "edit-group":ManageRequested?.Invoke("group:"+message.Id);break;
            case "open":_=OpenLink(message);break;
            case "open-card":_=OpenCard(message);break;
            case "layout" when message.Layout is { } layout:
                var current=State.Widgets.FirstOrDefault(w=>w.Id==layout.Id&&w.Enabled);if(current==null||current.LayoutGroupId!=null||State.IsDragLocked(current))return;
                Save(State with {Widgets=State.Widgets.Select(w=>w.Id==layout.Id?w with {Screen=layout.Screen,X=layout.X,Y=layout.Y,Width=layout.Width,Height=layout.Height}:w).ToArray()});break;
            case "group-layout" when message.GroupLayout is { } moved:
                if(!State.LayoutGroups.Any(g=>g.Id==moved.Id)||!State.Widgets.Any(w=>w.LayoutGroupId==moved.Id&&w.Enabled)||State.IsGroupDragLocked(moved.Id))return;
                Save(State with {LayoutGroups=State.LayoutGroups.Select(g=>g.Id==moved.Id?g with {Screen=moved.Screen,X=moved.X,Y=moved.Y}:g).ToArray()});break;
            case "group" when message.Layout is { } selected:
                var grouped=State.Widgets.FirstOrDefault(w=>w.Id==selected.Id&&w.Enabled);
                if(grouped==null||!WidgetInteraction.CanEdit(State,grouped))return;
                if(State.Groups.Any(g=>g.Id==selected.Group))Save(State with {Widgets=State.Widgets.Select(w=>w.Id==selected.Id?w with {Group=selected.Group}:w).ToArray()});break;
            case "fault":active.LastFault=DescribeFault(message);Status=UiLanguage.Text("组件发生错误，可点击“重新显示”恢复");Changed?.Invoke();Error?.Invoke(UiLanguage.Text("组件操作失败：")+active.LastFault);break;
        }
    }
    private static string DescribeFault(WidgetMessage message)
    {
        string stage=message.FaultStage switch {"startup"=>UiLanguage.Text("初始化"),"input"=>UiLanguage.Text("读取控制指令"),"configuration"=>UiLanguage.Text("显示组件"),"ui"=>UiLanguage.Text("处理窗口消息"),"shutdown"=>UiLanguage.Text("关闭组件"),_=>UiLanguage.Text("组件运行")};
        string type=message.FaultType is {Length:>0 and <=100} name&&name.All(c=>char.IsLetterOrDigit(c)||c=='_')?name:UiLanguage.Text("未知异常");
        return UiLanguage.Format($"{stage}失败（{type}")+(message.FaultHResult is int code?$"，0x{code:X8}":"")+"）";
    }
    private async Task OpenLink(WidgetMessage message)
    {
        var active=session;if(active==null||!State.Widgets.Any(w=>w.Id==message.Id&&IsPresented(w)&&w.Kind==WidgetKind.Search&&WidgetInteraction.CanInteract(State,w)))return;
        bool opened=false;string? error=null;
        try {var uri=NavigationLinks.Website(message.TargetUri??"");await targetLauncher.Open(uri.AbsoluteUri);opened=true;}
        catch(Exception ex) {AppLog.Warning("Widget.OpenLink","Browser activation failed",ex);error=UiLanguage.Text("无法打开浏览器，请检查默认浏览器设置");}
        SendReply(active,new WidgetMessage {Type="opened",Id=message.Id,RequestId=message.RequestId,Succeeded=opened,Error=error});
    }
    private async Task OpenCard(WidgetMessage message)
    {
        var active=session;if(active==null)return;
        var widget=State.Widgets.FirstOrDefault(w=>w.Id==message.Id&&IsPresented(w)&&w.Kind==WidgetKind.Shortcuts);
        if(widget?.ShortcutId==null||widget.ShortcutId!=message.ShortcutId||!WidgetInteraction.CanInteract(State,widget))return;
        var card=State.Shortcuts.FirstOrDefault(c=>c.Id==widget.ShortcutId);if(card==null)return;
        bool opened=false;string? error=null;
        try {
            string target=ShortcutTargets.Normalize(card.Kind,card.Url);
            switch(card.Kind) {
                case ShortcutKind.Website:await targetLauncher.Open(target);opened=true;break;
                case ShortcutKind.Folder:
                    await targetLauncher.Open(target);opened=true;break;
                case ShortcutKind.Command:
                    // Execute only the stored card selected by this enabled widget.
                    // No command is accepted from IPC; saving/editing never runs it.
                    string directory=string.IsNullOrWhiteSpace(card.WorkingDirectory)?Environment.GetFolderPath(Environment.SpecialFolder.UserProfile):ShortcutTargets.Folder(card.WorkingDirectory);
                    if(!Directory.Exists(directory))throw new DirectoryNotFoundException(UiLanguage.Text("命令的工作目录不存在"));
                    using(var process=Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,"cmd.exe")) {
                        Arguments="/d /s /c \""+target+"\"",WorkingDirectory=directory,UseShellExecute=false,
                        CreateNoWindow=!card.ShowCommandWindow,WindowStyle=card.ShowCommandWindow?ProcessWindowStyle.Normal:ProcessWindowStyle.Hidden
                    }))opened=process!=null;
                    break;
            }
        } catch(Exception ex) {AppLog.Error("WidgetController","Operation failed",ex);error=ex is DirectoryNotFoundException?ex.Message:UiLanguage.Text("无法启动，请检查卡片设置中的目标和权限");}
        SendReply(active,new WidgetMessage {Type="opened",Id=message.Id,RequestId=message.RequestId,Succeeded=opened,Error=error});
    }
    private void SendReply(Session active,WidgetMessage message)
    {
        if(session!=active||active.Messages.TryWrite(message))return;
        Stop();Status=UiLanguage.Text("组件通信未响应，请重新显示组件。");Changed?.Invoke();Error?.Invoke(Status);
    }
    public void ControlFocus(string id,string action) {
        var widget=State.Widgets.FirstOrDefault(w=>w.Id==id&&w.Kind==WidgetKind.FocusTimer)??throw new ArgumentException(UiLanguage.Text("专注计时器不存在"));
        if(!WidgetInteraction.CanInteract(State,widget))return;
        Save(State with {Widgets=State.Widgets.Select(w=>w.Id==id?w with {Focus=widget.Focus.Action(action,DateTimeOffset.UtcNow)}:w).ToArray()});
    }
    private void AdvanceFocusTimers() {
        if(disposed||loadError!=null)return;var now=DateTimeOffset.UtcNow;bool changed=false;
        var values=State.Widgets.Select(w=> {if(w.Kind!=WidgetKind.FocusTimer)return w;var focus=w.Focus.Advance(now);if(focus==w.Focus)return w;changed=true;return w with {Focus=focus};}).ToArray();
        if(changed)try {Save(State with {Widgets=values});}catch(Exception ex) {AppLog.Error("FocusTimer","Could not save focus timer",ex);}
    }
    internal Task Retirement {get;private set;}=Task.CompletedTask;
    private void Stop()
    {
        var old=session;session=null;AppliedRevision=null;if(old==null)return;old.Messages.Complete();
        var retired=Task.Run(()=>ChildProcessShutdown.StopAsync(old.Process,old.Sending,old.SendingCancellation,
            Task.WhenAll(old.Reading,old.Errors),error=>AppLog.Warning("WidgetHost",UiLanguage.Text("组件通信退出未完成"),error)));
        Retirement=Retirement.IsCompleted?retired:Task.WhenAll(Retirement,retired);
    }
    public void Dispose()
    {
        if(disposed)return;
        // Persist the final value without starting/reflowing desktop windows during exit.
        pendingLiveSync=false;
        try {FlushPendingSave();}finally {disposed=true;targetLauncher.Dispose();liveTimer.Dispose();saveTimer.Dispose();focusTimer.Dispose();Stop();}
    }
}
