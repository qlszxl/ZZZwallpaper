using LumaWall.DesktopIntegration;
using LumaWall.Contracts;
using System.Runtime.InteropServices;

int checks=0;
void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS: "+name);checks++;}
var main=new PauseScreen("main",new(0,0,1920,1080),new(0,0,1920,1040));
var side=new PauseScreen("side",new(-2560,0,2560,1440),new(-2520,0,2520,1440));
PauseWindow Window(Rectangle bounds,bool maximized=false)=>new(bounds,maximized,100,"Chrome_WidgetWin_1");
HashSet<string> Detect(params PauseWindow[] windows)=>DesktopPauseDetector.CoveredScreens([main,side],windows,1,2);
var fullscreen=Window(main.Bounds);
Check(Detect(fullscreen).SetEquals(["main"]),"Borderless fullscreen pauses its screen");
Check(Detect(Window(side.WorkingArea,true)).SetEquals(["side"]),"Maximized window beside a side taskbar pauses its negative-coordinate screen");
Check(Detect(Window(main.WorkingArea,true)).SetEquals(["main"]),"Maximized browser pauses even when hidden taskbar leaves a bottom gap");
Check(Detect(Window(main.WorkingArea)).Count==0,"Ordinary restored window covering only the work area is not fullscreen");
Check(Detect(Window(new(80,70,1000,700),true)).Count==0,"Stale maximized flag without coverage does not pause");
Check(Detect(Window(new(-2000,100,900,600)),fullscreen).SetEquals(["main"]),"Foreground activity on the second screen does not resume a covered main screen");
Check(Detect(fullscreen,Window(side.WorkingArea,true)).SetEquals(["main","side"]),"Multiple covered screens pause independently");
Check(Detect(Window(Rectangle.FromLTRB(-2560,0,1920,1440))).SetEquals(["main","side"]),"One spanning window covers both monitors");
Check(Detect(fullscreen with {Minimized=true}).Count==0,"Minimized fullscreen windows cannot hold pause");
Check(Detect(fullscreen with {Visible=false}).Count==0,"Hidden windows cannot hold pause");
Check(Detect(fullscreen with {Cloaked=true}).Count==0,"Other virtual desktops and cloaked windows are excluded");
Check(Detect(fullscreen with {Opaque=false}).Count==0,"Transparent fullscreen overlay cannot hold pause");
Check(Detect(Window(main.WorkingArea,true) with {Opaque=false}).Count==0,"Transparent maximized overlay cannot hold pause");
Check(Detect(fullscreen with {Opaque=false},Window(side.WorkingArea,true)).SetEquals(["side"]),"Transparent overlay does not suppress opaque coverage on another monitor");
Check(Detect(fullscreen with {Opaque=false},fullscreen).SetEquals(["main"]),"Opaque application under transparent overlay still pauses");
Check(DesktopPauseDetector.IsOpaque(0,false,0,0),"Ordinary application window remains eligible");
Check(DesktopPauseDetector.IsOpaque(0x20,false,0,0),"Paint-order transparency alone is not evidence of visual transparency");
Check(!DesktopPauseDetector.IsOpaque(0x200020,false,255,0),"DirectComposition transparent overlay excluded");
Check(DesktopPauseDetector.IsOpaque(0x200000,false,255,0),"Ordinary DirectComposition browser remains eligible");
Check(!DesktopPauseDetector.IsOpaque(0x80000,true,0,2),"Fully transparent global alpha excluded");
Check(!DesktopPauseDetector.IsOpaque(0x80000,true,128,2),"Translucent global alpha excluded");
Check(DesktopPauseDetector.IsOpaque(0x80000,true,255,2),"Opaque layered application remains eligible");
Check(!DesktopPauseDetector.IsOpaque(0x80000,true,255,3),"Color-key holes excluded even at alpha 255");
Check(!DesktopPauseDetector.IsOpaque(0x80000,false,255,0),"Unknown per-pixel-alpha coverage does not pause");
// Owned, hidden native windows exercise the actual interop without covering a desktop.
nint layeredWindow=OpacityFixture.CreateWindowEx(0x80000,"STATIC","Opacity regression",0x80000000,0,0,10,10,0,0,0,0);
Check(layeredWindow!=0,"Native opacity fixture created");
try {
    foreach(byte alpha in new byte[]{0,128,255}) {
        Check(OpacityFixture.SetLayeredWindowAttributes(layeredWindow,0,alpha,2),"Set native alpha "+alpha);
        Check(DesktopPauseDetector.IsOpaque(layeredWindow)==(alpha==255),"Read native alpha "+alpha);
    }
    Check(OpacityFixture.SetLayeredWindowAttributes(layeredWindow,0,255,3)&&!DesktopPauseDetector.IsOpaque(layeredWindow),"Native color-key window excluded");
} finally {OpacityFixture.DestroyWindow(layeredWindow);}
Check(Detect(fullscreen with {ProcessId=1},fullscreen with {ProcessId=2}).Count==0,"Own control center and widgets excluded");
foreach(string name in new[]{"Progman","WorkerW","Shell_TrayWnd","Shell_SecondaryTrayWnd","LumaWall.VideoOutput"})
    Check(Detect(fullscreen with {ClassName=name}).Count==0,"Desktop infrastructure excluded: "+name);
Check(Detect(Window(new(0,0,1920,1079))).Count==0,"Visible bottom gap is not full-screen coverage for a non-maximized window");
Check(Detect(Window(Rectangle.Empty,true)).Count==0,"Empty window geometry is ignored");
Check(Detect(fullscreen,fullscreen).Count==1,"Duplicate candidates keep one pause state per screen");
Check(Detect().Count==0,"Closing the last covering window releases the screen");
var pause=new PauseState();pause.Set(PauseReason.User,true);pause.Set(PauseReason.Fullscreen,true);pause.Set(PauseReason.Fullscreen,false);
Check(pause.IsPaused&&pause.Reasons==PauseReason.User,"Removing automatic pause preserves manual pause");
pause.Set(PauseReason.User,false);Check(!pause.IsPaused,"Playback resumes only after all reasons clear");
var live=DesktopShell.FullscreenScreens();
Check(live.All(id=>Screen.AllScreens.Any(s=>s.DeviceName==id)),"Read-only native window enumeration completes and returns only connected screens");
Console.WriteLine($"Fullscreen checks passed: {checks}");

internal static class OpacityFixture {
    [DllImport("user32.dll",CharSet=CharSet.Unicode,EntryPoint="CreateWindowExW")] internal static extern nint CreateWindowEx(uint ex,string cls,string name,uint style,int x,int y,int width,int height,nint parent,nint menu,nint instance,nint parameter);
    [DllImport("user32.dll")] internal static extern bool SetLayeredWindowAttributes(nint window,uint key,byte alpha,uint flags);
    [DllImport("user32.dll")] internal static extern bool DestroyWindow(nint window);
}
