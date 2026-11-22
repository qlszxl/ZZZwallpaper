using LumaWall.ControlCenter;
using System.Drawing;
int checks=0;
void Check(bool value,string text) {if(!value)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
string folder=Path.GetFullPath(Path.Combine("artifacts","refinement81-tests",Guid.NewGuid().ToString("N")));Directory.CreateDirectory(folder);
Refinement81Checks.Run(folder,Check);
foreach(var display in new[]{new Rectangle(0,0,1920,1080),new Rectangle(-2560,0,2560,1440),new Rectangle(0,0,3840,2160),new Rectangle(0,0,3440,1440),new Rectangle(0,0,1080,1920)}) {
    foreach(double dpi in new[]{1d,1.25,1.5,2}) {
        var area=display with {Height=display.Height-48};double scale=WindowPlacementGeometry.ResolutionScale(display);
        var bounds=WindowPlacementGeometry.Restore(new(0,0,1786*scale/dpi,1077*scale/dpi),area,dpi,true);
        Check(area.Contains(bounds),"Proportional window fits work area: "+display.Size+" / "+dpi);
        Check(Math.Abs(bounds.Width/(double)bounds.Height-1786d/1077)<.003,"Window retains aspect at "+display.Size+" / "+dpi);
    }
}
var fitted=WindowPlacementGeometry.Restore(new(0,0,3000,1800),new(0,0,1366,720),1,true);
Check(Math.Abs(fitted.Width/(double)fitted.Height-3000d/1800)<.003,"Oversized saved window fits uniformly rather than clamping axes independently");
Console.WriteLine($"Refinement 81 checks passed: {checks}");
