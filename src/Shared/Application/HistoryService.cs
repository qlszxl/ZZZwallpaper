using LumaWall.Contracts;

namespace LumaWall.Application;

public sealed class HistoryService(IHistoryStore store)
{
    public HistoryReplayResult Restore(bool redo,string? expectedRevision=null)
    {
        var revision=store.HistoryRevision.ToString();
        if(expectedRevision!=null&&expectedRevision!=revision)
            throw new ConfigurationConflictException(expectedRevision,revision,"app_get_state.history");
        return store.ReplayUndo(redo);
    }
}
