using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace LumaWall.DesktopIntegration;

/// <summary>Removes the empty area above the shell while keeping one animated HWND.</summary>
public sealed class WordmarkWindowRegion : IDisposable
{
    private nint window;
    private readonly byte[] silhouette;
    private readonly int imageWidth, imageHeight;
    private Layout? applied;
    private bool updating, disposed, hasRegion;
    private readonly SubclassProc resizeProc;
    private readonly record struct Layout(int Left, int Right, int Bottom, int Top, int X, int Y, int ImageWidth, int ImageHeight, int TopRadius, int BottomRadius, int Grip);
    private delegate nint SubclassProc(nint hwnd, uint message, nuint wparam, nint lparam, nuint id, nuint data);
    private const nuint SubclassId = 0x5A5752;
    private const uint WmNcPaint=0x0085,WmNcActivate=0x0086;
    private const uint WmNcUahDrawCaption=0x00AE,WmNcUahDrawFrame=0x00AF;
    [StructLayout(LayoutKind.Sequential)] private struct Margins {public int Left,Right,Top,Bottom;}
    [StructLayout(LayoutKind.Sequential)] private struct BlurBehind {public uint Flags;public int Enabled;public nint Region;public int Transition;}

    [StructLayout(LayoutKind.Sequential)]
    private struct Transform { public float M11, M12, M21, M22, Dx, Dy; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out Native.Rect rectangle);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint hwnd, out Native.Rect rectangle);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsZoomed(nint hwnd);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(nint hwnd, nint region, bool redraw);
    [DllImport("gdi32.dll", EntryPoint = "ExtCreateRegion")] private static extern nint CreateRegion(nint transform, uint size, byte[] data);
    [DllImport("gdi32.dll", EntryPoint = "ExtCreateRegion")] private static extern nint TransformRegion(ref Transform transform, uint size, byte[] data);
    [DllImport("gdi32.dll")] private static extern int CombineRgn(nint destination, nint first, nint second, int mode);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint handle);
    [DllImport("gdi32.dll")] private static extern nint CreateRectRgn(int left,int top,int right,int bottom);
    [DllImport("gdi32.dll")] private static extern int OffsetRgn(nint region,int x,int y);
    [DllImport("gdi32.dll")] private static extern nint GetStockObject(int index);
    [DllImport("user32.dll")] private static extern int FillRect(nint dc,ref Native.Rect rect,nint brush);
    [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(nint hwnd,ref Margins margins);
    [DllImport("dwmapi.dll")] private static extern int DwmEnableBlurBehindWindow(nint hwnd,ref BlurBehind settings);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint hwnd,int attribute,ref uint value,int size);
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(nint hwnd, SubclassProc callback, nuint id, nuint data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(nint hwnd, SubclassProc callback, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint hwnd, uint message, nuint wparam, nint lparam);

    public WordmarkWindowRegion(nint window, string imagePath)
    {
        this.window = window;
        using var source = new Bitmap(imagePath);
        imageWidth = source.Width; imageHeight = source.Height;
        using var bitmap = new Bitmap(imageWidth, imageHeight, PixelFormat.Format32bppArgb);
        using(var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.DrawImageUnscaled(source, 0, 0);
        }
        var rectangles = new List<Native.Rect>();
        var bits = bitmap.LockBits(new Rectangle(0, 0, imageWidth, imageHeight), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[imageWidth * 4];
            for(int y = 0; y < imageHeight; y++)
            {
                Marshal.Copy(bits.Scan0 + y * bits.Stride, row, 0, row.Length);
                int start = -1;
                for(int x = 0; x <= imageWidth; x++)
                {
                    // Keep the black extrusion as well as the white letters;
                    // preserve alpha edge pixels rather than using a colour key.
                    bool visible = x < imageWidth && row[x * 4 + 3] > 0;
                    if(visible && start < 0) start = x;
                    else if(!visible && start >= 0)
                    {
                        rectangles.Add(Rect(start, y, x, y + 1)); start = -1;
                    }
                }
            }
        }
        finally { bitmap.UnlockBits(bits); }
        if(rectangles.Count == 0) throw new InvalidDataException("The wordmark has no visible silhouette.");
        silhouette = RegionData(rectangles);
        resizeProc = HitTest;
        if(!SetWindowSubclass(window, resizeProc, SubclassId, 0))
            throw new InvalidOperationException("Cannot register the window resize border.");
        EnableAlphaComposition();
    }

    private void EnableAlphaComposition()
    {
        // An empty blur region enables per-pixel DWM composition without
        // blurring the desktop. The XAML backdrop itself remains transparent.
        var margins=new Margins();DwmExtendFrameIntoClientArea(window,ref margins);
        nint empty=CreateRectRgn(-2,-2,-1,-1);
        try {var blur=new BlurBehind {Flags=3,Enabled=1,Region=empty};DwmEnableBlurBehindWindow(window,ref blur);}
        finally {if(empty!=0)DeleteObject(empty);}
        uint noBorder=0xFFFFFFFE,noRounding=1;
        DwmSetWindowAttribute(window,34,ref noBorder,4);DwmSetWindowAttribute(window,33,ref noRounding,4);
    }

    // Parameters are physical pixels relative to the XAML client area.
    public void Update(Rectangle bodyPixels, Rectangle logoPixels, double scale)
    {
        if(disposed || updating || window == 0 || IsIconic(window) || logoPixels.Width <= 0 || logoPixels.Height <= 0) return;
        var origin = new Native.Point(0, 0);
        if(!Native.ClientToScreen(window, ref origin) || !GetWindowRect(window, out var bounds) || !GetClientRect(window, out var client)) return;
        if(client.Right <= 0 || client.Bottom <= 0) return;
        // SetWindowRgn is in window coordinates, but only the XAML client area
        // belongs to the visible shell. Including the native resize frame exposes
        // a light non-client rim once a custom region disables DWM's usual frame.
        var clientBounds = new Rectangle(origin.X - bounds.Left, origin.Y - bounds.Top, client.Right, client.Bottom);
        bodyPixels.Offset(clientBounds.Location);
        logoPixels.Offset(clientBounds.Location);
        bodyPixels.Intersect(clientBounds);
        if(bodyPixels.Width <= 0 || bodyPixels.Height <= 0) return;
        var layout = new Layout(bodyPixels.Left, bodyPixels.Right, bodyPixels.Bottom, bodyPixels.Top,
            logoPixels.X, logoPixels.Y, logoPixels.Width, logoPixels.Height,
            (int)Math.Round(22 * scale), (int)Math.Round(8 * scale), Math.Max(1, (int)Math.Round(8 * scale)));
        if(applied == layout) return;
        updating = true;
        nint region = 0, logo = 0;
        try
        {
            byte[] body = BodyData(layout);
            region = CreateRegion(0, (uint)body.Length, body);
            var transform = new Transform { M11 = layout.ImageWidth / (float)imageWidth, M22 = layout.ImageHeight / (float)imageHeight, Dx = layout.X, Dy = layout.Y };
            logo = TransformRegion(ref transform, (uint)silhouette.Length, silhouette);
            // Only the logo needs a guard so DWM can blend the filtered PNG edge.
            if(logo!=0)
            {
                nint guard=CreateRectRgn(0,0,0,0);
                try {for(int dy=-2;dy<=2;dy++)for(int dx=-2;dx<=2;dx++) {CombineRgn(guard,logo,0,5);OffsetRgn(guard,dx,dy);CombineRgn(region,region,guard,2);}}
                finally {if(guard!=0)DeleteObject(guard);}
            }
            if(region == 0 || logo == 0 || CombineRgn(region, region, logo, 2) == 0) // RGN_OR
                throw new InvalidOperationException("Cannot create the shaped window region.");
            nint clientRegion=CreateRectRgn(clientBounds.Left,clientBounds.Top,clientBounds.Right,clientBounds.Bottom);
            try {if(clientRegion==0||CombineRgn(region,region,clientRegion,1)==0)throw new InvalidOperationException("Cannot bound the window region.");}
            finally {if(clientRegion!=0)DeleteObject(clientRegion);}
            // SetWindowRgn emits window-position messages. Guard and cache before
            // the call so layout callbacks do not recursively rebuild the region.
            applied = layout;
            if(SetWindowRgn(window, region, true) == 0)
            {
                applied = null;
                throw new InvalidOperationException("Cannot apply the shaped window region.");
            }
            hasRegion = true;
            region = 0; // Windows owns it after success; never delete this handle.
        }
        finally
        {
            if(region != 0) DeleteObject(region);
            if(logo != 0) DeleteObject(logo);
            updating = false;
        }
    }

    private static byte[] BodyData(Layout layout)
    {
        var rectangles = new List<Native.Rect>();
        int maxRadius = Math.Min((layout.Right - layout.Left) / 2, (layout.Bottom - layout.Top) / 2);
        int topRadius = Math.Clamp(layout.TopRadius, 0, maxRadius), bottomRadius = Math.Clamp(layout.BottomRadius, 0, maxRadius);
        for(int y = 0; y < topRadius; y++)
        {
            int inset = CornerInset(topRadius, y);
            rectangles.Add(Rect(layout.Left + inset, layout.Top + y, layout.Right - inset, layout.Top + y + 1));
        }
        if(layout.Top + topRadius < layout.Bottom - bottomRadius)
            rectangles.Add(Rect(layout.Left, layout.Top + topRadius, layout.Right, layout.Bottom - bottomRadius));
        for(int y = 0; y < bottomRadius; y++)
        {
            int inset = CornerInset(bottomRadius, bottomRadius - y - 1);
            rectangles.Add(Rect(layout.Left + inset, layout.Bottom - bottomRadius + y, layout.Right - inset, layout.Bottom - bottomRadius + y + 1));
        }
        return RegionData(rectangles);
    }

    private static int CornerInset(int radius, int row)
    {
        double dy = radius - row - .5;
        return Math.Max(0, (int)Math.Ceiling(radius - Math.Sqrt(radius * radius - dy * dy) - .5));
    }

    private nint HitTest(nint hwnd, uint message, nuint wparam, nint lparam, nuint id, nuint data)
    {
        if(message==0x0014&&GetClientRect(hwnd,out var erase)) {FillRect((nint)wparam,ref erase,GetStockObject(4));return 1;}
        if(message==0x031E)EnableAlphaComposition();
        if(!disposed&&applied is not null&&
            (message is WmNcPaint or WmNcActivate or WmNcUahDrawCaption or WmNcUahDrawFrame)&&!IsIconic(hwnd))
        {
            // A shaped HWND can fall back to USER/theme frame painting even
            // with DWMWA_BORDER_COLOR=NONE. That frame follows the PNG region,
            // creating a pale outline around the letters. XAML owns the frame.
            if(message is WmNcPaint or WmNcUahDrawCaption or WmNcUahDrawFrame)return 0;
            // Keep activation processing, but explicitly suppress its frame
            // repaint. Do not disable DWM composition: alpha needs it.
            if(message==WmNcActivate)return DefSubclassProc(hwnd,message,wparam,-1);
        }
        nint result = DefSubclassProc(hwnd, message, wparam, lparam);
        // With the native frame clipped away, keep a narrow resize grip INSIDE
        // this window. Never change cursor handling for desktop or tray menus.
        if(message != 0x0084 || disposed || !hasRegion || IsZoomed(hwnd) || applied is not { } layout ||
            (Native.GetWindowLongPtr(hwnd, -16).ToInt64() & 0x00040000) == 0 ||
            !GetWindowRect(hwnd, out var bounds)) return result;
        int x = unchecked((short)(lparam.ToInt64() & 0xffff)) - bounds.Left;
        int y = unchecked((short)((lparam.ToInt64() >> 16) & 0xffff)) - bounds.Top;
        if(x < layout.Left || x >= layout.Right || y < layout.Top || y >= layout.Bottom) return result;
        int corner=Math.Max(layout.TopRadius+layout.Grip,layout.Grip*3);
        int fromLeft=x-layout.Left,fromRight=layout.Right-1-x,fromTop=y-layout.Top,fromBottom=layout.Bottom-1-y;
        // Rounded corners have no pixels in the old 6px square. Recognise the
        // diagonal band along the visible arc before ordinary edge/caption hits.
        if(fromTop<corner) {
            if(fromLeft<corner&&fromTop+fromLeft<corner+layout.Grip)return 13;
            if(fromRight<corner&&fromTop+fromRight<corner+layout.Grip)return 14;
        }
        if(fromBottom<corner) {
            if(fromLeft<corner&&fromBottom+fromLeft<corner+layout.Grip)return 16;
            if(fromRight<corner&&fromBottom+fromRight<corner+layout.Grip)return 17;
        }
        bool left = x < layout.Left + layout.Grip, right = x >= layout.Right - layout.Grip;
        bool bottom = y >= layout.Bottom - layout.Grip;
        bool top = y < layout.Top + layout.Grip && (x < layout.X || x >= layout.X + layout.ImageWidth);
        if(top) return left ? 13 : right ? 14 : 12;
        if(bottom) return left ? 16 : right ? 17 : 15;
        return left ? 10 : right ? 11 : result;
    }
    private static Native.Rect Rect(int left, int top, int right, int bottom) => new() { Left = left, Top = top, Right = right, Bottom = bottom };
    private static byte[] RegionData(List<Native.Rect> rectangles)
    {
        using var stream = new MemoryStream(32 + rectangles.Count * 16);
        using var writer = new BinaryWriter(stream);
        writer.Write(32); writer.Write(1); writer.Write(rectangles.Count); writer.Write(rectangles.Count * 16);
        writer.Write(rectangles.Min(r => r.Left)); writer.Write(rectangles.Min(r => r.Top));
        writer.Write(rectangles.Max(r => r.Right)); writer.Write(rectangles.Max(r => r.Bottom));
        foreach(var rectangle in rectangles)
        {
            writer.Write(rectangle.Left); writer.Write(rectangle.Top); writer.Write(rectangle.Right); writer.Write(rectangle.Bottom);
        }
        return stream.ToArray();
    }

    private void Clear()
    {
        if(!hasRegion || window == 0) return;
        hasRegion = false;
        applied = null;
        SetWindowRgn(window, 0, true);
    }
    public void Dispose()
    {
        if(disposed) return;
        disposed = true;
        RemoveWindowSubclass(window, resizeProc, SubclassId);
        Clear(); window = 0;
    }
}
