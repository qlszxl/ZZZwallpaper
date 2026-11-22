using System.Text.Json;
using LumaWall.Contracts;
using LumaWall.Diagnostics;
using Microsoft.Data.Sqlite;

namespace LumaWall.Storage;

public sealed partial class LibraryStore
{
    internal sealed record UndoScope(string Table,string Column,string Value);
    private sealed record TableRows(string[] Columns,JsonElement[][] Rows);
    private sealed record UndoPatch(UndoScope Scope,string Before,string After);
    private sealed record UndoEntry(string Label,string Key,long Gesture,List<UndoPatch> Patches) {
        public string OperationId {get;init;}=Guid.NewGuid().ToString("N");
        public string Source {get;init;}="ui";
        public OperationContext? Trace {get;init;}
    }
    private readonly System.Threading.AsyncLocal<string?> automationOperation=new();
    public string? AutomationOperationId {get=>automationOperation.Value;set=>automationOperation.Value=value;}

    private readonly List<UndoEntry> undoEdits=[],redoEdits=[];
    private long undoGesture;
    private int undoBatchDepth;
    private UndoEntry? undoBatch;
    public bool HistoryEnabled {get;set;}
    public event Action? HistoryChanged;
    public string? UndoLabel=>undoEdits.LastOrDefault()?.Label;
    public string? RedoLabel=>redoEdits.LastOrDefault()?.Label;
    public long HistoryRevision {get;private set;}
    public long CurrentUndoGesture=>undoGesture;
    // Migrate each snapshot using its own widgets so undo can still restore a removed card.
    public void ReconcileWidgetSettings(){
        string? Normalize(string? value){
            if(string.IsNullOrEmpty(value))return value;
            var state=WidgetConfiguration.FromJson(value);var next=state.ReconcileLocalState();
            return !ReferenceEquals(next,state)||value.Contains("\"Warehouse\"",StringComparison.Ordinal)?next.ToJson():value;
        }
        string? NormalizeSetting(string key,string? value){
            if(key!="sharing.undo.v1"||string.IsNullOrEmpty(value))return Normalize(value);
            var root=System.Text.Json.Nodes.JsonNode.Parse(value) as System.Text.Json.Nodes.JsonObject;
            if(root?["Widgets"] is not {} widgets)return value;
            var old=widgets.ToJsonString();var next=Normalize(old);
            if(next==old)return value;
            root["Widgets"]=System.Text.Json.Nodes.JsonNode.Parse(next!);return root.ToJsonString();
        }
        using(var transaction=connection.BeginTransaction()){
            foreach(var key in new[]{"widgets.v1","sharing.undo.v1"}){
                using var command=database.CreateCommand();command.Transaction=transaction;
                command.CommandText="SELECT value FROM settings WHERE key=$key";command.Parameters.AddWithValue("$key",key);
                var saved=command.ExecuteScalar() as string;
                var clean=NormalizeSetting(key,saved);
                if(clean==saved)continue;
                if(key=="widgets.v1" && saved!=null) {
                    using var original=JsonDocument.Parse(saved);
                    if(original.RootElement.TryGetProperty("Version",out var version)&&version.GetInt32()<18) {
                        using var backup=database.CreateCommand();backup.Transaction=transaction;
                        backup.CommandText="INSERT OR IGNORE INTO settings(key,value) VALUES('widgets.before-0.1.10.2',$value)";
                        backup.Parameters.AddWithValue("$value",saved);backup.ExecuteNonQuery();
                    }
                }
                command.CommandText="UPDATE settings SET value=$value WHERE key=$key";command.Parameters.AddWithValue("$value",clean!);command.ExecuteNonQuery();
            }
            transaction.Commit();
        }
        string CleanRows(string key,string json){
            var rows=JsonSerializer.Deserialize<TableRows>(json)!;int col=Array.IndexOf(rows.Columns,"value");bool changed=false;
            if(col>=0)foreach(var row in rows.Rows){var before=row[col].GetString();var after=NormalizeSetting(key,before);if(before!=after){row[col]=JsonSerializer.SerializeToElement(after);changed=true;}}
            return changed?JsonSerializer.Serialize(rows):json;
        }
        foreach(var entry in undoEdits.Concat(redoEdits)){
            for(int i=0;i<entry.Patches.Count;i++){
                var p=entry.Patches[i];if(p.Scope is {Table:"settings",Value:"widgets.v1" or "sharing.undo.v1"})
                    entry.Patches[i]=p with {Before=CleanRows(p.Scope.Value,p.Before),After=CleanRows(p.Scope.Value,p.After)};
            }
        }
    }
    public void BreakUndoGesture()=>undoGesture++;

    private string ReadUndoRows(UndoScope scope,SqliteTransaction? transaction=null)
    {
        using var command=connection.CreateCommand();command.Transaction=transaction;
        command.CommandText=$"SELECT * FROM {scope.Table} WHERE {scope.Column}=$value ORDER BY rowid";
        command.Parameters.AddWithValue("$value",scope.Value);
        using var reader=command.ExecuteReader();
        var columns=Enumerable.Range(0,reader.FieldCount).Select(reader.GetName).ToArray();
        var rows=new List<JsonElement[]>();
        while(reader.Read())rows.Add(Enumerable.Range(0,reader.FieldCount)
            .Select(i=>JsonSerializer.SerializeToElement(reader.IsDBNull(i)?null:reader.GetValue(i))).ToArray());
        return JsonSerializer.Serialize(new TableRows(columns,rows.ToArray()));
    }
    private static bool SameUndoRows(UndoScope scope,string left,string right)
    {
        if(left==right)return true;
        // Reopening a restored wallpaper allocates a fresh runtime revision.
        // Asset, screen, pause, fill and effects must still match exactly.
        if(scope.Table!="binding")return false;
        string Logical(string json) {
            var rows=JsonSerializer.Deserialize<TableRows>(json)!;
            int revision=Array.IndexOf(rows.Columns,"revision");
            if(revision<0)return json;
            return JsonSerializer.Serialize(rows.Rows.Select(row=>row.Where((_,i)=>i!=revision)));
        }
        return Logical(left)==Logical(right);
    }
    private sealed class UndoCapture(LibraryStore store,string label,string key,UndoScope[] scopes,long? capturedGesture=null) : IDisposable
    {
        private readonly long gesture=capturedGesture??store.undoGesture;
        private readonly OperationContext operation=OperationTrace.Current??OperationTrace.Begin("settings","configuration_edit",label,"system");
        private readonly string[] before=scopes.Select(scope=>store.ReadUndoRows(scope)).ToArray();
        public void Dispose()
        {
            var patches=scopes.Select((scope,i)=>new UndoPatch(scope,before[i],store.ReadUndoRows(scope)))
                .Where(p=>p.Before!=p.After).ToList();
            if(patches.Count>0)store.RecordUndo(new(label,key,gesture,patches) {OperationId=operation.Id,Source=operation.Source,Trace=operation});
        }
    }
    private sealed class NoUndo : IDisposable {public void Dispose() {}}
    private IDisposable CaptureUndo(string label,string key,params UndoScope[] scopes)=>
        HistoryEnabled?new UndoCapture(this,label,key,scopes):new NoUndo();
    private IDisposable CaptureUndo(long? gesture,string label,string key,params UndoScope[] scopes)=>
        HistoryEnabled?new UndoCapture(this,label,key,scopes,gesture):new NoUndo();
    public IDisposable BeginUndoBatch(string label)
    {
        if(undoBatchDepth++==0) {BreakUndoGesture();var operation=OperationTrace.Current??OperationTrace.Begin("settings","configuration_batch",label,"system");undoBatch=new(label,Guid.NewGuid().ToString("N"),undoGesture,[]) {OperationId=operation.Id,Source=operation.Source,Trace=operation};}
        return new UndoBatch(this);
    }
    private sealed class UndoBatch(LibraryStore store) : IDisposable
    {
        public void Dispose() {
            if(--store.undoBatchDepth!=0)return;
            var batch=store.undoBatch;store.undoBatch=null;
            if(batch is {Patches.Count:>0})store.RecordUndo(batch);
            store.BreakUndoGesture();
        }
    }
    private static void MergeUndo(UndoEntry target,UndoEntry incoming)
    {
        foreach(var patch in incoming.Patches) {
            int index=target.Patches.FindIndex(old=>old.Scope==patch.Scope);
            if(index<0)target.Patches.Add(patch);
            else target.Patches[index]=target.Patches[index] with {After=patch.After};
        }
        target.Patches.RemoveAll(p=>p.Before==p.After);
    }
    private void RecordUndo(UndoEntry entry)
    {
        if(AutomationOperationId is { } operationId)entry=entry with {OperationId=operationId,Source="mcp"};
        if(undoBatch is { } batch) {MergeUndo(batch,entry);OperationTrace.Record(entry.Trace,"history","batched",$"historyOperationId={batch.OperationId}");return;}
        redoEdits.Clear();
        if(undoEdits.LastOrDefault() is { } previous&&previous.Key==entry.Key&&previous.Gesture==entry.Gesture) {
            MergeUndo(previous,entry);
            OperationTrace.Record(entry.Trace,"history","merged",$"historyOperationId={previous.OperationId}; gesture={entry.Gesture}");
            if(previous.Patches.Count==0)undoEdits.RemoveAt(undoEdits.Count-1);
        } else {undoEdits.Add(entry);OperationTrace.Record(entry.Trace,"history","recorded",$"historyOperationId={entry.OperationId}; name={entry.Label}; patches={entry.Patches.Count}");}
        // Bound both record count and encoded data (image masks remain resource references).
        while(undoEdits.Count>100||undoEdits.Sum(e=>e.Patches.Sum(p=>(long)p.Before.Length+p.After.Length))*2>32*1024*1024)
            undoEdits.RemoveAt(0);
        HistoryRevision++;HistoryChanged?.Invoke();
    }
    public HistoryReplayResult ReplayUndo(bool redo)
    {
        var from=redo?redoEdits:undoEdits;var to=redo?undoEdits:redoEdits;
        if(from.Count==0)throw new InvalidOperationException(redo?"没有可重做的操作。":"没有可撤销的操作。");
        var entry=from[^1];OperationTrace.Record(OperationTrace.Current,"history_replay","started",$"direction={(redo?"redo":"undo")}; originalOperationId={entry.OperationId}");var screens=new HashSet<string>();
        using(var transaction=connection.BeginTransaction()) {
            foreach(var patch in entry.Patches) {
                string expected=redo?patch.Before:patch.After;
                if(!SameUndoRows(patch.Scope,ReadUndoRows(patch.Scope,transaction),expected))
                    throw new InvalidOperationException("配置已被其他操作更改，无法安全恢复此记录。");
                if(patch.Scope.Table=="binding") {
                    foreach(var json in new[]{patch.Before,patch.After}) {
                        var rows=JsonSerializer.Deserialize<TableRows>(json)!;
                        int index=Array.IndexOf(rows.Columns,"screen");
                        if(index>=0)foreach(var row in rows.Rows)screens.Add(row[index].GetString()!);
                    }
                }
            }
            foreach(var patch in entry.Patches) {
                using var delete=connection.CreateCommand();delete.Transaction=transaction;
                delete.CommandText=$"DELETE FROM {patch.Scope.Table} WHERE {patch.Scope.Column}=$value";
                delete.Parameters.AddWithValue("$value",patch.Scope.Value);delete.ExecuteNonQuery();
                var rows=JsonSerializer.Deserialize<TableRows>(redo?patch.After:patch.Before)!;
                foreach(var row in rows.Rows) {
                    using var insert=connection.CreateCommand();insert.Transaction=transaction;
                    insert.CommandText=$"INSERT INTO {patch.Scope.Table} ({string.Join(",",rows.Columns.Select(c=>'"'+c+'"'))}) VALUES ({string.Join(",",rows.Columns.Select((_,i)=>"$p"+i))})";
                    for(int i=0;i<row.Length;i++) {
                        object value=row[i].ValueKind switch {
                            JsonValueKind.Null=>DBNull.Value,JsonValueKind.String=>row[i].GetString()!,
                            JsonValueKind.Number=>row[i].TryGetInt64(out long integer)?(object)integer:row[i].GetDouble(),
                            _=>throw new InvalidDataException("撤销记录格式无效。")
                        };
                        insert.Parameters.AddWithValue("$p"+i,value);
                    }
                    insert.ExecuteNonQuery();
                }
            }
            transaction.Commit();
        }
        OperationTrace.Record(OperationTrace.Current,"history_replay","committed",$"direction={(redo?"redo":"undo")}; originalOperationId={entry.OperationId}");
        assetsByPath=null;PlaybackLibraryRevision++;from.RemoveAt(from.Count-1);to.Add(entry);
        BreakUndoGesture();HistoryRevision++;HistoryChanged?.Invoke();
        return new(entry.Label,entry.Patches.Select(p=>p.Scope.Table).Distinct().ToArray(),screens.ToArray(),entry.Patches.Where(p=>p.Scope.Table=="settings").Select(p=>p.Scope.Value).ToArray(),
            new(entry.Patches.Where(p=>p.Scope.Table=="screen_profile").Select(p=>p.Scope.Value).Concat(screens).Distinct().ToArray(),entry.Patches.Where(p=>p.Scope.Table is "asset_effects" or "media").Select(p=>p.Scope.Value).Distinct().ToArray()));
    }
}
