using LumaWall.DesktopIntegration;
using System.Text;

System.Windows.Forms.Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
var input=Native.OpenInputDesktop(0,false,1);var name=new StringBuilder(128);
Console.WriteLine($"Input desktop: {input}, nameOK={Native.GetUserObjectInformation(input,2,name,256,out _)}, name={name}");if(input!=0)Native.CloseDesktop(input);
var progman=Native.FindWindow("Progman",null);
Console.WriteLine($"Progman={progman} exStyle=0x{Native.GetWindowLongPtr(progman,-20):X}");
Native.EnumChildWindows(progman,(hwnd,_)=> {Native.GetWindowRect(hwnd,out var r);Console.WriteLine($"Child {hwnd} {Native.Class(hwnd)} parent={Native.GetParent(hwnd)} [{r.Left},{r.Top},{r.Right},{r.Bottom}] visible={Native.IsWindowVisible(hwnd)} exStyle=0x{Native.GetWindowLongPtr(hwnd,-20):X}");return true;},0);
if(args.Contains("--watch"))
{
    using var live=new DesktopHitTestService();
    Thread.Sleep(250);int fresh=0,missing=0,stale=0;long maxAge=0;
    for(int i=0;i<500;i++) {var snapshot=live.Scene;if(snapshot==null)missing++;else {long age=Environment.TickCount64-snapshot.CapturedAt;maxAge=Math.Max(maxAge,age);if(age<=120)fresh++;else stale++;}Thread.Sleep(20);}
    Console.WriteLine($"Cache samples=500 fresh={fresh} missing={missing} stale={stale} maxAge={maxAge}ms");return;
}
using var service=new DesktopHitTestService(false);
Console.WriteLine(service.Diagnostic);
foreach(var screen in Screen.AllScreens) {
    Console.WriteLine($"Screen: {screen.DeviceName} {screen.Bounds}");
    for(int y=screen.Bounds.Top+100;y<screen.Bounds.Bottom;y+=400)
    for(int x=screen.Bounds.Left+100;x<screen.Bounds.Right;x+=600) {
        bool blank=service.IsBlank(new Native.Point(x,y));Console.WriteLine($"{x},{y}: blank={blank} {service.Diagnostic}");
    }
}
