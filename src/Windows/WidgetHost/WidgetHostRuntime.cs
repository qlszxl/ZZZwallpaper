using System.Text;
using LumaWall.Contracts;
using LumaWall.WidgetSupport;
using Microsoft.Win32;

namespace LumaWall.WidgetHost;

internal sealed partial class WidgetHostRuntime : ApplicationContext
{
    private readonly WidgetHostChannel channel;
    private readonly Control dispatcher=new();
    private readonly Dictionary<string,WidgetWindow> forms=new();
    private Dictionary<string,WidgetInstance> effectiveWidgets=new();
    private AppearanceOptions? appliedAppearance;
    private WidgetConfiguration? pendingReady;
    private bool hashingReady;
    private bool resyncRequested;
    private readonly System.Windows.Forms.Timer themeTimer=new() {Interval=40};
    private readonly System.Windows.Forms.Timer timeTimer=new() {Interval=250};
    private readonly System.Windows.Forms.Timer performanceTimer=new() {Interval=1000};
    private SystemPerformanceSampler? performanceSampler;
    private bool sampling;
    private readonly System.Windows.Forms.Timer spectrumTimer=new() {Interval=33};
    private SpectrumSource? spectrumSource;
    private bool sessionLocked,suspended;
    private long spectrumRetry;
    private int? reportedSpectrumProcess;
    private void ReportSpectrumProcess()
    {
        int? pid=spectrumSource?.ProcessId;if(pid==reportedSpectrumProcess)return;
        reportedSpectrumProcess=pid;channel.Send(new() {Type="spectrum-process",ProcessId=pid});
    }
    private void RefreshSpectrum()
    {
        var targets=forms.Values.Where(w=>w.IsSpectrum&&w.Visible).ToArray();
        if(stopping||sessionLocked||suspended||targets.Length==0) {spectrumSource?.Dispose();spectrumSource=null;ReportSpectrumProcess();foreach(var form in targets)form.UpdateSpectrum(SpectrumSample.Empty);return;}
        if(spectrumSource?.Exited==true) {spectrumSource.Dispose();spectrumSource=null;spectrumRetry=Environment.TickCount64+2000;}
        if(spectrumSource==null&&Environment.TickCount64>=spectrumRetry) {
            try {spectrumSource=new SpectrumSource();}catch(Exception ex) when(ex is IOException or System.ComponentModel.Win32Exception or InvalidOperationException) {AppLog.Warning("AudioSpectrumHost","Could not start spectrum helper; retrying",ex);spectrumRetry=Environment.TickCount64+3000;}
        }
        ReportSpectrumProcess();var sample=spectrumSource?.Snapshot??SpectrumSample.Empty;foreach(var form in targets)form.UpdateSpectrum(sample);
    }
    private void RefreshPlayback() {foreach(var form in forms.Values)form.SetPlaybackPaused(sessionLocked||suspended);}
    private void SessionChanged(object sender,SessionSwitchEventArgs e)=>Post(()=> {if(e.Reason==SessionSwitchReason.SessionLock)sessionLocked=true;else if(e.Reason==SessionSwitchReason.SessionUnlock)sessionLocked=false;RefreshSpectrum();RefreshPlayback();});
    private void PowerChanged(object sender,PowerModeChangedEventArgs e)=>Post(()=> {if(e.Mode==PowerModes.Suspend)suspended=true;else if(e.Mode==PowerModes.Resume)suspended=false;RefreshSpectrum();RefreshPlayback();});
    private volatile bool stopping;
    private WidgetConfiguration current=new();
    private string? draggingGroup;
    private bool stacking;
    private void ApplyStacking()
    {
        if(stopping||stacking)return;stacking=true;
        try {
            var order=WidgetStacking.FrontToBack(current).ToArray();
            foreach(var widget in order.Where(w=>!w.AlwaysOnTop).Concat(order.Where(w=>w.AlwaysOnTop).Reverse()))
                if(forms.TryGetValue(widget.Id,out var form))form.LowerToDesktop();
        }
        finally {stacking=false;}
    }
    public WidgetHostRuntime(WidgetHostChannel channel)
    {
        this.channel=channel;
        _=dispatcher.Handle;
        themeTimer.Tick+=(_,_)=> {if(!stopping)WidgetVisuals.Advance();};
        performanceTimer.Tick+=async(_,_)=> {
            if(stopping||sampling)return;
            var targets=forms.Values.Where(w=>w.IsPerformance&&w.Visible).ToArray();
            if(targets.Length==0) {StopSampling();return;}
            if(!targets.Any(form=>form.PerformanceUpdateDue))return;
            if(performanceSampler is not { } sampler)return;
            var fields=targets.Aggregate((PerformanceMetrics)0,(mask,form)=>mask|form.Metrics);sampling=true;
            long sampledAt=Environment.TickCount64;
            try {
                var sample=await Task.Run(()=>sampler.Sample(fields));
                if(stopping||!ReferenceEquals(performanceSampler,sampler)||sample==null)return;
                foreach(var form in forms.Values.Where(w=>w.IsPerformance&&w.Visible&&(w.Metrics&~fields)==0))form.UpdatePerformance(sample,sampledAt);
            } catch(Exception ex) {if(!stopping)channel.ReportFault("performance",ex);}
            finally {sampling=false;}
        };
        timeTimer.Tick+=(_,_)=> {if(!stopping&&!sessionLocked&&!suspended)foreach(var form in forms.Values.Where(w=>w.Visible))form.UpdateTime();};
        spectrumTimer.Tick+=(_,_)=>RefreshSpectrum();
        SystemEvents.SessionSwitch+=SessionChanged;SystemEvents.PowerModeChanged+=PowerChanged;
        SystemEvents.DisplaySettingsChanged+=DisplayChanged;
        _=Task.Run(async()=> {
            try {
                await channel.ReadMessagesAsync(message=>Post(()=>Apply(message)));
            } catch(Exception ex) {if(!stopping)channel.ReportFault("input",ex);}
            finally {Post(Shutdown);}
        });
    }
    private void Post(Action action)
    {
        try {if(!stopping)dispatcher.BeginInvoke((Action)(()=> {if(!stopping)action();}));}
        catch(InvalidOperationException) when(stopping||dispatcher.IsDisposed) { }
    }
    private void Apply(WidgetMessage message)
    {
        if(stopping)return;
        try {
            if(message.Type=="opened") {if(message.Id!=null&&forms.TryGetValue(message.Id,out var target))target.OpenCompleted(message);return;}
            if(message.Type=="inspect") {
                string revision=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(current.ToJson())));
                channel.Send(new() {Type="inspection",Id=message.Id,RequestId=message.RequestId,Revision=revision,Telemetry=message.Id!=null&&forms.TryGetValue(message.Id,out var inspected)?inspected.AutomationTelemetry():null});return;
            }
            var state=message.Configuration??message.Delta?.Apply(current);
            if(state==null)return;state.Validate();
            // Geometry from an in-progress desktop drag wins over a queued sync.
            if(draggingGroup is { } dragId&&!state.IsGroupDragLocked(dragId)&&current.LayoutGroups.FirstOrDefault(g=>g.Id==dragId) is { } local)
                state=state with {LayoutGroups=state.LayoutGroups.Select(g=>g.Id==dragId?g with {Screen=local.Screen,X=local.X,Y=local.Y}:g).ToArray()};
            var previous=current;
            bool appearanceChanged=message.Appearance is { } appearance&&appearance!=appliedAppearance;
            if(appearanceChanged) {appliedAppearance=message.Appearance;WidgetVisuals.ApplyAppearance(appliedAppearance!);}
            bool groupsChanged=!previous.LayoutGroups.SequenceEqual(state.LayoutGroups)||!previous.Groups.SequenceEqual(state.Groups)
                ||!previous.Widgets.Select(w=>(w.Id,w.Locked)).SequenceEqual(state.Widgets.Select(w=>(w.Id,w.Locked)));
            bool reflow=message.Type=="reflow";
            bool restack=reflow||!WidgetStacking.FrontToBack(previous).Select(w=>w.Id).SequenceEqual(WidgetStacking.FrontToBack(state).Select(w=>w.Id));
            var oldCards=previous.Shortcuts.ToDictionary(c=>c.Id);var cards=state.Shortcuts.ToDictionary(c=>c.Id);
            current=state;
            var positions=WidgetPlacement.EffectiveChanges(state,previous,effectiveWidgets,reflow);
            foreach(var id in forms.Keys.Where(id=>!state.Widgets.Any(w=>w.Id==id&&w.Enabled)).ToArray()) {forms[id].CloseHost();forms.Remove(id);}
            foreach(var saved in state.Widgets.Where(w=>w.Enabled)) {
                var widget=positions[saved.Id];
                if(!forms.TryGetValue(widget.Id,out var form)) {form=new WidgetWindow(widget,state,channel.Send);form.GroupDrag=MoveGroup;form.StackRequested=ApplyStacking;forms.Add(widget.Id,form);form.Show();form.PlaceOnDesktop();restack=true;}
                else {
                    bool changed=!effectiveWidgets.TryGetValue(widget.Id,out var old)||old!=widget;
                    bool cardChanged=widget.ShortcutId is { } cardId&&oldCards.GetValueOrDefault(cardId)!=cards.GetValueOrDefault(cardId);
                    if(changed||cardChanged||groupsChanged||appearanceChanged||reflow)form.Apply(widget,state,reflow);
                    if(old==null||old.AlwaysOnTop!=widget.AlwaysOnTop||old.X!=widget.X||old.Y!=widget.Y||old.Screen!=widget.Screen||WidgetPlacement.LogicalSize(old)!=WidgetPlacement.LogicalSize(widget))restack=true;
                }
            }
            effectiveWidgets=positions;
            RefreshPlayback();
            RefreshTopmostHooks();if(restack)ApplyStacking();
            timeTimer.Enabled=state.Widgets.Any(w=>w.Enabled&&(TimeWidgets.IsTime(w.Kind)||FeaturePacks.GridFor(w)!=null));
            themeTimer.Enabled=WidgetVisuals.Animate&&forms.Values.Any(form=>form.Visible);
            var monitors=forms.Values.Where(form=>form.IsPerformance&&form.Visible).ToArray();
            if(monitors.Length==0)StopSampling();
            else {
                performanceSampler??=new SystemPerformanceSampler();
                performanceTimer.Interval=monitors.Min(form=>form.RefreshMilliseconds);
                performanceTimer.Start();
            }
            spectrumTimer.Enabled=forms.Values.Any(form=>form.IsSpectrum&&form.Visible);RefreshSpectrum();
            pendingReady=current;_=SendReady();resyncRequested=false;
        } catch(Exception ex) {
            channel.ReportFault("configuration",ex);
            if(!resyncRequested) {resyncRequested=true;channel.Send(new() {Type="resync"});}
        }
    }
    private void DisplayChanged(object? sender,EventArgs e)=>Post(()=>Apply(new() {Type="reflow",Configuration=current}));
    private async Task SendReady()
    {
        if(hashingReady)return;hashingReady=true;
        try {
            while(!stopping&&pendingReady is { } snapshot) {
                pendingReady=null;
                string revision=await Task.Run(()=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(snapshot.ToJson()))));
                if(!stopping&&pendingReady==null&&ReferenceEquals(snapshot,current))channel.Send(new() {Type="ready",Revision=revision});
            }
        }catch(Exception ex) {if(!stopping)channel.ReportFault("configuration",ex);}
        finally {hashingReady=false;}
    }
    private void MoveGroup(string id,int dx,int dy,bool finished)
    {
        if(stopping)return;var group=current.LayoutGroups.FirstOrDefault(g=>g.Id==id);if(group==null||current.IsGroupDragLocked(id)) {draggingGroup=null;return;}
        if(dx!=0||dy!=0) {
            draggingGroup=id;var old=WidgetPlacement.MeasureGroup(current,group);
            int left=old.Left+dx,top=old.Top+dy;
            var target=Screen.FromPoint(Cursor.Position);
            var moved=group with {Screen=target.DeviceName};var bounds=WidgetPlacement.MeasureGroup(current,moved);var area=target.WorkingArea;
            group=moved with {X=Math.Clamp((left-area.Left)/(double)Math.Max(1,bounds.MaxX),0,1),Y=Math.Clamp((top-area.Top)/(double)Math.Max(1,bounds.MaxY),0,1)};
            current=current with {LayoutGroups=current.LayoutGroups.Select(g=>g.Id==id?group:g).ToArray()};
            var positions=WidgetPlacement.EffectiveAll(current);
            effectiveWidgets=positions;
            foreach(var member in current.Widgets.Where(w=>w.LayoutGroupId==id&&w.Enabled))
                if(forms.TryGetValue(member.Id,out var form))form.Apply(positions[member.Id],current,true);
        }
        ApplyStacking();
        if(finished) {draggingGroup=null;channel.Send(new() {Type="group-layout",GroupLayout=group});}
    }
    private void StopSampling() {performanceTimer.Stop();var sampler=performanceSampler;performanceSampler=null;if(sampler!=null)_=Task.Run(sampler.Dispose);}
    public void Shutdown()
    {
        if(stopping)return;stopping=true;timeTimer.Stop();timeTimer.Dispose();spectrumTimer.Stop();spectrumTimer.Dispose();spectrumSource?.Dispose();spectrumSource=null;SystemEvents.SessionSwitch-=SessionChanged;SystemEvents.PowerModeChanged-=PowerChanged;StopSampling();performanceTimer.Dispose();themeTimer.Stop();themeTimer.Dispose();SystemEvents.DisplaySettingsChanged-=DisplayChanged;
        StopTopmostHooks();foreach(var form in forms.Values)form.CloseHost();forms.Clear();dispatcher.Dispose();ExitThread();
    }
    protected override void Dispose(bool disposing) {if(disposing)Shutdown();base.Dispose(disposing);}
}
