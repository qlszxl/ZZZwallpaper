using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace LumaWall.Storage;

/// <summary>Called before opening the default store, while both application mutexes are held.</summary>
public static class UnifiedDataMigration
{
    private const string MarkerName = "identity-migration.v1.json";
    private const string RecoveryName = "ZZZwallpaper-data-migrations";
    private sealed record MigrationMarker(int Version, string RecoveryId, string Policy);
    private sealed record MigrationJournal(int Version, string RecoveryId);
    private sealed record FileRecord(string Path, long Bytes, string Sha256);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string PrepareDefaultRoot(string localApplicationData)
    {
        string parent = Path.GetFullPath(localApplicationData).TrimEnd(Path.DirectorySeparatorChar);
        CheckDirectoryChain(parent);
        string canonical = Path.Combine(parent, "LumaWall");
        string modular = Path.Combine(parent, "LumaWall.Modular");
        string recovery = Path.Combine(parent, RecoveryName);
        if (Directory.Exists(recovery)) { CheckDirectoryChain(recovery); Recover(canonical, recovery); }
        if (Directory.Exists(canonical))
        {
            CheckDirectoryChain(canonical);
            string marker = Path.Combine(canonical, MarkerName);
            if (File.Exists(marker)) { ReadMarker(marker); return canonical; }
        }
        if (!File.Exists(Path.Combine(modular, "library.db")))
        {
            // This is also a completed identity transition. A former build
            // creating its old directory later must never overwrite new edits.
            Directory.CreateDirectory(canonical); CheckDirectoryChain(canonical);
            WriteJson(Path.Combine(canonical, MarkerName), new MigrationMarker(1, Guid.NewGuid().ToString("N"), "No former modular database existed on the first unified launch."));
            return canonical;
        }
        CheckDirectoryChain(modular);
        Directory.CreateDirectory(recovery); CheckDirectoryChain(recovery);
        string id = Guid.NewGuid().ToString("N"), batch = Path.Combine(recovery, id);
        Directory.CreateDirectory(batch);
        string originalSnapshot = Path.Combine(batch, "original-snapshot");
        string modularSnapshot = Path.Combine(batch, "modular-snapshot");
        string candidate = Path.Combine(batch, "candidate");
        bool originalExists = Directory.Exists(canonical);
        if (originalExists) Snapshot(canonical, originalSnapshot, Path.Combine(batch, "original-files.json"));
        Snapshot(modular, modularSnapshot, Path.Combine(batch, "modular-files.json"));
        CopyTree(modularSnapshot, candidate, skipDatabaseSidecars: false);
        RewriteDatabase(Path.Combine(candidate, "library.db"), modular, canonical);
        foreach (string file in Files(candidate).Where(p => Path.GetExtension(p).Equals(".json", StringComparison.OrdinalIgnoreCase)))
        {
            string before = File.ReadAllText(file), after = RelocateText(before, modular, canonical);
            if (before != after) File.WriteAllText(file, after, new UTF8Encoding(false));
        }
        WriteJson(Path.Combine(candidate, MarkerName), new MigrationMarker(1, id, "Modular configuration; both original directories preserved in verified recovery snapshots."));
        VerifyDatabase(Path.Combine(candidate, "library.db"));
        WriteManifest(candidate, Path.Combine(batch, "candidate-files.json"));
        File.WriteAllText(Path.Combine(batch, "恢复说明.txt"),
            "统一软件身份前的数据备份。original-snapshot 为原版完整快照，modular-snapshot 为模块版完整快照；数据库通过 SQLite 备份包含已提交 WAL 内容。" +
            "迁移后的活动数据位于本地应用数据目录下的 LumaWall，原 LumaWall.Modular 未删除。original-directory 如存在，是切换时保留的原目录。" +
            "恢复前退出全部 ZZZwallpaper，先备份当前 LumaWall，再复制所需完整快照到 LumaWall；模块版快照含旧路径，原目录必须保留。" +
            "恢复旧快照时将当前 LumaWall 的 identity-migration.v1.json 一并保留，可防止旧模块版再次导入。请勿仅覆盖正在使用的 library.db。", new UTF8Encoding(false));
        string pending = Path.Combine(recovery, "pending.json");
        WriteJson(pending, new MigrationJournal(1, id));
        string originalDirectory = Path.Combine(batch, "original-directory");
        try
        {
            // These paths are fixed children of the validated recovery batch.
            if (originalExists) Directory.Move(canonical, originalDirectory);
            Directory.Move(candidate, canonical);
        }
        catch
        {
            if (!Directory.Exists(canonical) && Directory.Exists(originalDirectory)) Directory.Move(originalDirectory, canonical);
            throw;
        }
        File.Delete(pending);
        return canonical;
    }

    private static void Recover(string canonical, string recovery)
    {
        string pending = Path.Combine(recovery, "pending.json");
        if (!File.Exists(pending)) return;
        CheckFile(pending);
        if (new FileInfo(pending).Length > 4096) throw new IOException("数据迁移恢复记录无效，原数据已保留。");
        var journal = JsonSerializer.Deserialize<MigrationJournal>(File.ReadAllText(pending));
        if (journal == null || journal.Version != 1 || !Guid.TryParseExact(journal.RecoveryId, "N", out _)) throw new IOException("数据迁移恢复记录无效，原数据已保留。");
        // The journal never supplies filesystem paths.
        string batch = Path.Combine(recovery, journal.RecoveryId);
        CheckDirectoryChain(batch);
        string original = Path.Combine(batch, "original-directory");
        if (Directory.Exists(canonical))
        {
            CheckDirectoryChain(canonical);
            string marker = Path.Combine(canonical, MarkerName);
            if (File.Exists(marker) && ReadMarker(marker).RecoveryId == journal.RecoveryId) { File.Delete(pending); return; }
            if (Directory.Exists(original)) throw new IOException("迁移被中断后发现新的数据目录。两套数据均已保留，请先检查恢复目录。");
        }
        else if (Directory.Exists(original)) { CheckDirectoryChain(original); Directory.Move(original, canonical); }
        File.Move(pending, Path.Combine(batch, "interrupted.json"), true);
    }

    private static MigrationMarker ReadMarker(string path)
    {
        CheckFile(path);
        if (new FileInfo(path).Length > 4096) throw new IOException("数据迁移标记无效。");
        var marker = JsonSerializer.Deserialize<MigrationMarker>(File.ReadAllText(path));
        if (marker == null || marker.Version != 1 || !Guid.TryParseExact(marker.RecoveryId, "N", out _)) throw new IOException("数据迁移标记无效。");
        return marker;
    }

    private static void Snapshot(string source, string destination, string manifest)
    {
        CopyTree(source, destination, skipDatabaseSidecars: true);
        string database = Path.Combine(source, "library.db");
        if (File.Exists(database))
        {
            CheckFile(database);
            using var input = Open(database, SqliteOpenMode.ReadOnly);
            using var output = Open(Path.Combine(destination, "library.db"), SqliteOpenMode.ReadWriteCreate);
            input.BackupDatabase(output);
            MakeStandalone(output);
        }
        if (File.Exists(Path.Combine(destination, "library.db"))) VerifyDatabase(Path.Combine(destination, "library.db"));
        WriteManifest(destination, manifest);
    }

    private static void CopyTree(string source, string destination, bool skipDatabaseSidecars)
    {
        CheckDirectoryChain(source);
        if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("迁移目标已存在，未覆盖数据。");
        Directory.CreateDirectory(destination);
        void Copy(string from, string to)
        {
            foreach (string item in Directory.EnumerateFileSystemEntries(from))
            {
                var attributes = File.GetAttributes(item);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("数据目录包含链接，迁移已停止并保留原数据：" + item);
                string name = Path.GetFileName(item), target = Path.Combine(to, name);
                if ((attributes & FileAttributes.Directory) != 0) { Directory.CreateDirectory(target); Copy(item, target); continue; }
                if (skipDatabaseSidecars && from == source && name is "library.db" or "library.db-wal" or "library.db-shm" or "library.db-journal") continue;
                using var input = new FileStream(item, FileMode.Open, FileAccess.Read, FileShare.Read);
                using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { input.CopyTo(output); output.Flush(true); }
                input.Position = 0;
                if (Hash(input) != HashFile(target)) throw new IOException("数据备份校验失败，未替换原数据。");
            }
        }
        Copy(source, destination);
    }

    private static IEnumerable<string> Files(string directory)
    {
        foreach (string path in Directory.EnumerateFileSystemEntries(directory))
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("数据备份中不允许链接。");
            if ((attributes & FileAttributes.Directory) != 0) { foreach (string child in Files(path)) yield return child; }
            else yield return path;
        }
    }

    private static void WriteManifest(string directory, string path) => WriteJson(path, Files(directory).Order(StringComparer.Ordinal).Select(file =>
        new FileRecord(Path.GetRelativePath(directory, file), new FileInfo(file).Length, HashFile(file))).ToArray());

    private static void RewriteDatabase(string path, string oldRoot, string newRoot)
    {
        using var connection = Open(path, SqliteOpenMode.ReadWrite);
        using var transaction = connection.BeginTransaction();
        var tables = new List<string>();
        using (var query = connection.CreateCommand())
        {
            query.Transaction = transaction; query.CommandText = "SELECT name FROM sqlite_schema WHERE type='table' AND name NOT LIKE 'sqlite_%'";
            using var reader = query.ExecuteReader(); while (reader.Read()) tables.Add(reader.GetString(0));
        }
        foreach (string table in tables)
        {
            var changes = new List<(long Row, string Column, string Value)>();
            using (var query = connection.CreateCommand())
            {
                query.Transaction = transaction; query.CommandText = "SELECT rowid,* FROM " + Quote(table);
                using var reader = query.ExecuteReader();
                while (reader.Read()) for (int i = 1; i < reader.FieldCount; i++) if (reader.GetValue(i) is string before)
                {
                    string after = RelocateText(before, oldRoot, newRoot);
                    if (after != before) changes.Add((reader.GetInt64(0), reader.GetName(i), after));
                }
            }
            foreach (var change in changes)
            {
                using var update = connection.CreateCommand(); update.Transaction = transaction;
                update.CommandText = "UPDATE " + Quote(table) + " SET " + Quote(change.Column) + "=$value WHERE rowid=$row";
                update.Parameters.AddWithValue("$value", change.Value); update.Parameters.AddWithValue("$row", change.Row); update.ExecuteNonQuery();
            }
        }
        transaction.Commit();
        MakeStandalone(connection);
    }

    private static string RelocateText(string value, string oldRoot, string newRoot, int depth = 0)
    {
        if (depth > 16) throw new IOException("数据路径嵌套过深，迁移已停止。");
        foreach (var pair in new[] { (Old: oldRoot, New: newRoot), (Old: oldRoot.Replace('\\', '/'), New: newRoot.Replace('\\', '/')) })
            if (value.StartsWith(pair.Old, StringComparison.OrdinalIgnoreCase) && (value.Length == pair.Old.Length || value[pair.Old.Length] is '\\' or '/')) return pair.New + value[pair.Old.Length..];
        string trimmed = value.TrimStart();
        if (trimmed.Length == 0 || trimmed[0] is not ('{' or '[' or '"')) return value;
        JsonNode? node;
        try { node = JsonNode.Parse(value); } catch (JsonException) { return value; }
        bool changed = false;
        JsonNode? Visit(JsonNode? current)
        {
            if (current is JsonValue scalar && scalar.TryGetValue<string>(out var text))
            {
                string relocated = RelocateText(text, oldRoot, newRoot, depth + 1);
                if (relocated != text) { changed = true; return JsonValue.Create(relocated); }
            }
            else if (current is JsonObject obj) foreach (string key in obj.Select(p => p.Key).ToArray()) { var original = obj[key]; var updated = Visit(original); if (!ReferenceEquals(original, updated)) obj[key] = updated; }
            else if (current is JsonArray array) for (int i = 0; i < array.Count; i++) { var original = array[i]; var updated = Visit(original); if (!ReferenceEquals(original, updated)) array[i] = updated; }
            return current;
        }
        node = Visit(node); return changed ? node!.ToJsonString() : value;
    }

    private static string Quote(string name) => '"' + name.Replace("\"", "\"\"") + '"';
    private static SqliteConnection Open(string path, SqliteOpenMode mode)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = mode, Pooling = false }.ToString());
        connection.Open(); return connection;
    }
    private static void VerifyDatabase(string path)
    {
        using var connection = Open(path, SqliteOpenMode.ReadOnly); using var command = connection.CreateCommand(); command.CommandText = "PRAGMA quick_check";
        if (!string.Equals(command.ExecuteScalar() as string, "ok", StringComparison.Ordinal)) throw new IOException("数据库备份完整性检查失败，原数据未替换。");
    }
    private static void MakeStandalone(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)"; command.ExecuteNonQuery();
        command.CommandText = "PRAGMA journal_mode=DELETE";
        if (!string.Equals(command.ExecuteScalar() as string, "delete", StringComparison.OrdinalIgnoreCase)) throw new IOException("数据库快照仍依赖临时日志，迁移已停止。");
    }
    private static void CheckDirectoryChain(string path)
    {
        for (var directory = new DirectoryInfo(path); directory != null; directory = directory.Parent)
            if (!directory.Exists || (directory.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("数据迁移路径不存在或包含链接：" + directory.FullName);
    }
    private static void CheckFile(string path) { if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("数据迁移文件不能是链接。"); }
    private static string Hash(Stream stream) => Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    private static string HashFile(string path) { using var stream = File.OpenRead(path); return Hash(stream); }
    private static void WriteJson(string path, object value)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { JsonSerializer.Serialize(stream, value, value.GetType(), Json); stream.Flush(true); }
        File.Move(temporary, path, false);
    }
}
