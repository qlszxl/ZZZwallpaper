using System.ComponentModel;
using System.Runtime.InteropServices;

namespace LumaWall.DesktopIntegration;

public sealed partial class DesktopShell
{
    // Top-down premultiplied-alpha DIB for our own layered popup.
    private sealed class MenuRowBuffer : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo
        {
            public uint Size;
            public int Width, Height;
            public ushort Planes, BitCount;
            public uint Compression, SizeImage;
            public int XPelsPerMeter, YPelsPerMeter;
            public uint ColorsUsed, ColorsImportant, Color;
        }
        [DllImport("gdi32.dll", SetLastError = true)] private static extern nint CreateCompatibleDC(nint dc);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);
        [DllImport("gdi32.dll", SetLastError = true)] private static extern nint CreateDIBSection(nint dc, ref BitmapInfo info, uint usage, out nint bits, nint section, uint offset);
        private nint bitmap, previousBitmap;
        public nint Dc { get; private set; }
        public nint Bits {get;}
        [StructLayout(LayoutKind.Sequential)] private struct Blend {public byte Operation,Flags,Alpha,Format;}
        [StructLayout(LayoutKind.Sequential)] private struct Size {public int Width,Height;}
        [DllImport("user32.dll",SetLastError=true)] private static extern bool UpdateLayeredWindow(nint window,nint destination,nint location,ref Size size,nint source,ref Native.Point sourcePoint,uint color,ref Blend blend,uint flags);
        public bool PresentLayered(nint window) {
            var size=new Size {Width=width,Height=height};var origin=new Native.Point(0,0);var blend=new Blend {Alpha=255,Format=1};
            return UpdateLayeredWindow(window,0,0,ref size,Dc,ref origin,0,ref blend,2);
        }
        private readonly int width, height;

        public MenuRowBuffer(nint target, int width, int height)
        {
            this.width = width; this.height = height;
            Dc = CreateCompatibleDC(target);
            if (Dc == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            var info = new BitmapInfo { Size = 40, Width = width, Height = -height, Planes = 1, BitCount = 32 };
            bitmap = CreateDIBSection(target, ref info, 0, out nint bits, 0, 0);
            Bits=bits;
            if (bitmap == 0)
            {
                int error = Marshal.GetLastWin32Error(); Dispose(); throw new Win32Exception(error);
            }
            previousBitmap = SelectObject(Dc, bitmap);
        }


        public void Dispose()
        {
            if (previousBitmap != 0) { SelectObject(Dc, previousBitmap); previousBitmap = 0; }
            if (bitmap != 0) { DeleteObject(bitmap); bitmap = 0; }
            if (Dc != 0) { DeleteDC(Dc); Dc = 0; }
        }
    }
}
