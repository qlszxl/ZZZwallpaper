using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace LumaWall.Storage;

public sealed partial class LibraryStore
{
    // No caller-supplied SQL, table names, paths or security settings enter this API.
    private static readonly string[] CheckpointTables=["media","binding","profile","screen_profile","folder","folder_member","asset_effects","effect_resource","settings"];
    private const string ConfigurationSettings="key='widgets.v1' OR key='appearance.v1' OR key='fpsLimit' OR key='audioAutoAdapt' OR key='decoderMode' OR key='fullscreenPause' OR key LIKE 'pause.%' OR key LIKE 'playback:%' OR key LIKE 'videoSound:%' OR key LIKE 'videoVolume:%' OR key LIKE 'audioCaptureDevice:%'";
    public sealed record ConfigurationSnapshot(Dictionary<string,string> Tables);
    public ConfigurationSnapshot CaptureConfiguration()
    {
        var tables=new Dictionary<string,string>();
        foreach(string table in CheckpointTables) {
            using var command=connection.CreateCommand();
            command.CommandText=$"SELECT * FROM {table}"+(table=="settings"?" WHERE "+ConfigurationSettings:"")+" ORDER BY rowid";
            using var reader=command.ExecuteReader();var columns=Enumerable.Range(0,reader.FieldCount).Select(reader.GetName).ToArray();var rows=new List<JsonElement[]>();
            while(reader.Read())rows.Add(Enumerable.Range(0,reader.FieldCount).Select(i=>JsonSerializer.SerializeToElement(reader.IsDBNull(i)?null:reader.GetValue(i))).ToArray());
            tables[table]=JsonSerializer.Serialize(new TableRows(columns,rows.ToArray()));
        }
        return new(tables);
    }
    public string ConfigurationRevision() {
        // Poster choices participate in concurrency without pretending checkpoints snapshot image bytes.
        using var command=connection.CreateCommand();command.CommandText="SELECT key,value FROM settings WHERE key LIKE 'poster.choice.v1:%' ORDER BY key";
        using var reader=command.ExecuteReader();var posters=new Dictionary<string,string>();while(reader.Read())posters[reader.GetString(0)]=reader.GetString(1);
        reader.Close();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {configuration=CaptureConfiguration(),posters}))));
    }
    public void RestoreConfiguration(ConfigurationSnapshot snapshot)
    {
        if(!CheckpointTables.ToHashSet().SetEquals(snapshot.Tables.Keys))throw new ArgumentException("检查点格式无效");
        using var transaction=connection.BeginTransaction();
        foreach(string table in CheckpointTables) {
            using var delete=connection.CreateCommand();delete.Transaction=transaction;
            delete.CommandText=$"DELETE FROM {table}"+(table=="settings"?" WHERE "+ConfigurationSettings:"");delete.ExecuteNonQuery();
            var rows=JsonSerializer.Deserialize<TableRows>(snapshot.Tables[table])!;
            foreach(var row in rows.Rows) {
                using var insert=connection.CreateCommand();insert.Transaction=transaction;
                insert.CommandText=$"INSERT INTO {table} ({string.Join(",",rows.Columns.Select(c=>'"'+c+'"'))}) VALUES ({string.Join(",",rows.Columns.Select((_,i)=>"$p"+i))})";
                for(int i=0;i<row.Length;i++)insert.Parameters.AddWithValue("$p"+i,row[i].ValueKind switch {JsonValueKind.Null=>DBNull.Value,JsonValueKind.String=>(object)row[i].GetString()!,JsonValueKind.Number=>row[i].TryGetInt64(out long value)?value:row[i].GetDouble(),_=>throw new InvalidDataException("检查点字段无效")});
                insert.ExecuteNonQuery();
            }
        }
        transaction.Commit();assetsByPath=null;audioMetadataCache.Clear();PlaybackLibraryRevision++;
        ReconcileWidgetSettings();
        undoEdits.Clear();redoEdits.Clear();HistoryRevision++;BreakUndoGesture();HistoryChanged?.Invoke();
    }
    public void FlushConfiguration()
    {
        using var command=connection.CreateCommand();command.CommandText="PRAGMA wal_checkpoint(FULL)";
        using var result=command.ExecuteReader();
        if(!result.Read()||result.GetInt32(0)!=0)throw new IOException("配置已提交，但 WAL 落盘检查点繁忙，请重试");
    }
    public string[] PlaybackScreens()
    {
        using var command=connection.CreateCommand();command.CommandText="SELECT key FROM settings WHERE key LIKE 'playback:%'";
        using var reader=command.ExecuteReader();var result=new List<string>();while(reader.Read())result.Add(reader.GetString(0)[9..]);return result.ToArray();
    }
    public object HistoryDetails()=>new {
        revision=HistoryRevision.ToString(),undo=UndoLabel,redo=RedoLabel,undoCount=undoEdits.Count,redoCount=redoEdits.Count,
        bytes=undoEdits.Concat(redoEdits).Sum(e=>e.Patches.Sum(p=>2L*(p.Before.Length+p.After.Length))),
        entries=undoEdits.TakeLast(20).Select(e=>new {operationId=e.OperationId,source=e.Source,name=e.Label,targetIds=e.Patches.Select(p=>p.Scope.Value).Distinct()})
    };
}
