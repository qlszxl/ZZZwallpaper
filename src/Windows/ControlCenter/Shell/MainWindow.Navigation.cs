using System.Runtime.InteropServices;
using LumaWall.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Input;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private sealed record NavigationEntry(string Page,string? Widget,string? Group,int SpectrumLayer,EffectKind? Effect,double Scroll);
    private readonly List<NavigationEntry> navigationBack=[],navigationForward=[];
    private string navigationPage="wallpaper";
    private bool navigationReady,navigationRestoring,navigationQueued;
    private long lastNavigationInput=-1000,sidePointerTime=-1000;
    private NavigationSubclass? navigationSubclass;
    private delegate nint NavigationSubclass(nint hwnd,uint message,nuint wParam,nint lParam,nuint id,nuint data);
    [DllImport("comctl32.dll")] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowSubclass(nint hwnd,NavigationSubclass callback,nuint id,nuint data);
    [DllImport("comctl32.dll")] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool RemoveWindowSubclass(nint hwnd,NavigationSubclass callback,nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint hwnd,uint message,nuint wParam,nint lParam);

    private ScrollViewer? NavigationScroll()=>navigationPage switch {"wallpaper"=>WallpaperScroll,"display"=>EffectsParameterScroll,"widgets"=>WidgetsScroll,"presets"=>SharingScroll,"performance"=>PerformanceScroll,"settings"=>SettingsScroll,"appearance"=>AppearanceScroll,"packs"=>ExpansionScroll,"history"=>HistoryScroll,_=>null};
    private NavigationEntry CaptureNavigation()=>new(navigationPage,navigationPage=="widgets"?selectedWidgetId:null,navigationPage=="widgets"?selectedLayoutGroupId:null,navigationPage=="widgets"?spectrumEditorSelection:-1,navigationPage=="display"?selectedEffectKind:null,NavigationScroll()?.VerticalOffset??0);
    private static bool SameRoute(NavigationEntry a,NavigationEntry b)=>(a with {Scroll=0})==(b with {Scroll=0});
    private static void PushNavigation(List<NavigationEntry> history,NavigationEntry entry) {if(history.Count==64)history.RemoveAt(0);history.Add(entry);}
    private void RememberNavigation()
    {
        if(!navigationReady||navigationRestoring)return;
        var entry=CaptureNavigation();if(navigationBack.Count==0||!SameRoute(navigationBack[^1],entry))PushNavigation(navigationBack,entry);
        navigationForward.Clear();
    }
    private void InitializeNavigation()
    {
        navigationReady=true;
        Root.AddHandler(UIElement.PointerPressedEvent,new PointerEventHandler((_,e)=> {
            var kind=e.GetCurrentPoint(Root).Properties.PointerUpdateKind;
            if(kind is not (PointerUpdateKind.XButton1Pressed or PointerUpdateKind.XButton2Pressed))return;
            sidePointerTime=Environment.TickCount64;e.Handled=true;QueueNavigation(kind==PointerUpdateKind.XButton1Pressed);
        }),true);
        Root.AddHandler(UIElement.PointerReleasedEvent,new PointerEventHandler((_,e)=> {
            if(e.GetCurrentPoint(Root).Properties.PointerUpdateKind is PointerUpdateKind.XButton1Released or PointerUpdateKind.XButton2Released) {sidePointerTime=Environment.TickCount64;e.Handled=true;}
        }),true);
        // Driver-mapped Browser Back/Forward commands bubble to the top-level
        // HWND. Handle only these commands; desktop/global input is untouched.
        navigationSubclass=(hwnd,message,wParam,lParam,id,data)=> {
            if(message==0x0319) {
                uint high=(uint)((long)lParam>>16)&0xffff,command=high&0x0fff;
                if(command is 1 or 2) {
                    if((high&0xf000)!=0x8000||Environment.TickCount64-sidePointerTime>500)QueueNavigation(command==1);
                    return 1;
                }
            }
            return DefSubclassProc(hwnd,message,wParam,lParam);
        };
        nint window=WinRT.Interop.WindowNative.GetWindowHandle(this);
        SetWindowSubclass(window,navigationSubclass,0x4c574e,0);
        Closed+=(_,_)=> {navigationReady=false;if(navigationSubclass!=null)RemoveWindowSubclass(window,navigationSubclass,0x4c574e);};
    }
    private void QueueNavigation(bool backward)
    {
        long now=Environment.TickCount64;
        if(!navigationReady||navigationQueued||now-lastNavigationInput<100)return;
        lastNavigationInput=now;navigationQueued=true;
        if(!DispatcherQueue.TryEnqueue(()=> {navigationQueued=false;if(!navigationReady)return;try {TravelNavigation(backward);}catch(Exception ex) {ShowError(UiLanguage.Text("页面切换失败：")+ex.Message,ex);}}))navigationQueued=false;
    }
    private void TravelNavigation(bool backward)
    {
        CommitSpectrumInput();FlushEffectChanges();
        if(WidgetDrawerOverlay.Visibility==Visibility.Visible) {if(backward)CloseWidgetDrawer(true);return;}
        if(EffectsTargetOverlay.Visibility==Visibility.Visible) {if(backward)CloseEffectsTarget(true);return;}
        if(EffectsAddOverlay.Visibility==Visibility.Visible) {if(backward)CloseEffectsAdd(true);return;}
        if(QueueOverlay.Visibility==Visibility.Visible) {if(backward)CloseQueue(true);return;}
        var from=backward?navigationBack:navigationForward;var to=backward?navigationForward:navigationBack;var current=CaptureNavigation();
        while(from.Count>0) {
            var target=from[^1];from.RemoveAt(from.Count-1);
            if(SameRoute(current,target))continue;
            if(target.Widget!=null&&widgets?.State.Widgets.Any(w=>w.Id==target.Widget)!=true)continue;
            if(target.Group!=null&&widgets?.State.LayoutGroups.Any(g=>g.Id==target.Group)!=true)continue;
            PushNavigation(to,current);navigationRestoring=true;
            try {
                if(target.Page=="display") {
                    EndEffectEditing();LoadEffectDraft(true);
                }
                Navigate(target.Page);
                if(target.Widget is { } widget) {spectrumEditorWidgetId=widget;spectrumEditorSelection=target.SpectrumLayer;OpenWidget(widget);}
                else if(target.Group is { } group)OpenWidgetGroup(group);
                if(target.Effect is { } effect&&(effectSession.Collection.Added&effect)!=0)OpenEffect(effect);
                var scroll=NavigationScroll();scroll?.UpdateLayout();scroll?.ChangeView(null,target.Scroll,null,true);
            }finally {navigationRestoring=false;}
            return;
        }
    }
}
