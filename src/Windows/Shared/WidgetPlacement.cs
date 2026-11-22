using System.Runtime.InteropServices;
using LumaWall.Contracts;
using Screen=System.Windows.Forms.Screen;

namespace LumaWall.WidgetSupport;

// Shared by the settings editor and desktop host; offsets are physical pixels
// from the selected work area's top-left, excluding the taskbar.
internal static class WidgetPlacement
{
    [StructLayout(LayoutKind.Sequential)] private struct Point {public int X,Y;}
    [DllImport("user32.dll")] private static extern nint MonitorFromPoint(Point point,uint flags);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(nint monitor,int kind,out uint x,out uint y);
    internal static Screen Resolve(string? name)=>Screen.AllScreens.FirstOrDefault(s=>s.DeviceName==name)??Screen.PrimaryScreen??Screen.AllScreens[0];
    internal static (double Width,double Height) LogicalSize(WidgetInstance widget)=>widget.Kind switch {
        WidgetKind.Extension when FeaturePacks.GridFor(widget) is {} grid=>grid.Size(widget),
        WidgetKind.Extension or WidgetKind.Text or WidgetKind.Sticker or WidgetKind.Mask or WidgetKind.Clock or WidgetKind.Countdown or WidgetKind.FocusTimer=>(widget.Visual.Width,widget.Visual.Height),WidgetKind.Spectrum=>SpectrumLayout.Size(widget.Spectrum),WidgetKind.Search=>(widget.Search.Width,widget.Search.Height),WidgetKind.Performance=>PerformanceSize(widget),_=>(widget.IconOnly?44:148,44)
    };
    private static (double Width,double Height) PerformanceSize(WidgetInstance widget)
    {
        int count=widget.Performance.Selected(widget.Metrics).Length,columns=widget.Performance.ColumnCount(count);
        return(24+116*columns,24+48*((count+columns-1)/columns));
    }
    internal static (int Width,int Height,int MaxX,int MaxY) Measure(WidgetInstance widget,Screen screen)
        =>Measure(widget,screen,Scale(screen));
    // The host and the alignment editor use the same physical body bounds, excluding decorative overflow.
    internal static System.Drawing.Rectangle BodyBounds(WidgetInstance placed,Screen screen)
    {
        var area=Area(placed,screen);
        var size=Measure(placed,screen);
        return new(area.Left+(int)Math.Round(placed.X*size.MaxX),area.Top+(int)Math.Round(placed.Y*size.MaxY),size.Width,size.Height);
    }
    private static (int Width,int Height,int MaxX,int MaxY) Measure(WidgetInstance widget,Screen screen,double scale)
    {
        var area=Area(widget,screen);
        var size=LogicalSize(widget);
        if(FeaturePacks.GridFor(widget)!=null) {
            double fit=Math.Min(1,Math.Min(area.Width/(size.Width*scale),area.Height/(size.Height*scale)));
            int fittedWidth=Math.Max(1,(int)Math.Round(size.Width*scale*fit)),fittedHeight=Math.Max(1,(int)Math.Round(size.Height*scale*fit));
            return(fittedWidth,fittedHeight,Math.Max(0,area.Width-fittedWidth),Math.Max(0,area.Height-fittedHeight));
        }
        int width=Math.Min(area.Width,(int)Math.Round(size.Width*scale)),height=Math.Min(area.Height,(int)Math.Round(size.Height*scale));
        return(width,height,Math.Max(0,area.Width-width),Math.Max(0,area.Height-height));
    }
    internal static System.Drawing.Rectangle Area(WidgetInstance widget,Screen screen)=>widget.LayoutGroupId==null&&FeaturePacks.GridFor(widget)==null&&widget.Kind is (WidgetKind.Spectrum or WidgetKind.Extension or WidgetKind.Text or WidgetKind.Sticker or WidgetKind.Mask or WidgetKind.Clock or WidgetKind.Countdown or WidgetKind.FocusTimer)?screen.Bounds:screen.WorkingArea;
    internal static (int Width,int Height) MaximumSize(WidgetInstance widget,Screen screen) {var area=Area(widget,screen);double scale=Scale(screen);return(Math.Clamp((int)(area.Width/scale),24,16384),Math.Clamp((int)(area.Height/scale),24,16384));}
    internal static double Scale(Screen screen)
    {
        var bounds=screen.Bounds;
        nint monitor=MonitorFromPoint(new Point {X=bounds.Left+bounds.Width/2,Y=bounds.Top+bounds.Height/2},2);
        return GetDpiForMonitor(monitor,0,out uint dpi,out _)==0?Math.Max(96,dpi)/96.0:1;
    }
    internal sealed record GroupBounds(int Left,int Top,int Width,int Height,int MaxX,int MaxY,WidgetGroupLayout.Slot[] Slots);
    internal static GroupBounds MeasureGroup(WidgetConfiguration state,WidgetLayoutGroup group)
    {
        var screen=Resolve(group.Screen);var area=screen.WorkingArea;double scale=Scale(screen);
        var items=state.Widgets.Where(w=>w.LayoutGroupId==group.Id&&w.Enabled).OrderBy(w=>w.LayoutOrder).ThenBy(w=>w.Id,StringComparer.Ordinal).Select(w=> {var s=Measure(w,screen,scale);return new WidgetGroupLayout.Item(w.Id,s.Width,s.Height);}).ToArray();
        var layout=WidgetGroupLayout.Arrange(group,items,area.Width,area.Height,(int)Math.Round(group.Gap*scale));
        int maxX=Math.Max(0,area.Width-layout.Width),maxY=Math.Max(0,area.Height-layout.Height);
        return new(area.Left+(int)Math.Round(group.X*maxX),area.Top+(int)Math.Round(group.Y*maxY),layout.Width,layout.Height,maxX,maxY,layout.Slots);
    }
    internal static WidgetInstance Effective(WidgetInstance widget,WidgetConfiguration state)
    {
        var group=state.LayoutGroups.FirstOrDefault(g=>g.Id==widget.LayoutGroupId);if(group==null)return widget;
        return Effective(widget,Resolve(group.Screen),MeasureGroup(state,group));
    }
    internal static Dictionary<string,WidgetInstance> EffectiveAll(WidgetConfiguration state)
    {
        var result=state.Widgets.ToDictionary(w=>w.Id);
        foreach(var group in state.LayoutGroups) {
            var screen=Resolve(group.Screen);var bounds=MeasureGroup(state,group);
            foreach(var widget in state.Widgets.Where(w=>w.LayoutGroupId==group.Id))result[widget.Id]=Effective(widget,screen,bounds);
        }
        return result;
    }
    internal static Dictionary<string,WidgetInstance> EffectiveChanges(WidgetConfiguration state,WidgetConfiguration previous,IReadOnlyDictionary<string,WidgetInstance> cached,bool force)
    {
        var result=state.Widgets.ToDictionary(w=>w.Id);
        var oldWidgets=previous.Widgets.ToDictionary(w=>w.Id);
        var oldGroups=previous.LayoutGroups.ToDictionary(g=>g.Id);
        foreach(var group in state.LayoutGroups) {
            var members=state.Widgets.Where(w=>w.LayoutGroupId==group.Id).ToArray();
            bool reflow=force||!oldGroups.TryGetValue(group.Id,out var oldGroup)||oldGroup!=group
                ||previous.Widgets.Count(w=>w.LayoutGroupId==group.Id)!=members.Length
                ||members.Any(w=>!oldWidgets.TryGetValue(w.Id,out var old)||old.LayoutGroupId!=w.LayoutGroupId||old.Enabled!=w.Enabled||old.LayoutOrder!=w.LayoutOrder||LogicalSize(old)!=LogicalSize(w)||!cached.ContainsKey(w.Id));
            if(reflow) {
                var screen=Resolve(group.Screen);var bounds=MeasureGroup(state,group);
                foreach(var widget in members)result[widget.Id]=Effective(widget,screen,bounds);
            } else foreach(var widget in members) {
                var effective=cached[widget.Id];
                result[widget.Id]=ReferenceEquals(widget,oldWidgets[widget.Id])?effective:widget with {Screen=effective.Screen,X=effective.X,Y=effective.Y};
            }
        }
        return result;
    }
    private static WidgetInstance Effective(WidgetInstance widget,Screen screen,GroupBounds bounds)
    {
        var area=screen.WorkingArea;var slot=bounds.Slots.FirstOrDefault(s=>s.Id==widget.Id);
        if(slot==null)return widget with {Screen=screen.DeviceName};
        // These are transient host coordinates, never persisted as member layout.
        // Keep relative positions even on an exceptionally small work area; clamping
        // each member separately would stack unrelated controls on top of each other.
        return widget with {Screen=screen.DeviceName,X=(bounds.Left-area.Left+slot.X)/(double)Math.Max(1,area.Width-slot.Width),Y=(bounds.Top-area.Top+slot.Y)/(double)Math.Max(1,area.Height-slot.Height)};
    }
}
