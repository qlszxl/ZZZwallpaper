using System.Drawing;
using LumaWall.DesktopIntegration;

internal static class MenuPlacementChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var work=new Rectangle(0,0,1920,1040);
        var size=new Size(240,128);
        check(MenuPopupPlacement.Locate(new Point(100,120),size,work)==new Point(100,120),"popup anchors at invocation in available space");
        check(MenuPopupPlacement.Locate(new Point(1900,1030),size,work)==new Point(1660,902),"popup flips left/up at bottom-right without covering anchor");
        check(MenuPopupPlacement.Locate(new Point(1900,1060),size,work)==new Point(1660,912),"tray invocation stays inside working area above taskbar");
        check(MenuPopupPlacement.Locate(new Point(-10,-10),size,new Rectangle(-2560,-1440,2560,1440))==new Point(-250,-138),"popup preserves negative coordinates on secondary monitor");
        check(MenuPopupPlacement.Locate(new Point(-2555,-1435),size,new Rectangle(-2560,-1440,2560,1440))==new Point(-2555,-1435),"top-left secondary anchor stays beside pointer");
        check(MenuPopupPlacement.Locate(new Point(100,90),new Size(320,300),new Rectangle(0,0,200,100))==Point.Empty,"oversized popup clamps without invalid bounds");
    }
}
