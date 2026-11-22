using System.Drawing;
using LumaWall.ControlCenter;
using Display=LumaWall.ControlCenter.WindowPlacementGeometry.Display;
using LogicalBounds=LumaWall.ControlCenter.WindowPlacementGeometry.LogicalBounds;

int checks=0;
void Check(bool value,string label)
{
    if(!value)throw new Exception("FAIL: "+label);
    Console.WriteLine("PASS: "+label);checks++;
}
(int Index,Rectangle Bounds) Resolve(Rectangle normal,bool workspace,Display[] displays,string? conversionMonitor=null)
{
    if(!WindowPlacementGeometry.TryResolveNormalBounds(normal,workspace,displays,conversionMonitor,out int index,out var bounds))
        throw new Exception("No display matched the normal rectangle.");
    return(index,bounds);
}

var primary=new Display("primary",new(0,0,1920,1080),new(0,0,1920,1040));
var topTaskbar=new Display("top",new(0,0,1920,1080),new(0,48,1920,1032));
var leftTaskbar=new Display("left",new(0,0,1920,1080),new(48,0,1872,1080));
var negative=new Display("negative",new(-1920,-240,1920,1080),new(-1872,-200,1872,1040));
var highDpi=new Display("high-dpi",new(1920,-360,2560,1440),new(1920,-312,2560,1392));

Check(Resolve(new(100,100,800,600),true,[primary],"primary").Bounds==new Rectangle(100,100,800,600),
    "A bottom taskbar does not shift restored screen coordinates");
Check(Resolve(new(100,100,800,600),true,[topTaskbar],"top").Bounds==new Rectangle(100,148,800,600),
    "A top taskbar is added exactly once to workspace coordinates");
Check(Resolve(new(100,100,800,600),true,[leftTaskbar],"left").Bounds==new Rectangle(148,100,800,600),
    "A left taskbar is added exactly once to workspace coordinates");
Check(Resolve(new(100,148,800,600),false,[topTaskbar]).Bounds==new Rectangle(100,148,800,600),
    "Tool windows already use screen coordinates");
Check(WindowPlacementGeometry.UsesWorkspaceCoordinates(0)
    &&WindowPlacementGeometry.UsesWorkspaceCoordinates(0x40000)
    &&!WindowPlacementGeometry.UsesWorkspaceCoordinates(0x80)
    &&!WindowPlacementGeometry.UsesWorkspaceCoordinates(0x40080),"Extended tool-window style controls coordinate conversion");

var negativeResult=Resolve(new(-1800,-140,900,600),true,[primary,negative],"primary");
Check(negativeResult==(1,new Rectangle(-1800,-140,900,600)),
    "Normal bounds on a negative-coordinate screen use the current primary monitor's zero workspace offset");
var mixed=Resolve(new(2070,-210,1200,900),true,[primary,highDpi],"high-dpi");
Check(mixed==(1,new Rectangle(2070,-162,1200,900)),
    "The current secondary monitor's top-taskbar offset converts workspace coordinates once");
var mixedLogical=WindowPlacementGeometry.ToLogical(mixed.Bounds,highDpi.WorkingArea,1.5);
Check(mixedLogical==new LogicalBounds(100,100,800,600),
    "A 150 percent display converts physical normal bounds to the existing logical storage format");
Check(WindowPlacementGeometry.Restore(mixedLogical,highDpi.WorkingArea,1.5,false)==mixed.Bounds,
    "Mixed-DPI normal bounds round-trip without position or size drift");
Check(WindowPlacementGeometry.Restore(mixedLogical,primary.WorkingArea,1,false)==new Rectangle(100,100,800,600),
    "Saved logical size follows the destination display DPI");

var separateDisplay=Resolve(new(-1692,-98,900,600),true,[topTaskbar,negative],"top");
Check(separateDisplay==(1,new Rectangle(-1692,-50,900,600)),
    "Conversion uses the current HWND top-taskbar offset before selecting the different normal display");
var separateLogical=WindowPlacementGeometry.ToLogical(separateDisplay.Bounds,negative.WorkingArea,1.5);
Check(separateLogical==new LogicalBounds(120,100,600,400)
    &&WindowPlacementGeometry.Restore(separateLogical,negative.WorkingArea,1.5,false)==separateDisplay.Bounds,
    "The normal display supplies its own work-area origin and DPI after conversion");
Check(Resolve(new(2022,-202,1200,900),true,[negative,highDpi],"negative")
    ==(1,new Rectangle(2070,-162,1200,900)),
    "A negative-coordinate current monitor contributes both offsets even when normal bounds belong to another screen");

var right=new Display("right",new(1920,0,1920,1080),new(1920,0,1920,1040));
Check(Resolve(new(1700,100,800,600),true,[primary,right],"primary").Index==1,
    "A window spanning monitors selects the largest normal-rectangle intersection");
Check(Resolve(new(1520,100,800,600),true,[primary,right],"right").Index==1,
    "The current monitor only breaks equal-intersection ties");
Check(Resolve(new(1520,100,800,600),true,[primary,right],"primary").Index==0,
    "A tie can retain the primary display as well");
Check(Resolve(new(4500,100,800,600),true,[primary,right],"primary").Index==1,
    "A fully off-screen rectangle resolves to the nearest display instead of the active display");
Check(!WindowPlacementGeometry.TryResolveNormalBounds(new(100,100,0,600),true,[primary],null,out _,out _)
    &&!WindowPlacementGeometry.TryResolveNormalBounds(new(100,100,800,600),true,[],null,out _,out _),
    "Invalid rectangles or missing displays preserve the previous placement");

var rightLeftTaskbar=new Display("right-left-taskbar",right.Bounds,new(1968,0,1872,1040));
Check(Resolve(new(1500,100,800,600),true,[primary,rightLeftTaskbar],"primary")
    ==(0,new Rectangle(1500,100,800,600)),
    "A neighboring display's left taskbar cannot pull a primary-origin restore rectangle across the boundary");
Check(Resolve(new(1500,100,800,600),true,[primary,rightLeftTaskbar],"right-left-taskbar")
    ==(1,new Rectangle(1548,100,800,600)),
    "The same workspace rectangle converts differently when the HWND monitor owns the left taskbar");
Check(Resolve(new(1482,100,800,600),true,[primary,rightLeftTaskbar],"right-left-taskbar")
    ==(1,new Rectangle(1530,100,800,600)),
    "Screen selection occurs after conversion, avoiding the opposite raw-workspace boundary error");
var bottomTopTaskbar=new Display("bottom-top-taskbar",new(0,1080,1920,1080),new(0,1128,1920,1032));
Check(Resolve(new(100,660,800,800),true,[primary,bottomTopTaskbar],"primary")
    ==(0,new Rectangle(100,660,800,800))
    &&Resolve(new(100,660,800,800),true,[primary,bottomTopTaskbar],"bottom-top-taskbar")
    ==(1,new Rectangle(100,708,800,800)),
    "The same one-time conversion rule also handles vertically stacked screens with a top taskbar");
Check(!WindowPlacementGeometry.TryResolveNormalBounds(new(100,100,800,600),true,[primary],"removed",out int missingIndex,out var missingBounds)
    &&missingIndex==-1&&missingBounds==default,
    "A missing conversion monitor safely preserves saved placement without guessing an offset");
Check(!WindowPlacementGeometry.TryResolveNormalBounds(new(100,100,800,600),true,[primary],null,out _,out _),
    "Workspace coordinates require an explicit current HWND monitor");
Check(Resolve(new(2070,100,800,600),false,[primary,rightLeftTaskbar],"removed")
    ==(1,new Rectangle(2070,100,800,600)),
    "Tool-window screen coordinates remain usable when no conversion monitor is available");
Check(Resolve(new(100,100,800,600),true,[topTaskbar],"TOP").Bounds==new Rectangle(100,148,800,600),
    "Conversion monitor device names are case-insensitive");

Check(WindowPlacementGeometry.MatchesMonitor("monitor-a","old-device","MONITOR-A","new-device"),
    "A display retains identity when Windows renumbers its device name");
Check(!WindowPlacementGeometry.MatchesMonitor("removed-monitor","same-device","replacement-monitor","same-device"),
    "A removed monitor is not silently replaced by a reused device name");
Check(WindowPlacementGeometry.MatchesMonitor("","DISPLAY1","unused","display1"),
    "Legacy device-only saves remain compatible");
Check(!WindowPlacementGeometry.MatchesMonitor("","missing","unused","display1"),
    "A missing legacy display also triggers the existing centered recovery path");

var center=WindowPlacementGeometry.Restore(new(5000,-2000,800,600),primary.WorkingArea,1,true);
Check(center==new Rectangle(560,220,800,600),"Removed-screen recovery centers the normal size on the destination work area");
Check(WindowPlacementGeometry.Restore(new(-500,-500,800,600),negative.WorkingArea,1,false)
    ==new Rectangle(-1872,-200,800,600),"Negative logical offsets clamp inside a negative-coordinate work area");
Check(WindowPlacementGeometry.Restore(new(5000,5000,800,600),primary.WorkingArea,1,false)
    ==new Rectangle(1120,440,800,600),"Off-screen offsets clamp without changing a valid normal size");
Check(WindowPlacementGeometry.Restore(new(0,0,4000,3000),primary.WorkingArea,1.5,false)
    ==new Rectangle(0,0,1387,1040),"Oversized saved windows preserve aspect while fitting the display work area");
Check(WindowPlacementGeometry.Restore(null,new(-200,80,300,200),1,true)==new Rectangle(-200,89,300,181),
    "Recovery on a very small work area keeps valid bounds");

Check(!WindowPlacementGeometry.RestoreMaximized(1,2,true),
    "A restored window clears a stale maximize flag");
Check(WindowPlacementGeometry.RestoreMaximized(3,0,false),
    "Moving then immediately maximizing preserves the maximized preference");
Check(!WindowPlacementGeometry.RestoreMaximized(2,0,false),
    "Moving then immediately minimizing preserves the normal preference");
Check(WindowPlacementGeometry.RestoreMaximized(2,2,false),
    "Minimized windows honor the native restore-to-maximized flag");
Check(WindowPlacementGeometry.RestoreMaximized(2,0,true),
    "A minimized window retains a previously observed maximize state when flags are absent");
Check(!WindowPlacementGeometry.RestoreMaximized(1,0,WindowPlacementGeometry.RestoreMaximized(2,2,true)),
    "Restoring a previously maximized window back to normal resets the remembered state");

// The delayed snapshot uses the final native normal rectangle, so transitions
// before the debounce fires do not save the earlier cached position or the
// currently maximized/minimized rectangle.
var finalNormal=new Rectangle(2080,-200,1200,900);
foreach(var state in new[] {(Show:3u,Flags:0u,Last:false,Max:true),(Show:2u,Flags:0u,Last:false,Max:false),
    (Show:2u,Flags:2u,Last:false,Max:true),(Show:2u,Flags:0u,Last:true,Max:true)}) {
    var final=Resolve(finalNormal,true,[primary,highDpi],"primary");
    var logical=WindowPlacementGeometry.ToLogical(final.Bounds,highDpi.WorkingArea,1.5);
    Check(WindowPlacementGeometry.Restore(logical,highDpi.WorkingArea,1.5,false)==final.Bounds
        &&WindowPlacementGeometry.RestoreMaximized(state.Show,state.Flags,state.Last)==state.Max,
        $"A final normal-rectangle snapshot survives state {state.Show}, flags {state.Flags}, remembered maximize {state.Last}");
}

foreach(var scale in new[] {1d,1.25,1.5,1.75,2d}) {
    var original=new Rectangle(-1701,-93,999,701);
    var logical=WindowPlacementGeometry.ToLogical(original,negative.WorkingArea,scale);
    Check(WindowPlacementGeometry.Restore(logical,negative.WorkingArea,scale,false)==original,
        $"Negative-coordinate bounds round-trip at {scale*100:0} percent DPI");
}
var largeWorkArea=new Rectangle(0,0,3840,2120);
var firstLaunch=WindowPlacementGeometry.Restore(null,largeWorkArea,1,true);
Check(firstLaunch.Width==1786&&firstLaunch.Height==1077&&firstLaunch.Left==(largeWorkArea.Width-1786)/2,
    "First launch uses the observed 1786 by 1077 normal window size and centers it");
var scaledDefault=WindowPlacementGeometry.Restore(null,largeWorkArea,1.5,true);
Check(scaledDefault.Width==2679&&scaledDefault.Height==1616,"Default size follows display scaling");
Check(WindowPlacementGeometry.Restore(null,new Rectangle(0,0,1366,728),1,true)==new Rectangle(79,0,1207,728),
    "Small work areas constrain the new default without placing controls off-screen");
Check(WindowPlacementGeometry.StartupSizeFactor(1,1,2)==1,"A window saved on the launch display retains its user-selected size");
Check(WindowPlacementGeometry.StartupSizeFactor(.75,1,1)==.75,"Moving to a smaller display applies its resolution factor on the next launch");
Check(WindowPlacementGeometry.StartupSizeFactor(1,0,.8)==.8,"Old placements without a launch scale use their saved resolution");
Console.WriteLine($"Passed {checks} window placement checks.");
