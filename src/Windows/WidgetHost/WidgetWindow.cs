using System.ComponentModel;
using System.Runtime.InteropServices;
using LumaWall.Contracts;
using LumaWall.WidgetSupport;
using Shortcut=LumaWall.Contracts.Shortcut;
using W=System.Windows;
using C=System.Windows.Controls;
using I=System.Windows.Input;
using M=System.Windows.Media;
using H=System.Windows.Interop;
using Shapes=System.Windows.Shapes;

namespace LumaWall.WidgetHost;

// Per-pixel transparent WPF windows keep the actual widget silhouette smooth.
// They contain one search field or one button, with no native or custom caption.
internal sealed partial class WidgetWindow : W.Window
{
    private WidgetInstance instance;
    private Shortcut? card;
    private WidgetContent? widgetContent;
    private readonly WidgetInputState inputState=new();
    private readonly Action<WidgetMessage> send;
    private C.TextBox? input=>widgetContent?.SearchInput;
    private C.Button? cardButton=>widgetContent?.CardButton;
    private C.Border? focusEdge=>widgetContent?.FocusEdge;
    private W.FrameworkElement? timeArtwork=>widgetContent?.DrawingElement;
    private IReadOnlyList<C.Button> timeButtons=>widgetContent?.ActionButtons??Array.Empty<C.Button>();
    private VisualWidgetOptions? visualContentKey;
    private WidgetInstance? contentInstance;
    private Shortcut? contentCard;
    internal bool IsSpectrum=>instance.Kind==WidgetKind.Spectrum;
    internal void UpdateSpectrum(SpectrumSample sample) {if(closing||widgetContent==null)return;widgetContent.UpdateSpectrum(sample);if(tip!=null&&!tip.IsOpen)tip.Content=sample.Available?UiLanguage.Text("系统声音 · 40 Hz–16 kHz"):UiLanguage.Text("声音暂不可用");}
    private PackageGrid? surface;
    internal void UpdateTime() {if(!closing) {RefreshOverflowPointerRegion();widgetContent?.UpdateTime();}}
    internal void SetPlaybackPaused(bool value) {if(!closing)widgetContent?.SetPlaybackPaused(value);}
    private C.ContextMenu? menu;
    private C.ToolTip? tip;
    private bool closing,applying,dragged,shown,dragLocked,editingLocked,mousePassThrough;
    private bool interactionLocked=>instance.InteractionLocked;
    private string requestId="",normalTip="";
    private nint hwnd;
    private H.HwndSource? source;
    private readonly System.Windows.Threading.DispatcherTimer tipTimer=new() {Interval=TimeSpan.FromSeconds(4)};
    [StructLayout(LayoutKind.Sequential)] private struct Rect {public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint {public int X,Y;}
    [DllImport("user32.dll")] private static extern nint GetShellWindow();
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(uint processId);
    private static void TransferLaunchGesture()
    {
        // Only the controller that created our private IPC process receives the
        // foreground grant, and only during an actual search/card gesture.
        if(uint.TryParse(Environment.GetEnvironmentVariable("ZZZWALLPAPER_CONTROLLER_PID"),out var processId)&&processId>0)
            AllowSetForegroundWindow(processId);
    }
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint h,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint h,int index,nint value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint h,nint after,int x,int y,int w,int height,uint flags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint h,out Rect rect);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern nint GetCapture();
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint h);
    internal bool Visible=>IsVisible;
    internal bool IsPerformance=>instance.Kind==WidgetKind.Performance;
    internal int RefreshMilliseconds=>instance.Performance.IntervalMilliseconds(instance.RefreshSeconds);
    internal PerformanceMetrics Metrics=>instance.Metrics;
    internal bool PerformanceUpdateDue=>widgetContent?.PerformanceUpdateDue==true;
    internal Action? StackRequested {get;set;}
    internal Action<string,int,int,bool>? GroupDrag {get;set;}
    internal WidgetWindow(WidgetInstance value,WidgetConfiguration state,Action<WidgetMessage> send)
    {
        this.send=send;instance=value;WindowStyle=W.WindowStyle.None;ResizeMode=W.ResizeMode.NoResize;AllowsTransparency=true;
        Background=M.Brushes.Transparent;ShowInTaskbar=false;ShowActivated=false;SizeToContent=W.SizeToContent.Manual;
        var size=WidgetPlacement.LogicalSize(value);SetLogicalBodySize(size.Width,size.Height);FontFamily=WidgetVisuals.Body;FontSize=14;Foreground=WidgetVisuals.Ink;
        SourceInitialized+=(_,_)=> {
            hwnd=new H.WindowInteropHelper(this).Handle;source=H.HwndSource.FromHwnd(hwnd);source?.AddHook(WindowMessage);
            // Explicitly remove caption AND sizing styles. The previous thick-frame
            // override allowed Windows to draw a second, classic non-client frame.
            SetWindowLongPtr(hwnd,-16,(nint)((GetWindowLongPtr(hwnd,-16).ToInt64()&~0x00CF0000L)|0x80000000L));
            SetWindowLongPtr(hwnd,-20,(nint)((GetWindowLongPtr(hwnd,-20).ToInt64()|0x80)&~0x40000L));
            SetWindowLongPtr(hwnd,-8,instance.AlwaysOnTop?0:GetShellWindow());
            SetWindowPos(hwnd,0,0,0,0,0,0x37); // FRAMECHANGED | NOACTIVATE | NOSIZE | NOMOVE | NOZORDER
        };
        System.Windows.Forms.Integration.ElementHost.EnableModelessKeyboardInterop(this);
        Deactivated+=(_,_)=>Dispatcher.BeginInvoke(PlaceOnDesktop);
        Activated+=(_,_)=>Dispatcher.BeginInvoke(PlaceOnDesktop);
        LocationChanged+=(_,_)=> {if(!applying&&shown)StackRequested?.Invoke();};
        SizeChanged+=(_,_)=>UpdateSearchRadius();
        ContextMenuOpening+=(_,e)=> {if(mousePassThrough||editingLocked) {e.Handled=true;return;}if(menu!=null)WidgetVisuals.RefreshMenu(menu);};
        C.ContextMenuService.SetShowOnDisabled(this,true);
        IsVisibleChanged+=(_,_)=> {if(IsVisible)shown=true;};
        Closing+=(_,e)=> {if(!closing) {e.Cancel=true;send(new() {Type="visibility",Id=instance.Id,Enabled=false});}};
        Closed+=(_,_)=> {tipTimer.Stop();widgetContent?.Dispose();source?.RemoveHook(WindowMessage);source=null;};
        tipTimer.Tick+=(_,_)=> {tipTimer.Stop();if(tip!=null) {tip.IsOpen=false;tip.Content=normalTip;}};
        _=new H.WindowInteropHelper(this).EnsureHandle();Apply(value,state,true);
    }
    private nint WindowMessage(nint handle,int msg,nint w,nint l,ref bool handled)
    {
        // The warehouse's extra pixels are light only, never a drag/click target.
        if(msg==0x84&&surface!=null&&GetWindowRect(handle,out var outer)) {
            var body=BodyBounds(outer);long point=l.ToInt64();
            int x=unchecked((short)(point&0xffff)),y=unchecked((short)((point>>16)&0xffff));
            bool pass=mousePassThrough||(!OverflowPointerCaptured&&!body.Contains(x,y));
            SetNativeMousePassThrough(pass);
            if(pass) {handled=true;return (nint)(-1);} // HTTRANSPARENT
        }
        if(msg==0x10D)inputState.Composing=!interactionLocked;
        if(msg==0x10E) {inputState.Composing=false;inputState.ImeCommit=!interactionLocked;}
        if(msg==0x101)inputState.ImeCommit=false;
        if(msg==0x2E0&&!closing&&!applying&&instance.LayoutGroupId==null)Dispatcher.BeginInvoke(()=> {if(!closing) {ClampToScreen();SaveLayout();}}); // Groups reflow together in their context.
        return 0;
    }
    internal void Apply(WidgetInstance value,WidgetConfiguration state,bool forceLayout=false)
    {
        if(closing)return;
        surface=FeaturePacks.GridFor(value);
        bool layout=forceLayout||SpectrumLayout.Size(instance.Spectrum)!=SpectrumLayout.Size(value.Spectrum)||instance.Screen!=value.Screen||instance.X!=value.X||instance.Y!=value.Y||instance.IconOnly!=value.IconOnly||instance.Metrics!=value.Metrics||WidgetPlacement.LogicalSize(instance)!=WidgetPlacement.LogicalSize(value)||instance.Search.Width!=value.Search.Width||instance.Search.Height!=value.Search.Height||instance.Spectrum.Width!=value.Spectrum.Width||instance.Spectrum.Height!=value.Spectrum.Height||instance.Visual.Width!=value.Visual.Width||instance.Visual.Height!=value.Visual.Height;
        if(instance.RefreshSeconds!=value.RefreshSeconds||instance.Performance.RefreshMilliseconds!=value.Performance.RefreshMilliseconds||instance.Metrics!=value.Metrics)widgetContent?.ResetPerformanceSchedule();
        bool wasDragLocked=dragLocked;dragLocked=state.IsDragLocked(value);editingLocked=state.IsEditingLocked(value);
        mousePassThrough=WidgetInteraction.MousePassThrough(state,value);
        bool topChanged=instance.AlwaysOnTop!=value.AlwaysOnTop;
        instance=value;card=state.Shortcuts.FirstOrDefault(c=>c.Id==value.ShortcutId);applying=true;
        try {
            if(hwnd!=0&&topChanged)SetWindowLongPtr(hwnd,-8,value.AlwaysOnTop?0:GetShellWindow());
            Topmost=value.AlwaysOnTop;
            Title=value.Kind switch {WidgetKind.Search=>SearchWidgetOptions.Label(value.Search.Engine)+UiLanguage.Text("搜索"),WidgetKind.Performance=>UiLanguage.Text("性能监测"),WidgetKind.Spectrum=>UiLanguage.Text("音频频谱"),WidgetKind.Text=>UiLanguage.Text("文本"),WidgetKind.Sticker=>UiLanguage.Text("贴纸"),WidgetKind.Mask=>UiLanguage.Text("遮罩"),WidgetKind.Clock=>UiLanguage.Text("时钟与日期"),WidgetKind.Countdown=>UiLanguage.Text("倒计时"),WidgetKind.FocusTimer=>UiLanguage.Text("专注计时器"),WidgetKind.Extension=>FeaturePacks.Widgets.FirstOrDefault(w=>w.Key==value.PackageWidgetId)?.Entry.Title??UiLanguage.Text("扩展组件"),_=>UiLanguage.Text("快捷卡片")};Opacity=value.Opacity;
            var previous=contentInstance;
            if(previous==null||previous.GridOptions?.Rows!=value.GridOptions?.Rows||previous.Spectrum.Background!=value.Spectrum.Background||previous.Kind!=value.Kind||previous.PackageWidgetId!=value.PackageWidgetId||previous.IconOnly!=value.IconOnly||previous.Icon!=value.Icon||previous.LayoutGroupId!=value.LayoutGroupId||previous.Metrics!=value.Metrics||previous.Performance.Flow!=value.Performance.Flow||previous.Performance.Columns!=value.Performance.Columns||!previous.Performance.Order.SequenceEqual(value.Performance.Order)||card!=contentCard||value.Visual!=visualContentKey) {
                BuildContent();contentInstance=value;contentCard=card;visualContentKey=value.Visual;
            }
            UpdateTime();
            widgetContent?.UpdateInteraction();
            if(menu!=null)WidgetVisuals.RefreshMenu(menu);
            if(editingLocked&&menu!=null)menu.IsOpen=false;
            if(!wasDragLocked&&dragLocked&&I.Mouse.Captured is C.Button captured&&ReferenceEquals(W.Window.GetWindow(captured),this))I.Mouse.Capture(null);
            ApplyMousePassThrough();
            if(layout)ApplySavedBounds();
            UpdateSearchRadius();
        } finally {applying=false;}
        if(shown&&!IsVisible) {Show();PlaceOnDesktop();}
    }
    private void ApplyMousePassThrough()
    {
        if(hwnd==0)return;bool pass=mousePassThrough;
        IsHitTestVisible=!pass;Focusable=!pass;
        if(pass) {
            if(menu!=null)menu.IsOpen=false;
            if(IsKeyboardFocusWithin)I.Keyboard.ClearFocus();
            if(I.Mouse.Captured is W.DependencyObject captured&&ReferenceEquals(W.Window.GetWindow(captured),this))I.Mouse.Capture(null);
        }
        if(surface!=null)RefreshOverflowPointerRegion();else SetNativeMousePassThrough(pass);
    }
    private bool OverflowPointerCaptured=>GetCapture()==hwnd||(I.Mouse.Captured is W.DependencyObject captured&&ReferenceEquals(W.Window.GetWindow(captured),this));
    private void RefreshOverflowPointerRegion()
    {
        if(closing||hwnd==0||surface==null)return;
        bool pass=mousePassThrough;
        if(!pass&&!OverflowPointerCaptured&&GetCursorPos(out var point)&&GetWindowRect(hwnd,out var outer))pass=!BodyBounds(outer).Contains(point.X,point.Y);
        // Keep WPF focus/capture and the explicit component setting independent:
        // only the decorative overflow switches native mouse transparency.
        SetNativeMousePassThrough(pass);
    }
    private void SetNativeMousePassThrough(bool pass)
    {
        long style=GetWindowLongPtr(hwnd,-20).ToInt64(),next=pass?style|0x20:style&~0x20L;
        if(style!=next) {
            SetWindowLongPtr(hwnd,-20,(nint)next);
            SetWindowPos(hwnd,0,0,0,0,0,0x37); // FRAMECHANGED | NOACTIVATE | NOSIZE | NOMOVE | NOZORDER
        }
    }
    private void UpdateSearchRadius()
    {
        double width=ActualWidth,height=ActualHeight;
        if(surface!=null) {
            width*=surface!.DesignWidth/(surface!.DesignWidth+2*surface!.Overflow);
            height=Math.Max(0,height-2*width*surface!.Overflow/surface!.DesignWidth);
        }
        widgetContent?.Resize(width,height);
    }
    private void BuildContent()
    {
        if(menu!=null)menu.IsOpen=false;if(tip!=null)tip.IsOpen=false;tipTimer.Stop();
        var search=SearchFieldState.Capture(input);
        widgetContent?.Dispose();
        menu=WidgetVisuals.Menu();
        var context=new WidgetContentContext(()=>instance,()=>card,()=>editingLocked,inputState,menu,DragHandle,SubmitSearch,ActivateCard,ShowMessage,send);
        widgetContent=PackageWidgetFactory.Create(context,search);
        widgetContent.RefreshPointerRegion=RefreshOverflowPointerRegion;
        Content=widgetContent.Element;
        if(search.Focused)input?.Focus();
        if(instance.LayoutGroupId!=null)WidgetVisuals.Action(menu,UiLanguage.Text("组件组设置…"),"settings",()=>send(new() {Type="edit-group",Id=instance.LayoutGroupId}),()=>!editingLocked);
        WidgetVisuals.Action(menu,()=>instance.Enabled?UiLanguage.Text("隐藏组件"):UiLanguage.Text("显示组件"),"visible",()=>send(new() {Type="visibility",Id=instance.Id,Enabled=!instance.Enabled}),()=>!mousePassThrough&&!editingLocked);
        var locking=WidgetVisuals.Action(menu,UiLanguage.Text("锁定组件"),"lock",
            ()=>send(new() {Type="lock",Id=instance.Id,Locked=true}),()=>!mousePassThrough&&!editingLocked);
        locking.ToolTip=UiLanguage.Text("锁定拖动和右键菜单；在控制中心解锁");
        WidgetVisuals.Action(menu,()=>interactionLocked?UiLanguage.Text("解锁交互"):UiLanguage.Text("锁定交互"),"lock",
            ()=>send(new() {Type="interaction-lock",Id=instance.Id,InteractionLocked=!interactionLocked}),()=>!mousePassThrough&&!editingLocked);
        if(instance.LayoutGroupId!=null)WidgetVisuals.Action(menu,UiLanguage.Text("锁定组件组"),"lock",
            ()=>send(new() {Type="group-lock",Id=instance.Id,Locked=true}),()=>!mousePassThrough&&!editingLocked);
        WidgetVisuals.Action(menu,UiLanguage.Text("管理桌面组件…"),"settings",()=>send(new() {Type="manage"}),()=>!mousePassThrough&&!editingLocked);
        ContextMenu=menu;
        normalTip=instance.Kind switch {WidgetKind.Search=>UiLanguage.Text("Enter 搜索"),WidgetKind.Performance=>UiLanguage.Text("整机性能"),WidgetKind.Spectrum=>UiLanguage.Text("系统声音"),WidgetKind.Text=>UiLanguage.Text("文本"),WidgetKind.Sticker=>UiLanguage.Text("贴纸"),WidgetKind.Mask=>UiLanguage.Text("遮罩"),_=>card==null?UiLanguage.Text("快捷卡片"):card.Name+"\n"+ShortcutTargets.Describe(card)};

        tip=WidgetVisuals.Tip(normalTip);tip.PlacementTarget=this;tip.Placement=System.Windows.Controls.Primitives.PlacementMode.Bottom;
        // Error feedback is opened explicitly; the widget has no hover tooltip.
    }
    internal void UpdatePerformance(SystemPerformanceSample sample,long sampledAt)
    {
        if(closing||!IsPerformance||widgetContent?.UpdatePerformance(sample,sampledAt) is not { } values)return;
        normalTip=string.Join("\n",values);
        if(tip!=null&&!tip.IsOpen)tip.Content=normalTip;
    }
    private void DragHandle(C.Button handle,Action click,bool opensSettings=false)
    {
        W.Point origin=default,last=default;string? activeGroupDrag=null;
        handle.PreviewMouseLeftButtonDown+=(_,e)=> {origin=last=PointToScreen(e.GetPosition(this));dragged=false;activeGroupDrag=null;};
        handle.PreviewMouseMove+=(_,e)=> {
            if(e.LeftButton!=I.MouseButtonState.Pressed||!handle.IsMouseCaptured)return;
            var p=PointToScreen(e.GetPosition(this));
            if(!dragged&&Math.Abs(p.X-origin.X)<W.SystemParameters.MinimumHorizontalDragDistance&&Math.Abs(p.Y-origin.Y)<W.SystemParameters.MinimumVerticalDragDistance)return;
            if(dragLocked) {dragged=true;e.Handled=true;return;}
            if(instance.LayoutGroupId is { } group) {
                dragged=true;activeGroupDrag=group;e.Handled=true;int dx=(int)Math.Round(p.X-last.X),dy=(int)Math.Round(p.Y-last.Y);last=p;
                GroupDrag?.Invoke(group,dx,dy,false);return;
            }
            dragged=true;e.Handled=true;I.Mouse.Capture(null);
            try {DragMove();}catch(InvalidOperationException) { }
            if(dragLocked)ApplySavedBounds();else {ClampToScreen();SaveLayout();}Dispatcher.BeginInvoke(()=>dragged=false);
        };
        void FinishGroupDrag() {if(activeGroupDrag is { } group) {activeGroupDrag=null;GroupDrag?.Invoke(group,0,0,true);Dispatcher.BeginInvoke(()=>dragged=false);}}
        handle.PreviewMouseLeftButtonUp+=(_,e)=> {if(dragged) {e.Handled=true;I.Mouse.Capture(null);FinishGroupDrag();Dispatcher.BeginInvoke(()=>dragged=false);}};
        handle.LostMouseCapture+=(_,_)=> {FinishGroupDrag();Dispatcher.BeginInvoke(()=>dragged=false);};
        handle.Click+=(_,_)=> {if(!mousePassThrough&&!dragged&&(opensSettings?!editingLocked:!interactionLocked))click();};
    }
    private void SubmitSearch()
    {
        if(mousePassThrough||interactionLocked||input==null||inputState.Composing)return;
        try {var uri=NavigationLinks.Search(input.Text,instance.Search);if(uri==null) {input.Focus();return;}
            TransferLaunchGesture();requestId=Guid.NewGuid().ToString("N");send(new() {Type="open",Id=instance.Id,TargetUri=uri.AbsoluteUri,RequestId=requestId});}
        catch(ArgumentException ex) {ShowMessage(ex.Message);}
    }
    private void ActivateCard()
    {
        if(mousePassThrough||interactionLocked)return;
        if(card==null) {if(editingLocked)return;send(new() {Type="edit-widget",Id=instance.Id});return;}
        TransferLaunchGesture();requestId=Guid.NewGuid().ToString("N");send(new() {Type="open-card",Id=instance.Id,ShortcutId=card.Id,RequestId=requestId});
    }
    internal void OpenCompleted(WidgetMessage message)
    {
        if(closing||message.RequestId!=requestId)return;
        if(!message.Succeeded)ShowMessage(message.Error??UiLanguage.Text("无法打开，请检查目标设置"));
        else if(tip!=null) {tipTimer.Stop();tip.IsOpen=false;tip.Content=normalTip;}
    }
    private void ShowMessage(string message)
    {
        if(tip==null)return;tip.Content=message;tip.IsOpen=true;tipTimer.Stop();tipTimer.Start();
    }
    private void ApplySavedBounds()
    {
        var logical=WidgetPlacement.LogicalSize(instance);SetLogicalBodySize(logical.Width,logical.Height);
        var body=WidgetPlacement.BodyBounds(instance,WidgetPlacement.Resolve(instance.Screen));
        SetBodyBounds(body.Left,body.Top,body.Width,body.Height);
    }
    private void SetLogicalBodySize(double width,double height)
    {
        double padding=surface!=null?width*surface!.Overflow/surface!.DesignWidth:0;
        Width=width+2*padding;Height=height+2*padding;
    }
    private int OverflowPixels(int bodyWidth)=>surface!=null?(int)Math.Round(bodyWidth*surface!.Overflow/surface!.DesignWidth):0;
    private void SetBodyBounds(int x,int y,int width,int height)
    {
        int padding=OverflowPixels(width);
        SetWindowPos(hwnd,0,x-padding,y-padding,width+2*padding,height+2*padding,0x14);
    }
    private System.Drawing.Rectangle BodyBounds(Rect outer)
    {
        int fullWidth=outer.Right-outer.Left;
        int width=surface!=null?(int)Math.Round(fullWidth*surface!.DesignWidth/(surface!.DesignWidth+2*surface!.Overflow)):fullWidth;
        int padding=OverflowPixels(width);
        return new(outer.Left+padding,outer.Top+padding,fullWidth-2*padding,outer.Bottom-outer.Top-2*padding);
    }
    private void ClampToScreen()
    {
        if(!GetWindowRect(hwnd,out var r))return;var bounds=BodyBounds(r);var area=WidgetPlacement.Area(instance,Screen.FromRectangle(bounds));
        int width=Math.Min(bounds.Width,area.Width),height=Math.Min(bounds.Height,area.Height);
        if(surface!=null) {
            double fit=Math.Min(1,Math.Min(area.Width/(double)Math.Max(1,bounds.Width),area.Height/(double)Math.Max(1,bounds.Height)));
            width=Math.Max(1,(int)Math.Round(bounds.Width*fit));height=Math.Max(1,(int)Math.Round(bounds.Height*fit));
        }
        SetBodyBounds(Math.Clamp(bounds.Left,area.Left,area.Right-width),Math.Clamp(bounds.Top,area.Top,area.Bottom-height),width,height);
    }
    private void SaveLayout()
    {
        if(applying||closing||dragLocked||instance.LayoutGroupId!=null||!IsVisible||!GetWindowRect(hwnd,out var r))return;
        var bounds=BodyBounds(r);var screen=Screen.FromRectangle(bounds);var area=WidgetPlacement.Area(instance,screen);
        instance=instance with {Screen=screen.DeviceName,X=Math.Clamp((bounds.Left-area.Left)/(double)Math.Max(1,area.Width-bounds.Width),0,1),Y=Math.Clamp((bounds.Top-area.Top)/(double)Math.Max(1,area.Height-bounds.Height),0,1),Width=Math.Clamp(bounds.Width/(double)area.Width,.05,1),Height=Math.Clamp(bounds.Height/(double)area.Height,.03,1)};
        send(new() {Type="layout",Layout=instance});
    }
    internal void Reflow() {if(closing)return;applying=true;try {ApplySavedBounds();}finally {applying=false;}SaveLayout();}
    internal void PlaceOnDesktop() {if(StackRequested is { } request)request();else LowerToDesktop();}
    internal void LowerToDesktop() {if(!closing&&IsVisible)SetWindowPos(hwnd,instance.AlwaysOnTop?(nint)(-1):(nint)1,0,0,0,0,0x0001|0x0002|0x0010|0x0200);}
    internal void CloseHost() {if(closing)return;closing=true;if(menu!=null)menu.IsOpen=false;Close();}
}

