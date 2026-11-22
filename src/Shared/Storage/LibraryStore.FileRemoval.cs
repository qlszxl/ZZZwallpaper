using System.Text.Json;
using System.Text.Json.Nodes;
using LumaWall.Contracts.Steam;

namespace LumaWall.Storage;

public sealed partial class LibraryStore
{
    public sealed record FileRemovalResult(string[] AssetIds, string[] Screens, int ChangedQueues);

    /// <summary>
    /// Runs on the store's owning thread. All SQL/queue validation precedes the
    /// irreversible file callback; callback failure rolls the database back.
    /// Filesystem and SQLite commits cannot form one atomic transaction: callers
    /// must report a file-already-deleted/metadata-commit-failed outcome honestly.
    /// </summary>
    public FileRemovalResult DeleteSourceFile(string path, Action deleteFile)
    {
        ArgumentNullException.ThrowIfNull(deleteFile);
        if (undoBatchDepth != 0) throw new InvalidOperationException("请先完成当前编辑操作，再删除源文件。");
        string full = NormalizePath(path);
        if (!Path.IsPathFullyQualified(path) || !File.Exists(full) || Directory.Exists(full))
            throw new IOException("源文件已不存在或不是普通文件，未删除素材记录。");
        for (string? cursor = full; cursor != null; cursor = Path.GetDirectoryName(cursor))
            if ((File.Exists(cursor) || Directory.Exists(cursor)) && (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("源文件路径包含链接，请直接管理原文件；未删除素材记录。");
        var assets = Assets().Where(a => string.Equals(NormalizePath(a.Path), full, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (assets.Length == 0) throw new InvalidOperationException("这个文件已不在素材库中，请刷新后重试。");
        foreach (var asset in assets)
        {
            string? json = GetSetting(SteamSourceIdentity.SettingKey(asset.Id));
            SteamWallpaperSource? source;
            try { source = json == null ? null : JsonSerializer.Deserialize<SteamWallpaperSource>(json); }
            catch (JsonException) { throw new IOException("Steam 来源记录无法读取，未执行文件删除。"); }
            if (SteamSourceIdentity.IsSteamManagedOriginal(full, source))
                throw new InvalidOperationException("这是 Steam / Wallpaper Engine 管理的原文件，请在 Steam 中管理或打开项目文件夹。");
        }
        var ids = assets.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
        string[] screens = Bindings().Where(b => ids.Contains(b.AssetId)).Select(b => b.ScreenId).Distinct().ToArray();
        var queues = new List<(string Key, string Json)>();
        using (var read = connection.CreateCommand())
        {
            read.CommandText = "SELECT key,value FROM settings WHERE key LIKE 'playback:%'";
            using var reader = read.ExecuteReader();
            while (reader.Read())
            {
                var node = JsonNode.Parse(reader.GetString(1)) as JsonObject
                    ?? throw new InvalidDataException("播放队列记录无法读取，未执行文件删除。");
                bool changed = RemoveIds(node["AssetIds"]);
                if (node["CustomPlaylists"] is JsonArray lists)
                    foreach (var list in lists)
                    {
                        if (list is not JsonObject item) throw new InvalidDataException("自定义播放列表记录无效，未执行文件删除。");
                        changed |= RemoveIds(item["AssetIds"]);
                    }
                else if (node["CustomPlaylists"] != null)
                    throw new InvalidDataException("自定义播放列表记录无效，未执行文件删除。");
                if (changed) queues.Add((reader.GetString(0), node.ToJsonString()));
            }
        }
        bool RemoveIds(JsonNode? value)
        {
            if (value == null) return false;
            if (value is not JsonArray array) throw new InvalidDataException("播放队列记录无效，未执行文件删除。");
            bool changed = false;
            for (int i = array.Count - 1; i >= 0; i--)
            {
                if (array[i] is not JsonValue item || !item.TryGetValue<string>(out string? id))
                    throw new InvalidDataException("播放队列素材编号无效，未执行文件删除。");
                if (ids.Contains(id!)) { array.RemoveAt(i); changed = true; }
            }
            return changed;
        }

        using (var transaction = connection.BeginTransaction())
        {
            foreach (var queue in queues)
            {
                using var update = connection.CreateCommand(); update.Transaction = transaction;
                update.CommandText = "UPDATE settings SET value=$value WHERE key=$key";
                update.Parameters.AddWithValue("$key", queue.Key); update.Parameters.AddWithValue("$value", queue.Json);
                update.ExecuteNonQuery();
            }
            foreach (string id in ids)
            {
                using var remove = connection.CreateCommand(); remove.Transaction = transaction;
                remove.CommandText = """
                    DELETE FROM asset_effects WHERE asset=$id;
                    DELETE FROM folder_member WHERE asset=$id;
                    DELETE FROM profile WHERE asset=$id;
                    DELETE FROM binding WHERE asset=$id;
                    DELETE FROM media WHERE id=$id;
                    DELETE FROM settings WHERE key=$steam OR key=$poster;
                    """;
                remove.Parameters.AddWithValue("$id", id);
                remove.Parameters.AddWithValue("$steam", SteamSourceIdentity.SettingKey(id));
                remove.Parameters.AddWithValue("$poster", "poster.choice.v1:" + id);
                remove.ExecuteNonQuery();
            }
            deleteFile();
            if (File.Exists(full) || Directory.Exists(full)) throw new IOException("文件删除未完成，素材库记录已保留。");
            transaction.Commit();
        }
        assetsByPath = null; audioMetadataCache.Clear(); PlaybackLibraryRevision++;
        // Ordinary configuration undo cannot recover a recycled/permanently
        // deleted file. Invalidate library/playback history, retaining unrelated
        // screen-effect/component edits rather than presenting a false undo.
        bool AffectedJson(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.String)
            {
                string text = element.GetString()!;
                if (ids.Contains(text) || string.Equals(text, full, StringComparison.OrdinalIgnoreCase)) return true;
                if (text.StartsWith('{') || text.StartsWith('['))
                    try { using var nested = JsonDocument.Parse(text); return AffectedJson(nested.RootElement); } catch (JsonException) { }
            }
            if (element.ValueKind == JsonValueKind.Array) return element.EnumerateArray().Any(AffectedJson);
            if (element.ValueKind == JsonValueKind.Object) return element.EnumerateObject().Any(p => AffectedJson(p.Value));
            return false;
        }
        bool ContainsFileState(UndoEntry entry) => entry.Patches.Any(p =>
        {
            if (p.Scope.Table is not ("media" or "asset_effects" or "folder_member" or "profile" or "binding" or "settings")) return false;
            if (p.Scope.Table == "settings" && !p.Scope.Value.StartsWith("playback:", StringComparison.Ordinal))
                return ids.Any(id => p.Scope.Value == SteamSourceIdentity.SettingKey(id) || p.Scope.Value == "poster.choice.v1:" + id);
            using var before = JsonDocument.Parse(p.Before); using var after = JsonDocument.Parse(p.After);
            return AffectedJson(before.RootElement) || AffectedJson(after.RootElement);
        });
        undoEdits.RemoveAll(ContainsFileState); redoEdits.RemoveAll(ContainsFileState);
        HistoryRevision++; BreakUndoGesture(); HistoryChanged?.Invoke();
        return new(ids.ToArray(), screens, queues.Count);
    }
}
