global using LumaWall.Diagnostics;
using System.Runtime.InteropServices;
using LumaWall.Contracts;
using LumaWall.DesktopIntegration;

// Pure input policy tests: no native call, window, hook, Explorer access or input injection.
int checks=0,queries=0;
void Check(bool condition,string message) {if(!condition)throw new Exception(message);Console.WriteLine("PASS: "+message);checks++;}
var widget=new DesktopRect(-1886,956,-674,1440);
var scene=new DesktopSceneSnapshot(1000,7,new(-2560,-2160,3840,2160),
    [new("left",new(-2560,0,0,1440)),new("main-200-percent",new(0,0,3840,2160)),new("above",new(0,-2160,3840,0))],
    [new(-2500,1050,-2400,1200)],
    [new(0,2112,3840,2160),new(100,500,1000,1400)]) {PerPixelOcclusions=[widget]};
HitSnapshot Hit(int x,int y,nint target)
    =>DesktopHitTestService.ResolveHit(scene,11,12,13,x,y,p=> {queries++;Check(p.X==x&&p.Y==y,"physical point remains signed and unscaled");return target;});

Check(!scene.Hit(-1600,1100).IsBlank,"unresolved layered bounds remain conservative");
foreach(var point in new[]{(-1600,1100),(-1200,1300),(-800,1439)}) {
    var hit=Hit(point.Item1,point.Item2,11);
    Check(hit.IsBlank&&hit.ScreenId=="left","transparent lower-screen widget pixels resolve to desktop");
    var sequence=new RightButtonSequence();
    Check(sequence.Down(point.Item1,point.Item2,hit,1010,7,true).Suppress,"transparent lower-screen down is captured");
    var up=sequence.Up(point.Item1,point.Item2,true);
    Check(up.Suppress&&up.Menu?.ScreenId=="left","transparent lower-screen release opens the custom menu without leaking to Explorer");
}
Check(!Hit(-1600,1100,99).IsBlank,"interactive widget pixels retain the widget right-click");
Check(!Hit(-2000,500,99).IsBlank,"new window not yet in the geometry cache retains its right-click");
Check(!Hit(-1600,1100,0).IsBlank,"unresolved physical target does not capture");
foreach(nint target in new nint[]{11,12,13})Check(Hit(-2000,1300,target).IsBlank,"only captured Explorer desktop identities are accepted");
var before=queries;
Check(!Hit(-2450,1100,11).IsBlank,"desktop icon and label remain excluded under a transparent overlay");
Check(!Hit(300,600,11).IsBlank,"ordinary or disabled window bounds remain excluded");
Check(!Hit(300,2130,11).IsBlank,"taskbar remains excluded");
Check(!Hit(-3000,100,11).IsBlank,"point outside every display remains excluded");
Check(!Hit(-2000,-100,11).IsBlank,"gap in the multi-monitor bounding rectangle remains excluded");
Check(queries==before,"definite occlusions never query native hit testing");
Check(Hit(3000,2000,11).IsBlank,"physical lower half of a 200-percent screen is not clipped to logical bounds");
Check(Hit(3000,-2000,11).ScreenId=="above","negative vertical monitor coordinates route to the correct display");
var stale=Hit(-1600,1100,11);var guard=new RightButtonSequence();
Check(!guard.Down(-1600,1100,stale,1201,7,true).Suppress,"physical target confirmation does not revive a stale geometry snapshot");
Check(!guard.Down(-1600,1100,stale,1010,8,true).Suppress,"physical target confirmation does not bypass topology revision checks");
Check(!guard.Down(-1600,1100,stale,1010,7,false).Suppress,"disabled screen or unavailable menu service still passes through");
var vanished=new RightButtonSequence();vanished.Down(-1600,1100,stale,1010,7,true);
var unavailableUp=vanished.Up(-1600,1100,false);
Check(unavailableUp.Suppress&&unavailableUp.Menu==null,"service loss never leaks the release of an already captured press");
var far=scene with {Desktop=new(-70000,-70000,-60000,-60000),Screens=[new("far",new(-70000,-70000,-60000,-60000))],Icons=[],Occlusions=[],PerPixelOcclusions=[]};
var farHit=DesktopHitTestService.ResolveHit(far,11,12,13,-65000,-64000,p=> {Check(p.X==-65000&&p.Y==-64000,"large negative positions are never packed into 16-bit message words");return 11;});
Check(farHit.IsBlank&&farHit.ScreenId=="far","large signed virtual-screen coordinates remain usable");
Check(Marshal.SizeOf<Native.MonitorInfo>()==104,"MONITORINFOEXW layout preserves the complete device ID and physical rectangles");
Check(Marshal.OffsetOf<Native.MonitorInfo>(nameof(Native.MonitorInfo.Device)).ToInt32()==40,"MONITORINFOEXW device name starts after native flags");
Check(Marshal.SizeOf<Native.Point>()==8,"mouse coordinates preserve two signed 32-bit values");
Console.WriteLine($"Desktop hit policy checks passed: {checks}");
