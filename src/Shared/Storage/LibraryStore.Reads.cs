using LumaWall.Contracts;
using Microsoft.Data.Sqlite;

namespace LumaWall.Storage;

public sealed partial class LibraryStore
{
    // A read-only connection belongs exclusively to this worker. No UI-owned
    // connection, undo scope or mutable cache is passed across threads.
    public Task<List<MediaAsset>> ReadLibraryAssetsAsync(string? folderId, CancellationToken token)
        =>ReadLibraryAssetsCoreAsync(folderId,token,false);

    public async Task<LibraryQueryResult> QueryLibraryAssetsAsync(string? folderId,LibraryFilter filter,CancellationToken token)
    {
        filter=filter.Validate();
        var assets=await ReadLibraryAssetsCoreAsync(folderId,token,true).ConfigureAwait(false);
        return new(filter.Apply(assets,token),assets.Count);
    }

    private Task<List<MediaAsset>> ReadLibraryAssetsCoreAsync(string? folderId,CancellationToken token,bool recent)
    {
        string source = database.DataSource;
        return Task.Run(() => {
            token.ThrowIfCancellationRequested();
            using var readerConnection = new SqliteConnection(new SqliteConnectionStringBuilder {
                DataSource = source, Mode = SqliteOpenMode.ReadOnly, Cache = SqliteCacheMode.Private
            }.ToString());
            readerConnection.Open();
            using var command = readerConnection.CreateCommand();
            command.CommandText = folderId == null
                ? "SELECT id,path,name,bytes,duration,width,height,codec,color,decoder FROM media ORDER BY rowid DESC"
                : "SELECT m.id,m.path,m.name,m.bytes,m.duration,m.width,m.height,m.codec,m.color,m.decoder FROM folder_member f JOIN media m ON m.id=f.asset WHERE f.folder=$folder ORDER BY f.ordinal,m.path COLLATE NOCASE";
            if (folderId != null) command.Parameters.AddWithValue("$folder", folderId);
            if(folderId!=null&&recent)command.CommandText=command.CommandText.Replace("ORDER BY f.ordinal,m.path COLLATE NOCASE","ORDER BY m.rowid DESC");
            using var rows = command.ExecuteReader();
            var result = new List<MediaAsset>();
            while (rows.Read()) {
                token.ThrowIfCancellationRequested();
                result.Add(new(rows.GetString(0),rows.GetString(1),rows.GetString(2),rows.GetInt64(3),rows.GetDouble(4),rows.GetInt32(5),rows.GetInt32(6),rows.GetString(7),rows.GetString(8),rows.GetString(9)));
            }
            return result;
        }, token);
    }

    public async Task CompleteEffectWriteAsync()
    {
        // Resume on the caller's context: undo/history still belongs to the UI.
        var pending = effectWrite;
        if (pending == null) return;
        try { await pending; }
        finally { CompleteEffectWrite(pending); }
    }
}
