using LumaWall.Automation;

namespace LumaWall.ControlCenter;

// UI-thread owned, bounded, and instance-local. A revision identifies an
// immutable observation, not a hash of unrelated live template decorations.
internal sealed class AutomationSnapshotCache<T>(Func<long>? clock=null) where T:class
{
    internal const int Capacity=8;
    internal const long LifetimeMs=60000;
    private readonly Func<long> now=clock??(()=>Environment.TickCount64);
    private readonly Dictionary<string,(long Created,long Sequence,T Value)> entries=new();
    private long sequence;
    internal string Add(T value)
    {
        Prune();while(entries.Count>=Capacity)entries.Remove(entries.MinBy(p=>p.Value.Sequence).Key);
        string revision=Guid.NewGuid().ToString("N");entries.Add(revision,(now(),sequence++,value));return revision;
    }
    internal T Get(string revision)
    {
        Prune();return entries.TryGetValue(revision,out var entry)?entry.Value:
            throw new OperationFault("revision_conflict","UI 快照已过期或不存在，请重新读取","arguments.treeRevision",recovery:"reread_ui_tree",details:new {reason="snapshot_expired_or_unknown"});
    }
    private void Prune() {long cutoff=now()-LifetimeMs;foreach(var id in entries.Where(p=>p.Value.Created<=cutoff).Select(p=>p.Key).ToArray())entries.Remove(id);}
    internal void Clear()=>entries.Clear();
}
