using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using LumaWall.Contracts;
using LumaWall.DesktopIntegration;

internal static class Program
{
    [DllImport("gdi32.dll",CharSet=CharSet.Unicode)] private static extern int GetTextFace(nint dc,int count,StringBuilder name);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc,nint value);
    [STAThread]
    private static void Main(string[] args)
    {
        if(args.Length!=1)throw new ArgumentException("Pass an isolated screenshot output directory");
        var output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        int checks=0;
        void Check(bool ok,string message) {if(!ok)throw new Exception(message);Console.WriteLine("PASS: "+message);checks++;}
        Check(!DesktopMenuPreference.IsEnabled(null)&&!DesktopMenuPreference.IsEnabled("false")&&!DesktopMenuPreference.IsEnabled(""),"Missing/new desktop menu settings default off");
        Check(DesktopMenuPreference.IsEnabled("true"),"Explicit per-screen opt-in is retained");
        var type=typeof(DesktopShell);const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        using var shell=new DesktopShell(Path.Combine(output,"diagnostics"),false);
        using var hit=new DesktopHitTestService(false);
        using(var controller=new DesktopMenuController(hit,action=>action(),_=>{},shell.Handle)) {
            Check(!controller.Enabled,"No screen is captured without explicit allowlist");
            controller.SetEnabledScreens(["opt-in"]);
            Check(controller.Enabled&&!(bool)typeof(DesktopMenuController).GetMethod("ScreenEnabled",flags)!.Invoke(controller,["new-monitor"])!,"Newly connected screens remain outside the opt-in allowlist");
            controller.SetEnabledScreens([]);Check(!controller.Enabled,"Turning off the last screen releases capture");
        }
        var popupType=type.GetNestedType("MenuPopup",BindingFlags.NonPublic)!;
        foreach(bool desktop in new[]{true,false}) {
            var menu=Native.CreatePopupMenu();
            try {
                var rows=desktop?new (uint,string)[]{(1,"添加或更换视频…"),(11,"上一段"),(2,"暂停壁纸"),(12,"下一段"),(3,"画面与效果…"),(6,"打开控制中心"),(8,"退出软件")}:
                    new (uint,string)[]{(6,"打开控制中心"),(10,"继续接管桌面右键"),(8,"退出软件")};
                foreach(var row in rows)type.GetMethod("Item",flags)!.Invoke(shell,[menu,0u,row.Item1,row.Item2]);
                var entries=type.GetMethod("PrepareMenu",flags)!.Invoke(shell,[menu,Screen.PrimaryScreen!.WorkingArea]);
                type.GetField("desktopMenuActive",flags)!.SetValue(shell,desktop);
                var font=(nint)type.GetField("menuFontHandle",flags)!.GetValue(shell)!;
                using(var graphics=Graphics.FromHwnd(0)) {
                    var dc=graphics.GetHdc();var previous=SelectObject(dc,font);var face=new StringBuilder(128);
                    try {GetTextFace(dc,128,face);}finally {SelectObject(dc,previous);graphics.ReleaseHdc(dc);}
                    Console.WriteLine("Selected font: "+face);
                    Check(face.ToString().Contains("Source Han Sans",StringComparison.OrdinalIgnoreCase)||face.ToString().Contains("思源黑体",StringComparison.Ordinal),"Bundled OTF selected, not an accidental system-font fallback");
                }
                // GDI+ behavior depends on the installed font cache: it can throw
                // (the supplied diagnostic) or silently choose a different face.
                try {using var legacy=Font.FromHfont(font);Console.WriteLine("Legacy GDI+ face: "+legacy.Name);}
                catch(ArgumentException) {Console.WriteLine("Legacy GDI+ conversion reproduces the diagnostic exception");}
                using var popup=(Form)Activator.CreateInstance(popupType,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,[shell,entries,new Point(100,100)],null)!;
                _=popup.Handle;
                Check(popupType.GetProperty("RenderError")!.GetValue(popup)==null,"Real popup constructs and renders without a font exception: "+(desktop?"desktop":"tray"));
                popupType.GetMethod("SelectRow",flags)!.Invoke(popup,[0]);
                var surface=(Bitmap)popupType.GetField("surface",flags)!.GetValue(popup)!;
                surface.Save(Path.Combine(output,desktop?"desktop.png":"tray.png"),ImageFormat.Png);
                Check(surface.GetPixel(0,0).A==0&&surface.GetPixel(surface.Width-1,surface.Height-1).A==0,"Text painting preserves transparent popup corners");
                var renderer=new NativeMenuText(font);
                Check(renderer.MeasureInk("打开控制中心").Width>40,"Chinese labels produce real glyph pixels");
                popupType.GetMethod("InvokeRow",flags)!.Invoke(popup,[rows.Length-1]);
                Check((int)popupType.GetProperty("Command")!.GetValue(popup)! == 8,"Exit menu command remains selectable: "+(desktop?"desktop":"tray"));
            }finally {Native.DestroyMenu(menu);type.GetMethod("EndMenuTheme",flags)!.Invoke(shell,null);}
        }
        Console.WriteLine($"Desktop menu checks passed: {checks}");
    }
}
