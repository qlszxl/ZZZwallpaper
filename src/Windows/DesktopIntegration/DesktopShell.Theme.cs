using LumaWall.Contracts;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace LumaWall.DesktopIntegration;

public sealed partial class DesktopShell
{
    private readonly Dictionary<uint,string> menuLabels=new();
    private nint menuFontHandle;
    private string? registeredMenuFont;
    private int menuRowHeight,menuWidth;
    private float menuScale;
    private bool menuAnimationsEnabled,appearanceMotion=true;
    private readonly Stopwatch menuClock=new();
    private Color MenuBackground=Color.FromArgb(12,12,12),MenuSurface=Color.FromArgb(27,27,27);
    private Color MenuYellow=Color.FromArgb(244,230,0),MenuGreen=Color.FromArgb(134,235,22);
    private sealed record MenuEntry(uint Id,string Label,bool Enabled,bool Separator);
    public void ApplyAppearance(AppearanceOptions appearance) {
        var skin=SkinPalette.Find(appearance.Skin);MenuBackground=ColorTranslator.FromHtml(skin.Background);MenuSurface=ColorTranslator.FromHtml(skin.Surface);
        MenuYellow=ColorTranslator.FromHtml(skin.Accent);MenuGreen=ColorTranslator.FromHtml(skin.AccentEnd);appearanceMotion=appearance.Motion;
    }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] private struct ItemInfo {
        public uint Size,Mask,Type,State,Id;
        public nint Submenu,Checked,Unchecked;
        public nuint Data;
        public nint Text;
        public uint TextLength;
        public nint Bitmap;
    }
    [StructLayout(LayoutKind.Sequential)] private struct TextSize {public int Width,Height;}
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern bool GetMenuItemInfo(nint menu,uint item,bool byPosition,ref ItemInfo info);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint value);
    [DllImport("gdi32.dll",CharSet=CharSet.Unicode)] private static extern int AddFontResourceEx(string path,uint flags,nint reserved);
    [DllImport("gdi32.dll",CharSet=CharSet.Unicode)] private static extern bool RemoveFontResourceEx(string path,uint flags,nint reserved);
    [DllImport("gdi32.dll",CharSet=CharSet.Unicode)] private static extern nint CreateFont(int height,int width,int escapement,int orientation,int weight,uint italic,uint underline,uint strikeout,uint charset,uint outputPrecision,uint clipPrecision,uint quality,uint pitch,string face);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc,nint value);
    [DllImport("gdi32.dll",CharSet=CharSet.Unicode)] private static extern bool GetTextExtentPoint32(nint dc,string text,int count,out TextSize size);
    [DllImport("user32.dll")] private static extern nint GetDC(nint window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint window,nint dc);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern bool SystemParametersInfo(uint action,uint parameter,[MarshalAs(UnmanagedType.Bool)] out bool value,uint flags);
    private int MenuPixel(float dip)=>Math.Max(1,(int)Math.Round(dip*menuScale));
    private MenuEntry[] PrepareMenu(nint menu,Rectangle workArea) {
        var entries=new List<MenuEntry>();
        for(uint i=0;i<Native.GetMenuItemCount(menu);i++) {
            var item=new ItemInfo {Size=(uint)Marshal.SizeOf<ItemInfo>(),Mask=0x103};
            if(!GetMenuItemInfo(menu,i,true,ref item))throw new InvalidOperationException(UiLanguage.Text("无法读取菜单项"));
            bool separator=(item.Type&0x800)!=0;
            entries.Add(new(item.Id,separator?"":menuLabels[item.Id],(item.State&3)==0,separator));
        }
        // Compact dimensions are native DIPs, never a scaled bitmap or 80% font transform.
        // DPI is the only scale; each surface is rasterized at its final pixel size.
        menuScale=Math.Max(1,GetDpiForWindow(Handle)/96f);
        menuAnimationsEnabled=appearanceMotion&&(!SystemParametersInfo(0x1042,0,out bool animations,0)||animations);
        menuRowHeight=MenuPixel(36);menuWidth=MenuPixel(240);
        string path=Path.Combine(AppContext.BaseDirectory,"Assets","Fonts","SourceHanSansSC-Medium.otf");
        bool loaded=File.Exists(path)&&AddFontResourceEx(path,0x10,0)>0;if(loaded)registeredMenuFont=path;
        menuFontHandle=CreateFont(-MenuPixel(14),0,0,0,500,0,0,0,134,8,0,4,0,loaded?"Source Han Sans SC Medium":"Microsoft YaHei UI");
        if(menuFontHandle==0)throw new InvalidOperationException("Could not create desktop menu font");
        nint dc=GetDC(Handle);
        try {
            nint previous=dc!=0?SelectObject(dc,menuFontHandle):0;
            try {foreach(var entry in entries)if(dc!=0&&GetTextExtentPoint32(dc,entry.Label,entry.Label.Length,out var size))menuWidth=Math.Max(menuWidth,size.Width+MenuPixel(54));}
            finally {if(dc!=0&&previous!=0)SelectObject(dc,previous);}
        } finally {if(dc!=0)ReleaseDC(Handle,dc);}
        menuWidth=Math.Min(menuWidth,Math.Max(1,workArea.Width-8));
        if(menuAnimationsEnabled)menuClock.Restart();
        return entries.ToArray();
    }
    private Color MenuAccent() {
        double phase=menuClock.IsRunning?menuClock.Elapsed.TotalSeconds%2:0,t=phase<=1?phase:2-phase;
        return Color.FromArgb((int)Math.Round(MenuYellow.R+(MenuGreen.R-MenuYellow.R)*t),(int)Math.Round(MenuYellow.G+(MenuGreen.G-MenuYellow.G)*t),(int)Math.Round(MenuYellow.B+(MenuGreen.B-MenuYellow.B)*t));
    }
    private void EndMenuTheme() {
        menuClock.Reset();
        if(menuFontHandle!=0) {DeleteObject(menuFontHandle);menuFontHandle=0;}
        if(registeredMenuFont!=null) {RemoveFontResourceEx(registeredMenuFont,0x10,0);registeredMenuFont=null;}
    }
    private static GraphicsPath MenuRoundedRectangle(RectangleF rectangle, float radius)
    {
        radius = Math.Min(radius, Math.Min(rectangle.Width, rectangle.Height) / 2);
        float diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure(); return path;
    }
}
