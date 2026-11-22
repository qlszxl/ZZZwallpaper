using System.Drawing;

namespace LumaWall.DesktopIntegration;

// Physical screen coordinates, including monitors to the left/above the primary.
// Keep a menu edge at the invocation point; flip when there is no room there.
internal static class MenuPopupPlacement
{
    internal static Point Locate(Point anchor, Size menu, Rectangle work)
    {
        int x=anchor.X+menu.Width<=work.Right?anchor.X:anchor.X-menu.Width;
        int y=anchor.Y+menu.Height<=work.Bottom?anchor.Y:anchor.Y-menu.Height;
        return new Point(Math.Clamp(x,work.Left,Math.Max(work.Left,work.Right-menu.Width)),
            Math.Clamp(y,work.Top,Math.Max(work.Top,work.Bottom-menu.Height)));
    }
}
