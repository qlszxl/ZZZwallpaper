using LumaWall.Contracts;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace LumaWall.Storage;

public sealed partial class LibraryStore : IDisposable, IEffectsStore, IConfigurationStore, IHistoryStore
{
    private readonly SqliteConnection database;
    // Mutations and effect/profile reads retain their ordering barrier. Catalog
    // reads use WAL snapshots and cannot accidentally join an unrelated writer.
    private SqliteConnection connection {get {CompleteEffectWrite();return database;}}
    private Dictionary<string,MediaAsset>? assetsByPath;
    public string Root { get; }
    public long PlaybackLibraryRevision {get;private set;}
    public LibraryStore(string root)
    {
        Root=root;Directory.CreateDirectory(root);Directory.CreateDirectory(Path.Combine(root,"posters"));
        database=new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=Path.Combine(root,"library.db") }.ToString());connection.Open();
        using var cmd=connection.CreateCommand();
        cmd.CommandText="""
            PRAGMA journal_mode=WAL;
            PRAGMA journal_size_limit=16777216;
            PRAGMA wal_autocheckpoint=1000;
            """;cmd.ExecuteNonQuery();
        InitializeSettingsTable();
        cmd.CommandText="""
            CREATE TABLE IF NOT EXISTS media(id TEXT PRIMARY KEY, path TEXT NOT NULL UNIQUE, name TEXT NOT NULL, bytes INTEGER NOT NULL, duration REAL, width INTEGER, height INTEGER, codec TEXT, color TEXT, decoder TEXT);
            CREATE TABLE IF NOT EXISTS binding(screen TEXT PRIMARY KEY, asset TEXT NOT NULL, fill INTEGER NOT NULL, paused INTEGER NOT NULL, revision INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS profile(screen TEXT NOT NULL,asset TEXT NOT NULL,fill INTEGER NOT NULL,effects TEXT NOT NULL,PRIMARY KEY(screen,asset));
            CREATE TABLE IF NOT EXISTS screen_profile(screen TEXT PRIMARY KEY,fill INTEGER NOT NULL,effects TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS folder(id TEXT PRIMARY KEY,path TEXT NOT NULL COLLATE NOCASE UNIQUE,recursive INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE IF NOT EXISTS folder_member(folder TEXT NOT NULL,asset TEXT NOT NULL,ordinal INTEGER NOT NULL,PRIMARY KEY(folder,asset));
            CREATE INDEX IF NOT EXISTS folder_member_asset ON folder_member(asset);
            """;cmd.ExecuteNonQuery();
        using var columns=connection.CreateCommand();columns.CommandText="PRAGMA table_info(binding)";
        bool hasEffects=false;using(var r=columns.ExecuteReader())while(r.Read())if(r.GetString(1)=="effects")hasEffects=true;
        if(!hasEffects) {cmd.CommandText="ALTER TABLE binding ADD COLUMN effects TEXT NOT NULL DEFAULT '{}'";cmd.ExecuteNonQuery();}
        // Prefer the last committed wallpaper when migrating existing per-video profiles.
        // An existing screen profile always wins, including an explicitly cleared one.
        cmd.CommandText="""
            INSERT OR IGNORE INTO screen_profile(screen,fill,effects) SELECT screen,fill,effects FROM binding;
            INSERT OR IGNORE INTO screen_profile(screen,fill,effects)
                SELECT screen,fill,effects FROM profile WHERE rowid IN (SELECT MAX(rowid) FROM profile GROUP BY screen);
            PRAGMA user_version=4;
            """;cmd.ExecuteNonQuery();InitializeEffectStorage();MaintainStorage();MigrateWallpaperPositions();
    }
    private void InitializeSettingsTable()
    {
        using var transaction = database.BeginTransaction();
        using var command = database.CreateCommand();
        command.Transaction = transaction;
        // Missing keys in an existing library still carry that version's defaults.
        command.CommandText = "SELECT 1 FROM sqlite_schema WHERE type='table' LIMIT 1";
        bool newLibrary = command.ExecuteScalar() is null;
        command.CommandText = "CREATE TABLE IF NOT EXISTS settings(key TEXT PRIMARY KEY, value TEXT NOT NULL)";
        command.ExecuteNonQuery();
        if (newLibrary)
        {
            command.CommandText = """
                INSERT INTO settings(key,value) VALUES
                    ('fpsLimit','30'),
                    ('pause.HighResourceLoad','true'),
                    ('audioAutoAdapt','false');
                """;
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public List<MediaAsset> Assets()
    {
        using var cmd=database.CreateCommand();cmd.CommandText="SELECT id,path,name,bytes,duration,width,height,codec,color,decoder FROM media ORDER BY rowid DESC";
        using var r=cmd.ExecuteReader();var list=new List<MediaAsset>();
        while(r.Read()) list.Add(new(r.GetString(0),r.GetString(1),r.GetString(2),r.GetInt64(3),r.GetDouble(4),r.GetInt32(5),r.GetInt32(6),r.GetString(7),r.GetString(8),r.GetString(9)));
        return list;
    }
    public static string NormalizePath(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    public MediaAsset? FindAssetByPath(string path)
    {
        assetsByPath??=Assets().GroupBy(asset=>NormalizePath(asset.Path),StringComparer.OrdinalIgnoreCase).ToDictionary(group=>group.Key,group=>group.First(),StringComparer.OrdinalIgnoreCase);
        return assetsByPath.GetValueOrDefault(NormalizePath(path));
    }
    public void Upsert(MediaAsset asset)
    {
        asset=asset with {Path=NormalizePath(asset.Path)};
        var existing=FindAssetByPath(asset.Path);
        if(existing!=null&&existing.Id!=asset.Id)throw new IOException("这个文件已在视频库中，请使用已有条目。");
        string? oldPath;
        using(var previous=connection.CreateCommand())
        {
            previous.CommandText="SELECT path FROM media WHERE id=$id";previous.Parameters.AddWithValue("$id",asset.Id);oldPath=previous.ExecuteScalar() as string;
        }
        using var cmd=connection.CreateCommand();cmd.CommandText="INSERT INTO media VALUES($id,$path,$name,$bytes,$duration,$width,$height,$codec,$color,$decoder) ON CONFLICT(id) DO UPDATE SET path=excluded.path,name=excluded.name,bytes=excluded.bytes,duration=excluded.duration,width=excluded.width,height=excluded.height,codec=excluded.codec,color=excluded.color,decoder=excluded.decoder";
        cmd.Parameters.AddWithValue("$id",asset.Id);cmd.Parameters.AddWithValue("$path",asset.Path);cmd.Parameters.AddWithValue("$name",asset.Name);cmd.Parameters.AddWithValue("$bytes",asset.Bytes);cmd.Parameters.AddWithValue("$duration",asset.Duration);cmd.Parameters.AddWithValue("$width",asset.Width);cmd.Parameters.AddWithValue("$height",asset.Height);cmd.Parameters.AddWithValue("$codec",asset.Codec);cmd.Parameters.AddWithValue("$color",asset.Color);cmd.Parameters.AddWithValue("$decoder",asset.Decoder);cmd.ExecuteNonQuery();
        if(assetsByPath!=null)
        {
            if(oldPath!=null&&!string.Equals(NormalizePath(oldPath),asset.Path,StringComparison.OrdinalIgnoreCase))assetsByPath.Remove(NormalizePath(oldPath));
            assetsByPath[asset.Path]=asset;
        }
        foreach(var key in audioMetadataCache.Keys.Where(a=>a.Id==asset.Id).ToArray())audioMetadataCache.Remove(key);
        PlaybackLibraryRevision++;
    }
    public List<ScreenBinding> Bindings()
    {
        using var cmd=connection.CreateCommand();cmd.CommandText="SELECT screen,asset,fill,paused,revision,effects FROM binding";using var r=cmd.ExecuteReader();var list=new List<ScreenBinding>();
        while(r.Read()) list.Add(new(r.GetString(0),r.GetString(1),(FillMode)r.GetInt32(2),r.GetInt32(3)!=0,r.GetInt64(4)) {Effects=DecodeEffects(r.GetString(5))});return list;
    }
    public void SaveBinding(ScreenBinding binding,bool saveProfile=false)
    {
        var effectsJson=EncodeEffects(binding.Effects);
        var baseline=ScreenProfile(binding.ScreenId)?.Effects??VideoEffects.Original;
        string screenEffects="";
        if(saveProfile) {
            var specific=AssetProfile(binding.AssetId);
            var copied=AssetEffects.Copy(binding.Effects,baseline,EffectCollection.All&~(specific.Overrides|EffectKind.MaskLibrary),true);
            // Effective asset ordering is independent of its parameter overrides
            // and must never be promoted back to the display when applying it.
            var displayLayers=specific.Effects.GetCollection().HasCustomOrder?baseline.GetCollection().Layers:binding.Effects.GetCollection().Layers;
            copied=copied with {Collection=copied.GetCollection() with {Layers=EffectOrder.Clone(displayLayers)}};
            screenEffects=EncodeEffects(copied);
        }
        using var transaction=connection.BeginTransaction();
        using var cmd=connection.CreateCommand();cmd.CommandText="INSERT INTO binding(screen,asset,fill,paused,revision,effects) VALUES($screen,$asset,$fill,$paused,$revision,$effects) ON CONFLICT(screen) DO UPDATE SET asset=excluded.asset,fill=excluded.fill,paused=excluded.paused,revision=excluded.revision,effects=excluded.effects";
        cmd.Transaction=transaction;cmd.Parameters.AddWithValue("$screen",binding.ScreenId);cmd.Parameters.AddWithValue("$asset",binding.AssetId);cmd.Parameters.AddWithValue("$fill",(int)binding.Fill);cmd.Parameters.AddWithValue("$paused",binding.UserPaused?1:0);cmd.Parameters.AddWithValue("$revision",binding.Revision);cmd.Parameters.AddWithValue("$effects",effectsJson);cmd.ExecuteNonQuery();
        if(saveProfile) {cmd.Parameters["$effects"].Value=screenEffects;cmd.CommandText="INSERT INTO screen_profile VALUES($screen,$fill,$effects) ON CONFLICT(screen) DO UPDATE SET fill=excluded.fill,effects=excluded.effects";cmd.ExecuteNonQuery();}
        transaction.Commit();
    }
    public WallpaperProfile? Profile(string screen,string asset) => EffectiveProfile(screen,asset);
    public WallpaperProfile? ScreenProfile(string screen)
    {
        using var cmd=connection.CreateCommand();cmd.CommandText="SELECT fill,effects FROM screen_profile WHERE screen=$screen";cmd.Parameters.AddWithValue("$screen",screen);
        using var r=cmd.ExecuteReader();if(!r.Read())return null;
        var fill=(FillMode)r.GetInt32(0);var json=r.GetString(1);r.Close();return new(fill,DecodeEffects(json));
    }
    public void SaveProfile(string screen,string asset,WallpaperProfile profile)
    {
        using var history=CaptureUndo("修改显示器效果","screen:"+screen,new UndoScope("screen_profile","screen",screen));
        var effectsJson=EncodeEffects(profile.Effects);
        using var transaction=connection.BeginTransaction();
        using var cmd=connection.CreateCommand();cmd.Transaction=transaction;
        cmd.CommandText="INSERT INTO screen_profile VALUES($screen,$fill,$effects) ON CONFLICT(screen) DO UPDATE SET fill=excluded.fill,effects=excluded.effects; UPDATE binding SET fill=$fill,effects=$effects WHERE screen=$screen";
        cmd.Parameters.AddWithValue("$screen",screen);cmd.Parameters.AddWithValue("$fill",(int)profile.Fill);cmd.Parameters.AddWithValue("$effects",effectsJson);cmd.ExecuteNonQuery();transaction.Commit();
    }
    // Sharing replaces one effects profile and the component snapshot together.
    // The previous state lives only in this local database, never in a share code.
    public void SaveSharedConfiguration(WidgetConfiguration widgets,string? screen,string? asset,WallpaperProfile? profile,string? undo)
    {
        string configuration=widgets.ReconcileLocalState().ToJson();profile?.Effects.Validate();
        if(profile!=null&&string.IsNullOrWhiteSpace(screen))throw new ArgumentException("请选择效果要保存到的显示器。");
        var scopes=new List<UndoScope> {new("settings","key","widgets.v1"),new("settings","key","sharing.undo.v1")};
        if(profile!=null)scopes.Add(new("screen_profile","screen",screen!));
        BreakUndoGesture();
        using var history=CaptureUndo(undo==null?"恢复导入前配置":"导入分享配置","sharing",scopes.ToArray());
        using var transaction=connection.BeginTransaction();effectTransaction=transaction;
        try {
            string? effects=profile==null?null:EncodeEffects(profile.Effects);
            using var cmd=connection.CreateCommand();cmd.Transaction=transaction;
            cmd.CommandText="INSERT INTO settings(key,value) VALUES('widgets.v1',$widgets) ON CONFLICT(key) DO UPDATE SET value=excluded.value";
            cmd.Parameters.AddWithValue("$widgets",configuration);cmd.ExecuteNonQuery();cmd.Parameters.Clear();
            if(undo==null)cmd.CommandText="DELETE FROM settings WHERE key='sharing.undo.v1'";
            else {cmd.CommandText="INSERT INTO settings(key,value) VALUES('sharing.undo.v1',$undo) ON CONFLICT(key) DO UPDATE SET value=excluded.value";cmd.Parameters.AddWithValue("$undo",EncodeSharingBackup(undo));}
            cmd.ExecuteNonQuery();
            if(profile!=null) {
                cmd.Parameters.Clear();cmd.Parameters.AddWithValue("$screen",screen!);
                cmd.Parameters.AddWithValue("$fill",(int)profile.Fill);cmd.Parameters.AddWithValue("$effects",effects!);
                cmd.CommandText="INSERT INTO screen_profile(screen,fill,effects) VALUES($screen,$fill,$effects) ON CONFLICT(screen) DO UPDATE SET fill=excluded.fill,effects=excluded.effects";cmd.ExecuteNonQuery();
                cmd.CommandText="UPDATE binding SET fill=$fill,effects=$effects WHERE screen=$screen";cmd.ExecuteNonQuery();
            }
            transaction.Commit();
        } finally {effectTransaction=null;BreakUndoGesture();}
    }

    public void Remove(string assetId)
    {
        using var history=CaptureUndo("移除素材引用","remove:"+assetId,
            new UndoScope("media","id",assetId),new UndoScope("asset_effects","asset",assetId),
            new UndoScope("folder_member","asset",assetId),new UndoScope("profile","asset",assetId),new UndoScope("binding","asset",assetId));
        using var transaction=connection.BeginTransaction();using var cmd=connection.CreateCommand();cmd.Transaction=transaction;
        cmd.CommandText="DELETE FROM asset_effects WHERE asset=$id; DELETE FROM folder_member WHERE asset=$id; DELETE FROM profile WHERE asset=$id; DELETE FROM binding WHERE asset=$id; DELETE FROM media WHERE id=$id";cmd.Parameters.AddWithValue("$id",assetId);cmd.ExecuteNonQuery();transaction.Commit();assetsByPath=null;PlaybackLibraryRevision++;
    }
    public void RemoveBindings(string? screen=null)
    {
        using var cmd=connection.CreateCommand();cmd.CommandText=screen==null?"DELETE FROM binding":"DELETE FROM binding WHERE screen=$screen";
        if(screen!=null)cmd.Parameters.AddWithValue("$screen",screen);cmd.ExecuteNonQuery();
    }
    public string? GetSetting(string key)
    {
        var value=ReadStoredSetting(key);
        return key=="sharing.undo.v1"&&value!=null?DecodeSharingBackup(value):value;
    }
    public IReadOnlyDictionary<string,string> GetSettingsByPrefix(string prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        // Capture one independent snapshot on the connection's owning thread.
        // Prefixes are literal and case-sensitive, including SQL wildcard characters.
        using var cmd=connection.CreateCommand();cmd.Transaction=effectTransaction;
        cmd.CommandText="SELECT key,value FROM settings WHERE substr(key,1,length($prefix))=$prefix COLLATE BINARY";
        cmd.Parameters.AddWithValue("$prefix",prefix);
        using var reader=cmd.ExecuteReader();var values=new Dictionary<string,string>(StringComparer.Ordinal);
        while(reader.Read())values.Add(reader.GetString(0),reader.GetString(1));
        reader.Close();
        if(values.TryGetValue("sharing.undo.v1",out var backup))values["sharing.undo.v1"]=DecodeSharingBackup(backup);
        return values;
    }
    private string? ReadStoredSetting(string key)
    {
        // Prepared effect writes never change settings. The sharing backup is
        // decoded separately through its existing effect-resource barrier.
        using var cmd=database.CreateCommand();cmd.Transaction=effectTransaction;cmd.CommandText="SELECT value FROM settings WHERE key=$key";cmd.Parameters.AddWithValue("$key",key);return cmd.ExecuteScalar() as string;
    }
    public void SetSetting(string key,string value)
    {
        if(key=="widgets.v1")value=WidgetConfiguration.FromJson(value).ReconcileLocalState().ToJson();
        bool screenSwitch=key.StartsWith("screen.effects.enabled:",StringComparison.Ordinal)||key.StartsWith("screen.widgets.enabled:",StringComparison.Ordinal);
        bool position=key.StartsWith("wallpaper.position:",StringComparison.Ordinal),startup=key.StartsWith("startup.delay.",StringComparison.Ordinal);
        using var history=screenSwitch||position||startup||key=="widgets.v1"||(key=="audioAutoAdapt"&&GetSetting(key)!=null)||key.StartsWith("playback:",StringComparison.Ordinal)
            ?CaptureUndo(screenSwitch?"修改显示器总开关":position?"调整壁纸位置":startup?"修改启动延时":key=="widgets.v1"?"修改桌面组件":key=="audioAutoAdapt"?"修改音频自动适应":"修改播放队列","setting:"+key,new UndoScope("settings","key",key)):new NoUndo();
        if(key=="sharing.undo.v1")value=EncodeSharingBackup(value);
        using var cmd=connection.CreateCommand();cmd.CommandText="INSERT INTO settings VALUES($key,$value) ON CONFLICT(key) DO UPDATE SET value=excluded.value";cmd.Parameters.AddWithValue("$key",key);cmd.Parameters.AddWithValue("$value",value);cmd.ExecuteNonQuery();
    }
    public PlaybackSettings Playback(string screen)
    {
        try {
            string? json=GetSetting("playback:"+screen);
            if(json==null)return new PlaybackSettings().Validate();
            var settings=(JsonSerializer.Deserialize<PlaybackSettings>(json)??new()).Validate();
            using var document=JsonDocument.Parse(json);
            if(document.RootElement.ValueKind!=JsonValueKind.Object)return settings;
            bool legacy=!document.RootElement.TryGetProperty(nameof(PlaybackSettings.Version),out var version)||
                !version.TryGetInt32(out int number)||number<2;
            if(legacy) {
                // Adopt the new default once. Explicit choices made in v2,
                // including SingleLoop, remain unchanged on later launches.
                if(settings.Mode==PlaybackMode.SingleLoop)settings=settings with {Mode=PlaybackMode.Automatic};
                SavePlayback(screen,settings);
            }
            return settings;
        }
        catch(JsonException) {return new PlaybackSettings().Validate();}
    }
    public void SavePlayback(string screen,PlaybackSettings settings) {SetSetting("playback:"+screen,JsonSerializer.Serialize(settings.Validate()));PlaybackLibraryRevision++;}
    public List<MediaFolder> Folders()
    {
        using var cmd=database.CreateCommand();cmd.CommandText="SELECT id,path,recursive FROM folder ORDER BY path COLLATE NOCASE";
        using var reader=cmd.ExecuteReader();var folders=new List<MediaFolder>();
        while(reader.Read())folders.Add(new(reader.GetString(0),reader.GetString(1),reader.GetInt32(2)!=0));
        return folders;
    }
    public MediaFolder SaveFolder(MediaFolder folder)
    {
        string path=NormalizePath(folder.Path);
        var existing=Folders().FirstOrDefault(value=>string.Equals(NormalizePath(value.Path),path,StringComparison.OrdinalIgnoreCase));
        folder=folder with {Id=existing?.Id??folder.Id,Path=path};
        using var cmd=connection.CreateCommand();
        cmd.CommandText="INSERT INTO folder(id,path,recursive) VALUES($id,$path,$recursive) ON CONFLICT(id) DO UPDATE SET path=excluded.path,recursive=excluded.recursive";
        cmd.Parameters.AddWithValue("$id",folder.Id);cmd.Parameters.AddWithValue("$path",folder.Path);cmd.Parameters.AddWithValue("$recursive",folder.IncludeSubfolders?1:0);cmd.ExecuteNonQuery();
        PlaybackLibraryRevision++;return folder;
    }
    public List<MediaAsset> FolderAssets(string folderId)
    {
        using var cmd=database.CreateCommand();
        cmd.CommandText="SELECT m.id,m.path,m.name,m.bytes,m.duration,m.width,m.height,m.codec,m.color,m.decoder FROM folder_member f JOIN media m ON m.id=f.asset WHERE f.folder=$folder ORDER BY f.ordinal,m.path COLLATE NOCASE";
        cmd.Parameters.AddWithValue("$folder",folderId);using var reader=cmd.ExecuteReader();var assets=new List<MediaAsset>();
        while(reader.Read())assets.Add(new(reader.GetString(0),reader.GetString(1),reader.GetString(2),reader.GetInt64(3),reader.GetDouble(4),reader.GetInt32(5),reader.GetInt32(6),reader.GetString(7),reader.GetString(8),reader.GetString(9)));
        return assets;
    }
    public void ReplaceFolderAssets(string folderId,IEnumerable<string> assetIds)
    {
        using var transaction=connection.BeginTransaction();using var cmd=connection.CreateCommand();cmd.Transaction=transaction;
        cmd.CommandText="DELETE FROM folder_member WHERE folder=$folder";cmd.Parameters.AddWithValue("$folder",folderId);cmd.ExecuteNonQuery();
        cmd.CommandText="INSERT INTO folder_member(folder,asset,ordinal) SELECT $folder,$asset,$ordinal WHERE EXISTS(SELECT 1 FROM media WHERE id=$asset) AND EXISTS(SELECT 1 FROM folder WHERE id=$folder)";
        var asset=cmd.Parameters.Add("$asset",SqliteType.Text);var ordinal=cmd.Parameters.Add("$ordinal",SqliteType.Integer);int position=0;
        foreach(string id in assetIds.Distinct(StringComparer.Ordinal)) {asset.Value=id;ordinal.Value=position++;cmd.ExecuteNonQuery();}
        transaction.Commit();PlaybackLibraryRevision++;
    }
    public void RemoveFolder(string folderId)
    {
        var folder=Folders().FirstOrDefault(value=>value.Id==folderId);
        string name=folder==null?"文件夹播放列表":Path.GetFileName(Path.TrimEndingDirectorySeparator(folder.Path));
        if(string.IsNullOrWhiteSpace(name))name="文件夹播放列表";
        var members=FolderAssets(folderId).OrderBy(asset=>asset.Name,StringComparer.CurrentCultureIgnoreCase).ThenBy(asset=>asset.Path,StringComparer.OrdinalIgnoreCase).Select(asset=>asset.Id).ToList();
        var replacements=new List<(string Key,string Value)>();
        using(var query=connection.CreateCommand())
        {
            query.CommandText="SELECT key,value FROM settings WHERE key LIKE 'playback:%'";using var reader=query.ExecuteReader();
            while(reader.Read())
            {
                try
                {
                    var settings=JsonSerializer.Deserialize<PlaybackSettings>(reader.GetString(1))?.Validate();
                    if(settings?.FolderId==folderId)
                    {
                        var playlists=settings.CustomPlaylists!.ToList();
                        string playlistName=name;int suffix=2;
                        while(playlists.Any(playlist=>string.Equals(playlist.Name,playlistName,StringComparison.OrdinalIgnoreCase)))playlistName=name+" "+suffix++;
                        var playlist=new PlaybackPlaylist {Id=Guid.NewGuid().ToString("N"),Name=playlistName,AssetIds=members.ToList()};
                        playlists.Add(playlist);
                        replacements.Add((reader.GetString(0),JsonSerializer.Serialize((settings with {FolderId=null,CustomPlaylists=playlists,PlaylistId=playlist.Id,AssetIds=members.ToList()}).Validate())));
                    }
                }
                catch(JsonException) { }
            }
        }
        using var transaction=connection.BeginTransaction();using var cmd=connection.CreateCommand();cmd.Transaction=transaction;
        cmd.CommandText="DELETE FROM folder_member WHERE folder=$id;DELETE FROM folder WHERE id=$id";cmd.Parameters.AddWithValue("$id",folderId);cmd.ExecuteNonQuery();
        cmd.CommandText="UPDATE settings SET value=$value WHERE key=$key";cmd.Parameters.Clear();var key=cmd.Parameters.Add("$key",SqliteType.Text);var value=cmd.Parameters.Add("$value",SqliteType.Text);
        foreach(var replacement in replacements) {key.Value=replacement.Key;value.Value=replacement.Value;cmd.ExecuteNonQuery();}
        transaction.Commit();PlaybackLibraryRevision++;
    }
    public string Poster(string id) => Path.Combine(Root,"posters",id+".bmp");
    public void Dispose() {if(storeDisposed)return;MaintainStorage();storeDisposed=true;connection.Dispose();}
}
