using System.ComponentModel;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace LumaWall.DesktopIntegration;

// GDI accepts the bundled OpenType/CFF font. Converting its HFONT to a GDI+
// Font throws on machines where the private font is actually selected.
internal sealed class NativeMenuText(nint font)
{
    private readonly Dictionary<string,Rectangle> bounds=new();
    [StructLayout(LayoutKind.Sequential)] private struct TextSize {public int Width,Height;}
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc,nint value);
    [DllImport("gdi32.dll")] private static extern int SetBkMode(nint dc,int mode);
    [DllImport("gdi32.dll")] private static extern uint SetTextColor(nint dc,uint color);
    [DllImport("gdi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern bool TextOut(nint dc,int x,int y,string text,int count);
    [DllImport("gdi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern bool GetTextExtentPoint32(nint dc,string text,int count,out TextSize size);
    private void WithFont(Graphics graphics,Action<nint> action)
    {
        var dc=graphics.GetHdc();var previous=SelectObject(dc,font);
        try {action(dc);}finally {SelectObject(dc,previous);graphics.ReleaseHdc(dc);}
    }
    private void Draw(Graphics graphics,string text,int x,int y,Color ink)=>WithFont(graphics,dc=> {
        var background=SetBkMode(dc,1);var color=SetTextColor(dc,(uint)(ink.R|(ink.G<<8)|(ink.B<<16)));
        try {if(!TextOut(dc,x,y,text,text.Length))throw new Win32Exception(Marshal.GetLastWin32Error(),"Menu text drawing failed");}
        finally {SetTextColor(dc,color);SetBkMode(dc,background);}
    });
    internal Rectangle MeasureInk(string text)
    {
        if(bounds.TryGetValue(text,out var cached))return cached;
        TextSize size=default;
        using(var graphics=Graphics.FromHwnd(0))WithFont(graphics,dc=> {
            if(!GetTextExtentPoint32(dc,text,text.Length,out size))throw new Win32Exception(Marshal.GetLastWin32Error());
        });
        const int padding=4;
        using var mask=new Bitmap(Math.Max(1,size.Width+padding*2),Math.Max(1,size.Height+padding*2),PixelFormat.Format24bppRgb);
        using(var graphics=Graphics.FromImage(mask)) {graphics.Clear(Color.Black);Draw(graphics,text,padding,padding,Color.White);}
        int left=mask.Width,top=mask.Height,right=-1,bottom=-1;
        for(int y=0;y<mask.Height;y++)for(int x=0;x<mask.Width;x++) {
            var pixel=mask.GetPixel(x,y);if(pixel.R==0&&pixel.G==0&&pixel.B==0)continue;
            left=Math.Min(left,x);right=Math.Max(right,x);top=Math.Min(top,y);bottom=Math.Max(bottom,y);
        }
        var result=right<0?Rectangle.Empty:new Rectangle(left-padding,top-padding,right-left+1,bottom-top+1);
        bounds[text]=result;return result;
    }
    // Draw on an opaque copy of the already painted background, then restore it
    // without scaling. GDI cannot overwrite the layered surface's alpha channel.
    internal void Paint(Bitmap surface,string text,Point visibleOrigin,Color ink)
    {
        var measured=MeasureInk(text);if(measured.IsEmpty)return;
        var area=Rectangle.Intersect(new Rectangle(visibleOrigin.X-2,visibleOrigin.Y-2,measured.Width+4,measured.Height+4),new Rectangle(Point.Empty,surface.Size));
        if(area.IsEmpty)return;
        using var patch=new Bitmap(area.Width,area.Height,PixelFormat.Format24bppRgb);
        using(var graphics=Graphics.FromImage(patch)) {
            graphics.DrawImage(surface,new Rectangle(Point.Empty,area.Size),area,GraphicsUnit.Pixel);
            Draw(graphics,text,visibleOrigin.X-measured.X-area.X,visibleOrigin.Y-measured.Y-area.Y,ink);
        }
        using var destination=Graphics.FromImage(surface);destination.DrawImageUnscaled(patch,area.Location);
    }
}
