using System.Drawing;

namespace LumaWall.ControlCenter;

internal static class WindowPlacementGeometry
{
    internal const double DefaultWidth=1786,DefaultHeight=1077;
    internal static double ResolutionScale(Rectangle display)=>Math.Clamp(Math.Min(display.Width/2560d,display.Height/1440d),.25,8);
    internal static double StartupSizeFactor(double startupScale,double savedScale,double legacyFactor)=>
        double.IsFinite(savedScale)&&savedScale>0?startupScale/savedScale:legacyFactor;
    internal readonly record struct Display(string Device,Rectangle Bounds,Rectangle WorkingArea);
    internal readonly record struct LogicalBounds(double X,double Y,double Width,double Height);

    internal static bool MatchesMonitor(string savedIdentity,string savedDevice,string identity,string device)
        =>savedIdentity.Length>0
            ?string.Equals(savedIdentity,identity,StringComparison.OrdinalIgnoreCase)
            :string.Equals(savedDevice,device,StringComparison.OrdinalIgnoreCase);

    internal static bool RestoreMaximized(uint showCommand,uint flags,bool lastNonMinimizedMaximized)
        =>showCommand==3||(showCommand==2&&((flags&2)!=0||lastNonMinimizedMaximized));

    internal static bool UsesWorkspaceCoordinates(uint extendedStyle)=>(extendedStyle&0x80)==0;

    // WPF's GetNormalRectDeviceUnits / TransformWorkAreaScreenArea converts
    // WINDOWPLACEMENT with the current HWND monitor's work-area offset:
    // https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/PresentationFramework/System/Windows/Window.cs
    // Apply that offset once, then locate the resulting normal screen rectangle.
    // Its display (and DPI) need not be the current HWND's display.
    internal static bool TryResolveNormalBounds(Rectangle normal,bool workspaceCoordinates,
        IReadOnlyList<Display> displays,string? conversionMonitorDevice,out int displayIndex,out Rectangle screenBounds)
    {
        displayIndex=-1;screenBounds=default;
        if(normal.Width<=0||normal.Height<=0)return false;
        var candidate=normal;
        if(workspaceCoordinates) {
            int conversionIndex=-1;
            for(int i=0;i<displays.Count;i++)
                if(string.Equals(displays[i].Device,conversionMonitorDevice,StringComparison.OrdinalIgnoreCase)) {conversionIndex=i;break;}
            // A display change can invalidate the HWND monitor snapshot. Keep
            // the old placement instead of guessing a workspace origin.
            if(conversionIndex<0)return false;
            var conversionDisplay=displays[conversionIndex];
            candidate.Offset(conversionDisplay.WorkingArea.Left-conversionDisplay.Bounds.Left,
                conversionDisplay.WorkingArea.Top-conversionDisplay.Bounds.Top);
        }
        long bestIntersection=-1,bestDistance=long.MaxValue;
        bool bestPreferred=false;
        for(int i=0;i<displays.Count;i++) {
            var display=displays[i];
            long intersection=IntersectionArea(candidate,display.Bounds);
            long distance=DistanceSquared(candidate,display.Bounds);
            bool preferred=string.Equals(display.Device,conversionMonitorDevice,StringComparison.OrdinalIgnoreCase);
            if(intersection<bestIntersection
                ||(intersection==bestIntersection&&distance>bestDistance)
                ||(intersection==bestIntersection&&distance==bestDistance&&(!preferred||bestPreferred)))continue;
            bestIntersection=intersection;bestDistance=distance;bestPreferred=preferred;
            displayIndex=i;screenBounds=candidate;
        }
        return displayIndex>=0;
    }

    internal static LogicalBounds ToLogical(Rectangle screenBounds,Rectangle workArea,double scale)
        =>new((screenBounds.Left-workArea.Left)/scale,(screenBounds.Top-workArea.Top)/scale,
            screenBounds.Width/scale,screenBounds.Height/scale);

    internal static Rectangle Restore(LogicalBounds? saved,Rectangle workArea,double scale,bool center)
    {
        double wantedWidth=(saved?.Width??DefaultWidth)*scale,wantedHeight=(saved?.Height??DefaultHeight)*scale;
        double fit=Math.Min(1,Math.Min(workArea.Width/wantedWidth,workArea.Height/wantedHeight));
        int width=Math.Clamp((int)Math.Round(wantedWidth*fit),1,workArea.Width);
        int height=Math.Clamp((int)Math.Round(wantedHeight*fit),1,workArea.Height);
        int x=center?(workArea.Width-width)/2:Math.Clamp((int)Math.Round((saved?.X??0)*scale),0,workArea.Width-width);
        int y=center?(workArea.Height-height)/2:Math.Clamp((int)Math.Round((saved?.Y??0)*scale),0,workArea.Height-height);
        return new(workArea.Left+x,workArea.Top+y,width,height);
    }

    private static long IntersectionArea(Rectangle first,Rectangle second)
    {
        long width=Math.Max(0L,Math.Min((long)first.Right,second.Right)-Math.Max((long)first.Left,second.Left));
        long height=Math.Max(0L,Math.Min((long)first.Bottom,second.Bottom)-Math.Max((long)first.Top,second.Top));
        return width*height;
    }

    private static long DistanceSquared(Rectangle first,Rectangle second)
    {
        long x=Math.Max(0L,Math.Max((long)second.Left-first.Right,(long)first.Left-second.Right));
        long y=Math.Max(0L,Math.Max((long)second.Top-first.Bottom,(long)first.Top-second.Bottom));
        return x*x+y*y;
    }
}
