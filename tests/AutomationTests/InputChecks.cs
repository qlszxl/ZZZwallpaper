using System.Runtime.InteropServices;
using System.Text.Json;
using LumaWall.Automation;
using LumaWall.ControlCenter;

internal static class InputChecks
{
    internal static void Run(Action<bool,string> check)
    {
        check(IntPtr.Size==8&&AutomationInput.Size==40,"x64 Win32 INPUT is 40 bytes");
        check(Marshal.OffsetOf<AutomationInput.Input>("Data").ToInt32()==8,"x64 INPUT payload starts at byte 8");
        check(Marshal.SizeOf<AutomationInput.MouseData>()==32&&Marshal.OffsetOf<AutomationInput.MouseData>("ExtraInfo").ToInt32()==24,"MOUSEINPUT includes pointer alignment");
        check(Marshal.SizeOf<AutomationInput.KeyData>()==24&&Marshal.OffsetOf<AutomationInput.KeyData>("ExtraInfo").ToInt32()==16,"KEYBDINPUT includes pointer alignment");
        var desktop=new System.Drawing.Rectangle(-1920,-1080,4480,2520);
        var first=AutomationInput.Mouse(1,desktop.Left,desktop.Top,desktop);var last=AutomationInput.Mouse(1,desktop.Right-1,desktop.Bottom-1,desktop);
        check(first.Data.Mouse.X==0&&first.Data.Mouse.Y==0&&last.Data.Mouse.X==65535&&last.Data.Mouse.Y==65535,"Absolute coordinates cover negative-origin virtual desktop");
        check(unchecked((int)AutomationInput.Mouse(0x800,0,0,desktop,-120).Data.Mouse.Data)==-120,"Wheel direction retains signed delta");
        var down=AutomationInput.Key(39);var up=AutomationInput.Key(39,true);
        check(down.Data.Keyboard.Flags==1&&up.Data.Keyboard.Flags==3,"Navigation key preserves extended-key flags");
        check(AutomationInput.Unicode('中').Data.Keyboard.Scan=='中'&&AutomationInput.Unicode('中',true).Data.Keyboard.Flags==6,"Unicode uses paired scan events");
        uint count=AutomationInput.Dispatch([down,up],batch=>new((uint)batch.Length,0),_=>false);
        check(count==2,"Successful dispatch reports actual event count");
        OperationFault Fault(Action run) {try {run();}catch(OperationFault e) {return e;}throw new Exception("Expected input failure");}
        int calls=0;
        var held=Fault(()=>AutomationInput.Dispatch([down,up],batch=> {calls++;return new(2,0);},key=>key==91));
        check(held.Code=="invalid_state"&&calls==0,"Held Windows key rejects before dispatch");
        var zero=Fault(()=>AutomationInput.Dispatch([down,up],batch=> {calls++;return new(0,87);},_=>false));
        var details=JsonSerializer.SerializeToElement(zero.Details);
        check(calls==1&&details.GetProperty("nativeError").GetInt32()==87&&details.GetProperty("cleanupRequested").GetInt32()==0,"Zero-send preserves native error and emits no cleanup input");
        var batches=new List<AutomationInput.Input[]>();
        var partial=Fault(()=>AutomationInput.Dispatch([AutomationInput.Key(16),down,up,AutomationInput.Key(16,true)],batch=> {
            batches.Add(batch);return batches.Count==1?new(2,5):new((uint)batch.Length,0);
        },_=>false));
        details=JsonSerializer.SerializeToElement(partial.Details);
        check(batches.Count==2&&batches[1].Length==2&&batches[1][0].Data.Keyboard.Key==39&&batches[1][1].Data.Keyboard.Key==16&&batches[1].All(x=>(x.Data.Keyboard.Flags&2)!=0),"Partial keyboard send releases only accepted downs in reverse order");
        check(details.GetProperty("nativeError").GetInt32()==5&&details.GetProperty("cleanupSent").GetInt32()==2,"Cleanup does not overwrite original error");
        var click=new[]{first,AutomationInput.Mouse(2,0,0,desktop),AutomationInput.Mouse(4,0,0,desktop)};
        check(AutomationInput.PendingReleases(click,1).Length==0,"Mouse move failure cannot release user's button");
        var release=AutomationInput.PendingReleases(click,2);
        check(release.Length==1&&release[0].Type==0&&release[0].Data.Mouse.Flags==4,"Interrupted click releases just left button without moving pointer");
        check(AutomationInput.PendingReleases(click,3).Length==0,"Completed click requires no release");
        var unicode=AutomationInput.PendingReleases([AutomationInput.Unicode('中')],1);
        check(unicode.Length==1&&unicode[0].Data.Keyboard.Scan=='中'&&unicode[0].Data.Keyboard.Flags==6,"Interrupted Unicode input releases same code unit");
        long now=100;var cache=new AutomationSnapshotCache<string>(()=>now);
        string token=cache.Add("first");now+=59999;
        check(cache.Get(token)=="first","Snapshot remains available within fixed lifetime");
        now++;
        check(Fault(()=>cache.Get(token)).Code=="revision_conflict","Snapshot expires without extending lifetime on reads");
        var tokens=Enumerable.Range(0,9).Select(i=>cache.Add(i.ToString())).ToArray();
        check(Fault(()=>cache.Get(tokens[0])).Code=="revision_conflict"&&tokens.Skip(1).Select(cache.Get).SequenceEqual(Enumerable.Range(1,8).Select(i=>i.ToString())),"Cache retains latest eight observations");
        cache.Add("next");check(Fault(()=>cache.Get(tokens[1])).Code=="revision_conflict"&&cache.Get(tokens[8])=="8","Eviction remains FIFO after dictionary slot reuse");
        cache.Clear();check(Fault(()=>cache.Get(tokens[8])).Code=="revision_conflict","Dispatch/revocation invalidates cached observations");
        ToolCatalog.Validate("ui_get_tree",JsonSerializer.SerializeToElement(new {treeRevision="snapshot",offset=200,limit=200}));
        check(true,"Contract accepts pinned snapshot pagination");
    }
}
