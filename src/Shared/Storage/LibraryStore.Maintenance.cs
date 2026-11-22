using System.Text.Json;
using System.Text.RegularExpressions;
using LumaWall.Contracts;

namespace LumaWall.Storage;

public sealed partial class LibraryStore
{
    private bool storeDisposed;
    private static readonly Regex ResourceReference=new("resource:([A-Fa-f0-9]{64})",RegexOptions.CultureInvariant);

    // Runs on the owning UI thread, outside an edit transaction. Preserve both
    // sides of undo/redo and legacy profiles before reclaiming image resources.
    public void MaintainStorage()
    {
        if(storeDisposed||effectTransaction!=null||undoBatchDepth!=0)return;
        try {
            var resources=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var media=Assets().Select(a=>a.Id).ToHashSet(StringComparer.Ordinal);
            void References(string text) {foreach(Match match in ResourceReference.Matches(text))resources.Add(match.Groups[1].Value);}
            foreach(string table in new[]{"screen_profile","binding","profile","asset_effects"}) {
                using var query=connection.CreateCommand();query.CommandText=$"SELECT effects FROM {table}";
                using var reader=query.ExecuteReader();while(reader.Read())References(reader.GetString(0));
            }
            References(ReadStoredSetting("sharing.undo.v1")??"");
            foreach(var patch in undoEdits.Concat(redoEdits).SelectMany(e=>e.Patches)) {
                References(patch.Before);References(patch.After);
                if(patch.Scope.Table=="media")foreach(string json in new[]{patch.Before,patch.After}) {
                    var rows=JsonSerializer.Deserialize<TableRows>(json)!;int id=Array.IndexOf(rows.Columns,"id");
                    if(id>=0)foreach(var row in rows.Rows)media.Add(row[id].GetString()!);
                }
            }
            var unused=new List<string>();
            using(var query=connection.CreateCommand()) {
                query.CommandText="SELECT id FROM effect_resource";using var reader=query.ExecuteReader();
                while(reader.Read())if(!resources.Contains(reader.GetString(0)))unused.Add(reader.GetString(0));
            }
            using(var transaction=connection.BeginTransaction()) {
                using var remove=connection.CreateCommand();remove.Transaction=transaction;remove.CommandText="DELETE FROM effect_resource WHERE id=$id";
                var id=remove.Parameters.Add("$id",Microsoft.Data.Sqlite.SqliteType.Text);
                foreach(string key in unused) {id.Value=key;remove.ExecuteNonQuery();}
                var stale=new List<string>();
                using(var query=connection.CreateCommand()) {
                    query.Transaction=transaction;query.CommandText="SELECT key FROM settings WHERE key LIKE 'assetAudio:%' OR key LIKE 'poster.v4:%' OR key LIKE 'poster.choice.v1:%'";
                    using var reader=query.ExecuteReader();while(reader.Read()) {var key=reader.GetString(0);if(!media.Contains(key[(key.IndexOf(':')+1)..]))stale.Add(key);}
                }
                remove.CommandText="DELETE FROM settings WHERE key=$id";
                foreach(string key in stale) {id.Value=key;remove.ExecuteNonQuery();}
                transaction.Commit();
            }
            foreach(var key in audioMetadataCache.Keys.Where(a=>!media.Contains(a.Id)).ToArray())audioMetadataCache.Remove(key);
            string posters=Path.Combine(Root,"posters");
            OwnedFileRetention.Prune(posters,n=>OwnedFileRetention.HexName(n,".bmp"),0,0,TimeSpan.Zero,media.Select(Poster),TimeSpan.FromMinutes(2));
            OwnedFileRetention.Prune(posters,n=>Regex.IsMatch(n,@"^[a-fA-F0-9]{32}\.bmp\.[a-fA-F0-9]{32}\.(bmp|tmp)$"),0,0,TimeSpan.Zero,recentGrace:TimeSpan.FromDays(1));
            MaintainPreviewFiles();
            // Deleted SQLite pages are reused; truncate the WAL after completed
            // edits so a past large operation does not leave a large sidecar.
            using var checkpoint=connection.CreateCommand();checkpoint.CommandText="PRAGMA wal_checkpoint(TRUNCATE)";checkpoint.ExecuteNonQuery();
        } catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException or JsonException) {
            System.Diagnostics.Debug.WriteLine("Storage maintenance deferred: "+ex.Message);
        }
    }
    public void MaintainPreviewFiles()
    {
        string preview=Path.Combine(Root,"effect-preview");
        string? current=GetSetting("effectPreviewCustom");
        OwnedFileRetention.Prune(preview,n=>n.StartsWith("custom-",StringComparison.Ordinal)&&new[]{".png",".jpg",".jpeg",".webp"}.Any(ext=>OwnedFileRetention.HexName(n[7..],ext)),
            2,64L*1024*1024,TimeSpan.FromDays(1),string.IsNullOrWhiteSpace(current)?null:[current],TimeSpan.FromMinutes(2));
        OwnedFileRetention.Prune(preview,n=>OwnedFileRetention.HexName(n.StartsWith("mcp-poster-",StringComparison.Ordinal)?n[11..]:"",".bmp",64)||
            n.StartsWith("poster-",StringComparison.Ordinal)&&OwnedFileRetention.HexName(n[7..],".bmp"),64,128L*1024*1024,TimeSpan.FromDays(7),recentGrace:TimeSpan.FromMinutes(2));
        OwnedFileRetention.Prune(preview,n=>Regex.IsMatch(n,@"^mcp-poster-[a-fA-F0-9]{64}\.bmp\.[a-fA-F0-9]{32}\.bmp$"),0,0,TimeSpan.Zero,recentGrace:TimeSpan.FromDays(1));
        OwnedFileRetention.Prune(Path.Combine(preview,"cache"),n=>OwnedFileRetention.HexName(n,".bmp"),8,32L*1024*1024,TimeSpan.FromDays(1),recentGrace:TimeSpan.FromMinutes(2));
        MaintainAutomationArtifacts();
    }
    public void MaintainAutomationArtifacts(string? current=null)
    {
        OwnedFileRetention.Prune(Path.Combine(Root,"automation-artifacts"),n=>new[]{".png",".txt",".json",".zip",".bmp"}.Any(ext=>OwnedFileRetention.HexName(n,ext)),
            128,256L*1024*1024,TimeSpan.FromDays(7),current==null?null:[current],TimeSpan.FromMinutes(5));
    }
    public static void MaintainPixelFiles()
    {
        OwnedFileRetention.Pixels(Path.Combine(Path.GetTempPath(),"ZZZwallpaper-masks"),".mask");
        OwnedFileRetention.Pixels(Path.Combine(Path.GetTempPath(),"ZZZwallpaper-particles"),".rgba");
    }
}
