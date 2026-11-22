using System.Runtime.InteropServices;
using System.Text.Json;
using LumaWall.Automation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private sealed record AutomationControl(string ControlId,string? ParentId,string Type,string Name,Rect Bounds,bool Visible,bool Enabled,bool Focused,string[] Actions,object? State);
    private sealed record AutomationUiSnapshot(string Context,string Page,AutomationControl[] Nodes);
    private readonly AutomationSnapshotCache<AutomationUiSnapshot> automationUiSnapshots=new();
    private readonly Dictionary<string,FrameworkElement> automationControls=new();
    [StructLayout(LayoutKind.Sequential)] private struct InputPoint {public int X,Y;}
    [DllImport("user32.dll",EntryPoint="ClientToScreen")] private static extern bool AutomationClientToScreen(nint window,ref InputPoint point);
    [DllImport("user32.dll",EntryPoint="WindowFromPoint")] private static extern nint AutomationWindowFromPoint(InputPoint point);
    [DllImport("user32.dll",EntryPoint="GetAncestor")] private static extern nint AutomationAncestor(nint window,uint flags);
    [DllImport("user32.dll",EntryPoint="GetForegroundWindow")] private static extern nint AutomationForeground();
    private bool AutomationSafeControl(FrameworkElement element)
    {
        // Limit mutations to parameter editors. Never grant an entire app
        // process permission to execute its command cards, file pickers or authorization controls.
        bool editor=false;
        for(DependencyObject? node=element;node!=null;node=VisualTreeHelper.GetParent(node)) {
            if(ReferenceEquals(node,DisplayPanel))editor=true;
            if(ReferenceEquals(node,WidgetEditorPanel)&&widgets?.State.Widgets.FirstOrDefault(w=>w.Id==widgetDetailKey) is { } widget&&widget.Kind!=LumaWall.Contracts.WidgetKind.Shortcuts&&!widgets.State.IsEditingLocked(widget))editor=true;
            if(ReferenceEquals(node,SettingsAutomationHost)||ReferenceEquals(node,SharingPanel))return false;
        }
        return editor&&(element is Slider or ToggleSwitch or TextBox or ScrollViewer or ComboBox||element is Button&&Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(element)=="ParameterSectionToggle");
    }
    private string AutomationUiContext()=>JsonSerializer.Serialize(new {
        page=navigationPage,screen=SelectedScreen?.DeviceName,effectTargetAssetId,selectedEffectKind,draftEdit=effectSession.Revision,
        widgetDetailKey,widgetRevision=widgets?.ConfigurationRevision,configurationRevision=store.ConfigurationRevision()
    });
    private AutomationUiSnapshot SnapshotAutomationUi()
    {
        automationControls.Clear();var nodes=new List<AutomationControl>();var focused=FocusManager.GetFocusedElement(Root.XamlRoot);
        var handle=WinRT.Interop.WindowNative.GetWindowHandle(this);var origin=new InputPoint();AutomationClientToScreen(handle,ref origin);double scale=Root.XamlRoot.RasterizationScale;
        void Visit(DependencyObject element,string? parent,bool visible,bool enabled) {
            if(nodes.Count>=10000)return;
            if(element is FrameworkElement control) {
                string id=AutomationControlIdentity.Get(control);
                visible&=control.Visibility==Visibility.Visible&&control.Opacity>0&&control.IsLoaded;enabled&=control is not Control c||c.IsEnabled;
                Rect bounds=new();try {var local=control.TransformToVisual(Root).TransformBounds(new Rect(0,0,control.ActualWidth,control.ActualHeight));bounds=new(origin.X+local.X*scale,origin.Y+local.Y*scale,local.Width*scale,local.Height*scale);}catch {visible=false;}
                visible&=bounds.Width>0&&bounds.Height>0;
                string name=Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(control);if(string.IsNullOrEmpty(name))name=control.Name;
                string[] actions=AutomationSafeControl(control)&&visible&&enabled?control switch {ComboBox=>["select_option"],Button=>["click"],TextBox=>["click","text","key"],Slider=>["click","drag","key"],ToggleSwitch=>["click"],ScrollViewer=>["scroll"],_=>[]}:[];
                object? state=AutomationSafeControl(control)?control switch {
                    ComboBox combo=>new {selectedIndex=combo.SelectedIndex,options=combo.Items.Cast<object>().Select((item,index)=>new {index,label=item is ComboBoxItem option?option.Content?.ToString():item.ToString()}).ToArray()},
                    Slider slider=>new {value=slider.Value,minimum=slider.Minimum,maximum=slider.Maximum,step=slider.StepFrequency},
                    ToggleSwitch toggle=>new {isOn=toggle.IsOn},
                    TextBox text=>new {text=text.Text,isReadOnly=text.IsReadOnly,selectionStart=text.SelectionStart,selectionLength=text.SelectionLength},
                    ScrollViewer scroll=>new {horizontalOffset=scroll.HorizontalOffset,verticalOffset=scroll.VerticalOffset,scrollableWidth=scroll.ScrollableWidth,scrollableHeight=scroll.ScrollableHeight},
                    _=>null
                }:null;
                nodes.Add(new(id,parent,control.GetType().Name,name,bounds,visible,enabled,focused is DependencyObject focus&&AutomationControlIdentity.Get(focus)==id,actions,state));automationControls[id]=control;parent=id;
            }
            int count=VisualTreeHelper.GetChildrenCount(element);for(int i=0;i<count;i++)Visit(VisualTreeHelper.GetChild(element,i),parent,visible,enabled);
        }
        Visit(Root,null,IsWindowVisible(handle)&&!IsIconic(handle),true);
        return new(AutomationUiContext(),navigationPage,nodes.ToArray());
    }
    private static OperationFault UiConflict(string reason)=>new("revision_conflict","UI 目标或编辑上下文已变化，请重新读取","arguments.treeRevision",recovery:"reread_ui_tree",details:new {reason});
    private OperationReply AutomationSelectOption(JsonElement args)
    {
        var observed=automationUiSnapshots.Get(Arg(args,"treeRevision"));
        var previous=observed.Nodes.FirstOrDefault(n=>n.ControlId==Arg(args,"controlId"))??throw UiConflict("control_removed");
        var current=SnapshotAutomationUi();
        if(observed.Context!=current.Context)throw UiConflict("editing_context_changed");
        var node=current.Nodes.FirstOrDefault(n=>n.ControlId==previous.ControlId)??throw UiConflict("control_removed");
        if(JsonSerializer.Serialize(previous with {Focused=false})!=JsonSerializer.Serialize(node with {Focused=false}))throw UiConflict("target_changed");
        if(!node.Actions.Contains("select_option")||automationControls[node.ControlId] is not ComboBox combo)throw new UnauthorizedAccessException("此控件不允许下拉选择");
        if(VisualTreeHelper.GetOpenPopupsForXamlRoot(Root.XamlRoot).Any(p=>p.IsOpen))throw new OperationFault("invalid_state","请先关闭弹出层再选择参数",null);
        int index=args.GetProperty("index").GetInt32();
        if(index<0||index>=combo.Items.Count||combo.Items[index] is ComboBoxItem {IsEnabled:false})throw new OperationFault("invalid_argument","选项不存在或已禁用",null);
        try {combo.SelectedIndex=index;FlushHistoryEdits();}
        finally {automationUiSnapshots.Clear();}
        return OperationReply.Success(new {controlId=node.ControlId,selectedIndex=combo.SelectedIndex,requiresReadback=true});
    }
    private async Task<OperationReply> AutomationUi(OperationRequest request,CancellationToken token)
    {
        var a=request.Arguments;
        if(request.Name=="ui_perform_action"&&Arg(a,"controlId").StartsWith("widget:",StringComparison.Ordinal))return await AutomationWidgetClick(a,token);
        if(request.Name=="ui_get_tree") {
            string revision;AutomationUiSnapshot snapshot;
            if(a.TryGetProperty("treeRevision",out var r)) {revision=r.GetString()!;snapshot=automationUiSnapshots.Get(revision);}
            else {snapshot=SnapshotAutomationUi();revision=automationUiSnapshots.Add(snapshot);}
            int offset=a.TryGetProperty("offset",out var o)?o.GetInt32():0,limit=a.TryGetProperty("limit",out var l)?l.GetInt32():100;
            return OperationReply.Success(new {treeRevision=revision,page=snapshot.Page,coordinateSpace="physical_desktop_pixels",total=snapshot.Nodes.Length,offset,controls=snapshot.Nodes.Skip(offset).Take(limit),scope="realized_control_center_tree",truncated=snapshot.Nodes.Length>=10000,snapshotLifetimeMs=AutomationSnapshotCache<AutomationUiSnapshot>.LifetimeMs});
        }
        var observed=automationUiSnapshots.Get(Arg(a,"treeRevision"));
        var previous=observed.Nodes.FirstOrDefault(n=>n.ControlId==Arg(a,"controlId"))??throw new OperationFault("resource_not_found","此快照中没有该控件","arguments.controlId");
        var current=SnapshotAutomationUi();
        if(observed.Context!=current.Context)throw UiConflict("editing_context_changed");
        var node=current.Nodes.FirstOrDefault(n=>n.ControlId==previous.ControlId)??throw UiConflict("control_removed");
        // Ignore other controls and pointer-induced focus changes. Keyboard/text
        // still require current focus below, and selection/value must be current.
        if(JsonSerializer.Serialize(previous with {Focused=false})!=JsonSerializer.Serialize(node with {Focused=false}))throw UiConflict("target_changed");
        string action=Arg(a,"action");if(!node.Actions.Contains(action))throw new UnauthorizedAccessException("此控件未授权该交互");
        var element=automationControls[node.ControlId];var handle=WinRT.Interop.WindowNative.GetWindowHandle(this);
        if(AutomationForeground()!=handle)throw new OperationFault("invalid_state","控制中心不是前台窗口，拒绝向其他窗口发送输入",null);
        if(VisualTreeHelper.GetOpenPopupsForXamlRoot(Root.XamlRoot).Any(p=>p.IsOpen&&p.Tag as string!="numeric-input-hint"))throw new OperationFault("invalid_state","有弹出层打开，请先关闭后操作基础控件",null);
        double Fraction(string key,double fallback)=>a.TryGetProperty(key,out var value)?value.GetDouble():fallback;
        InputPoint PointAt(double x,double y)=>new() {X=(int)(node.Bounds.X+Math.Clamp(x,.001,.999)*node.Bounds.Width),Y=(int)(node.Bounds.Y+Math.Clamp(y,.001,.999)*node.Bounds.Height)};
        void CheckHit(InputPoint point) {
            if(AutomationForeground()!=handle||AutomationAncestor(AutomationWindowFromPoint(point),2)!=handle)throw new OperationFault("invalid_state","目标被其他窗口遮挡或焦点已改变",null);
            var origin=new InputPoint();AutomationClientToScreen(handle,ref origin);double scale=Root.XamlRoot.RasterizationScale;
            var hits=VisualTreeHelper.FindElementsInHostCoordinates(new Point((point.X-origin.X)/scale,(point.Y-origin.Y)/scale),Root).ToArray();
            bool matches=false;
            for(DependencyObject? hit=hits.FirstOrDefault();hit!=null;hit=VisualTreeHelper.GetParent(hit))if(AutomationControlIdentity.Get(hit)==node.ControlId) {matches=true;break;}
            if(!matches)throw new OperationFault("invalid_state","实际命中与目标控件不符",null);
        }
        var inputs=new List<AutomationInput.Input>();var point=PointAt(Fraction("x",.5),Fraction("y",.5));CheckHit(point);
        AutomationInput.Input Mouse(uint flags,InputPoint p,int delta=0)=>AutomationInput.Mouse(flags,p.X,p.Y,System.Windows.Forms.SystemInformation.VirtualScreen,delta);
        if(action is "click" or "drag") {
            inputs.Add(Mouse(1,point));inputs.Add(Mouse(2,point));
            if(action=="drag") {var target=PointAt(Fraction("toX",.5),Fraction("toY",.5));CheckHit(target);for(int i=1;i<=12;i++)inputs.Add(Mouse(1,new() {X=point.X+(target.X-point.X)*i/12,Y=point.Y+(target.Y-point.Y)*i/12}));point=target;}
            inputs.Add(Mouse(4,point));
        } else if(action=="scroll") {
            inputs.Add(Mouse(1,point));inputs.Add(Mouse(0x800,point,a.GetProperty("delta").GetInt32()));
        } else {
            if(!node.Focused)throw new OperationFault("invalid_state","请先点击控件获得焦点，再重读 UI 树后输入",null);
            if(action=="text") {if(element is not TextBox box||box.IsReadOnly)throw new UnauthorizedAccessException("控件不可输入");foreach(char ch in Arg(a,"text")) {if(char.IsControl(ch))throw new OperationFault("invalid_argument","不允许控制字符",null);inputs.Add(AutomationInput.Unicode(ch));inputs.Add(AutomationInput.Unicode(ch,true));}}
            else {
                ushort key=Arg(a,"key") switch {"Enter"=>13,"Escape"=>27,"Left"=>37,"Right"=>39,"Up"=>38,"Down"=>40,"Home"=>36,"End"=>35,"Backspace"=>8,_=>throw new UnauthorizedAccessException("不允许将焦点移出受控范围")};
                if(Flag(a,"shift"))inputs.Add(AutomationInput.Key(16));inputs.Add(AutomationInput.Key(key));inputs.Add(AutomationInput.Key(key,true));if(Flag(a,"shift"))inputs.Add(AutomationInput.Key(16,true));
            }
        }
        token.ThrowIfCancellationRequested();uint sent;
        try {sent=AutomationInput.Dispatch(inputs);}
        finally {automationUiSnapshots.Clear();} // Read again after an attempted dispatch, including partial sends.
        return OperationReply.Success(new {status="input_dispatched",controlId=node.ControlId,inputMethod="win32_send_input",eventCount=sent,requiresReadback=true});
    }
    private async Task<OperationReply> AutomationWidgetClick(JsonElement a,CancellationToken token)
    {
        if(Arg(a,"action")!="click")throw new UnauthorizedAccessException("桌面组件仅开放实际按钮点击");
        var parts=Arg(a,"controlId").Split(':');if(parts.Length!=3)throw new OperationFault("invalid_argument","控件 ID 无效",null);
        var widget=widgets!.State.Widgets.FirstOrDefault(w=>w.Id==parts[1]);
        if(widget==null||(widget.Kind!=LumaWall.Contracts.WidgetKind.FocusTimer&&LumaWall.Contracts.FeaturePacks.GridFor(widget)==null)||!widget.Enabled||widgets.State.IsInteractionLocked(widget))throw new UnauthorizedAccessException("组件未允许点击");
        if(LumaWall.Contracts.FeaturePacks.GridFor(widget)!=null&&parts[2]!="0")throw new UnauthorizedAccessException("网格组件仅开放主要操作按钮");
        if(Arg(a,"treeRevision")!=widgets.ConfigurationRevision)throw new OperationFault("invalid_state","组件配置已变化，请重读 widget_get",null);
        var inspection=await widgets.Inspect(widget.Id,token);
        if(inspection?.Telemetry is not { } telemetry||inspection.Revision!=widgets.ConfigurationRevision||Arg(a,"treeRevision")!=widgets.ConfigurationRevision)throw new OperationFault("invalid_state","组件宿主未确认当前配置，请重读 widget_get",null);
        var button=telemetry.GetProperty("buttons").EnumerateArray().FirstOrDefault(b=>b.GetProperty("controlId").GetString()==Arg(a,"controlId"));
        if(button.ValueKind==JsonValueKind.Undefined||!button.GetProperty("visible").GetBoolean()||!button.GetProperty("enabled").GetBoolean())throw new OperationFault("invalid_state","按钮不可见或禁用",null);
        var bounds=button.GetProperty("bounds");int x=(int)(bounds.GetProperty("x").GetDouble()+bounds.GetProperty("width").GetDouble()/2),y=(int)(bounds.GetProperty("y").GetDouble()+bounds.GetProperty("height").GetDouble()/2);
        var handle=(nint)telemetry.GetProperty("window").GetInt64();
        if(AutomationAncestor(AutomationWindowFromPoint(new() {X=x,Y=y}),2)!=handle)throw new OperationFault("invalid_state","按钮被其他窗口遮挡",null);
        token.ThrowIfCancellationRequested();var desktop=System.Windows.Forms.SystemInformation.VirtualScreen;
        uint sent;
        try {sent=AutomationInput.Dispatch([AutomationInput.Mouse(1,x,y,desktop),AutomationInput.Mouse(2,x,y,desktop),AutomationInput.Mouse(4,x,y,desktop)]);}
        finally {automationUiSnapshots.Clear();}
        return OperationReply.Success(new {status="input_dispatched",controlId=Arg(a,"controlId"),inputMethod="win32_send_input",eventCount=sent,requiresReadback=true});
    }
}
