using System.Text.Json.Nodes;
using System.Security.Cryptography;
using System.Text;
using LumaWall.Contracts;

namespace LumaWall.Storage;

public sealed partial class LibraryStore
{
    private void InitializeEffectStorage()
    {
        using var command=connection.CreateCommand();
        command.CommandText="CREATE TABLE IF NOT EXISTS asset_effects(asset TEXT PRIMARY KEY, overrides INTEGER NOT NULL,effects TEXT NOT NULL); CREATE TABLE IF NOT EXISTS effect_resource(id TEXT PRIMARY KEY,data TEXT NOT NULL); PRAGMA user_version=5;";
        command.ExecuteNonQuery();
        if(GetSetting("effects.resources.v1")!="true"||GetSetting("effects.resources.v2")!="true"||GetSetting("effects.resources.v3")!="true") {
            using var transaction=connection.BeginTransaction();effectTransaction=transaction;
            try {
                foreach(var table in new[] {"screen_profile","binding","profile","asset_effects"}) {
                    using var read=connection.CreateCommand();read.Transaction=transaction;read.CommandText=$"SELECT rowid,effects FROM {table}";
                    var rows=new List<(long Id,string Json)>();
                    using(var reader=read.ExecuteReader())while(reader.Read())rows.Add((reader.GetInt64(0),reader.GetString(1)));
                    foreach(var row in rows) {
                        string encoded=EncodeEffects(DecodeEffects(row.Json));
                        using var update=connection.CreateCommand();update.Transaction=transaction;update.CommandText=$"UPDATE {table} SET effects=$effects WHERE rowid=$id";
                        update.Parameters.AddWithValue("$effects",encoded);update.Parameters.AddWithValue("$id",row.Id);update.ExecuteNonQuery();
                    }
                }
                if(ReadStoredSetting("sharing.undo.v1") is { } backup) {
                    string encoded=EncodeSharingBackup(DecodeSharingBackup(backup));
                    using var update=connection.CreateCommand();update.Transaction=transaction;
                    update.CommandText="UPDATE settings SET value=$value WHERE key='sharing.undo.v1'";
                    update.Parameters.AddWithValue("$value",encoded);update.ExecuteNonQuery();
                }
                using var marker=connection.CreateCommand();marker.Transaction=transaction;
                marker.CommandText="INSERT INTO settings VALUES('effects.resources.v1','true'),('effects.resources.v2','true'),('effects.resources.v3','true') ON CONFLICT(key) DO UPDATE SET value='true'";marker.ExecuteNonQuery();transaction.Commit();
            }finally {effectTransaction=null;}
        }
    }
    private string EncodeEffects(VideoEffects effects)
    {
        return EncodeEffectResources(effects,SaveEffectResource);
    }
    private void SaveEffectResource(string id,string image)
    {
        using var command=connection.CreateCommand();command.Transaction=effectTransaction;
        command.CommandText="INSERT OR IGNORE INTO effect_resource VALUES($id,$data)";
        command.Parameters.AddWithValue("$id",id);command.Parameters.AddWithValue("$data",image);command.ExecuteNonQuery();
    }
    private VideoEffects DecodeEffects(string json)
    {
        var node=JsonNode.Parse(json)!.AsObject();
        DecodeEffectResources(node);
        return VideoEffects.FromJson(node.ToJsonString());
    }
    private void DecodeEffectResources(JsonObject node)
    {
        var resources=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var (owner,key) in EffectImageFields(node)) {
            var image=owner[key]?.GetValue<string>();if(image?.StartsWith("resource:",StringComparison.Ordinal)!=true)continue;
            string id=image[9..];
            if(!resources.TryGetValue(id,out var data)) {
                using var command=connection.CreateCommand();command.Transaction=effectTransaction;
                command.CommandText="SELECT data FROM effect_resource WHERE id=$id";command.Parameters.AddWithValue("$id",id);
                data=command.ExecuteScalar() as string??throw new InvalidDataException("效果图片资源缺失。");resources.Add(id,data);
            }
            owner[key]=data;
        }
    }
    // Shared by synchronous saves and the background transaction. Public JSON
    // remains self-contained; only database rows and history use resource IDs.
    private static string EncodeEffectResources(VideoEffects effects,Action<string,string> save)
    {
        var node=JsonNode.Parse(effects.ToJson())!.AsObject();
        EncodeEffectResources(node,save);
        return node.ToJsonString();
    }
    private static void EncodeEffectResources(JsonObject node,Action<string,string> save)
    {
        var resources=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var (owner,key) in EffectImageFields(node)) {
            string image=owner[key]?.GetValue<string>()??"";if(image.Length==0)continue;
            if(!resources.TryGetValue(image,out var id)) {
                id=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(image)));
                resources.Add(image,id);save(id,image);
            }
            owner[key]="resource:"+id;
        }
    }
    private string EncodeSharingBackup(string json)
    {
        var node=JsonNode.Parse(json)!.AsObject();
        if(node["Profile"]?["Effects"] is JsonObject effects)EncodeEffectResources(effects,SaveEffectResource);
        return node.ToJsonString();
    }
    private string DecodeSharingBackup(string json)
    {
        var node=JsonNode.Parse(json)!.AsObject();
        if(node["Profile"]?["Effects"] is JsonObject effects)DecodeEffectResources(effects);
        return node.ToJsonString();
    }
    private static IEnumerable<(JsonObject Owner,string Key)> EffectImageFields(JsonObject node)
    {
        if(node["Masks"] is JsonArray masks)foreach(var mask in masks.OfType<JsonObject>())yield return (mask,"Image");
        if(node["ScanCards"] is not JsonObject scan)yield break;
        if(scan["Images"] is JsonArray images)foreach(var image in images.OfType<JsonObject>()) {
            yield return (image,"Data");yield return (image,"MediaData");
        }
        if(scan["CenterImage"] is JsonObject center) {yield return (center,"Data");yield return (center,"MediaData");}
    }
    private Microsoft.Data.Sqlite.SqliteTransaction? effectTransaction;
    public AssetEffects AssetProfile(string asset)
    {
        using var command=connection.CreateCommand();command.Transaction=effectTransaction;
        command.CommandText="SELECT overrides,effects FROM asset_effects WHERE asset=$asset";command.Parameters.AddWithValue("$asset",asset);
        using var reader=command.ExecuteReader();
        if(!reader.Read())return new();
        var kinds=(EffectKind)reader.GetInt64(0);var json=reader.GetString(1);reader.Close();
        var effects=DecodeEffects(json);
        var stored=JsonNode.Parse(json)!;
        if((stored["Version"]?.GetValue<int>()??1)<=38&&(kinds&EffectKind.AudioReactive)!=0) {
            // The old audio override also owned zoom, including an explicit off.
            kinds|=EffectKind.Transform;
        }
        return new AssetEffects {Overrides=kinds,Effects=effects}.Validate();
    }
    public WallpaperProfile EffectiveProfile(string screen,string asset)
    {
        var baseline=ScreenProfile(screen)??new WallpaperProfile(FillMode.Cover,VideoEffects.Original);
        return baseline with {Effects=AssetProfile(asset).Resolve(baseline.Effects)};
    }
    public WallpaperProfile EditingProfile(string screen,string? asset)
    {
        var baseline=ScreenProfile(screen)??new WallpaperProfile(FillMode.Cover,VideoEffects.Original);
        return asset==null?baseline:baseline with {Effects=AssetProfile(asset).OwnEffects(baseline.Effects)};
    }
    public void SaveEffectDraft(string screen,string asset,WallpaperProfile draft,EffectKind overrides)
    {
        var old=AssetProfile(asset);var screenProfile=ScreenProfile(screen)??new(FillMode.Cover,VideoEffects.Original);
        var own=AssetEffects.Copy(draft.Effects,old.Effects,overrides,true);
        own=own with {Collection=own.GetCollection() with {Layers=EffectOrder.ApplyDelta(old.Effects.GetCollection(),old.Resolve(screenProfile.Effects).GetCollection(),draft.Effects.GetCollection())}};
        var specific=new AssetEffects {Overrides=overrides,Effects=own}.Validate();
        // Store each group in its own scope, preserving dormant screen defaults.
        var screenLinks=draft.Effects.EffectMasks.Where(p=>(p.Key&overrides)==0).ToDictionary(p=>p.Key,p=>p.Value);
        var privateIds=specific.Effects.Masks.Select(m=>m.Id).ToHashSet();
        var screenSource=draft.Effects with {EffectMasks=screenLinks,Masks=draft.Effects.Masks.Where(m=>!privateIds.Contains(m.Id)||screenLinks.ContainsValue(m.Id)||screenProfile.Effects.Masks.Any(oldMask=>oldMask.Id==m.Id)).ToArray()};
        var screenNext=screenProfile with {Fill=draft.Fill,Effects=AssetEffects.Copy(screenSource,screenProfile.Effects,EffectCollection.All&~overrides,true)};
        screenNext=screenNext with {Effects=screenNext.Effects with {Collection=screenNext.Effects.GetCollection() with {Layers=EffectOrder.Clone(screenProfile.Effects.GetCollection().Layers)}}};
        SaveEffectLayers(screen,asset,screenNext,specific);
    }
    public void SaveEffectLayers(string screen,string asset,WallpaperProfile baseline,AssetEffects specific)
    {
        using var history=CaptureUndo("修改素材效果","asset:"+asset,new UndoScope("screen_profile","screen",screen),new UndoScope("asset_effects","asset",asset));
        specific.Validate();specific.Resolve(baseline.Effects).Validate();
        if(!Assets().Any(a=>a.Id==asset))throw new ArgumentException("素材不存在。");
        using var transaction=connection.BeginTransaction();effectTransaction=transaction;
        try {
            using var command=connection.CreateCommand();command.Transaction=transaction;
            command.CommandText="INSERT INTO screen_profile VALUES($screen,$fill,$effects) ON CONFLICT(screen) DO UPDATE SET fill=excluded.fill,effects=excluded.effects";
            command.Parameters.AddWithValue("$screen",screen);command.Parameters.AddWithValue("$fill",(int)baseline.Fill);command.Parameters.AddWithValue("$effects",EncodeEffects(baseline.Effects));command.ExecuteNonQuery();
            command.Parameters.Clear();command.CommandText="INSERT INTO asset_effects VALUES($asset,$overrides,$effects) ON CONFLICT(asset) DO UPDATE SET overrides=excluded.overrides,effects=excluded.effects";
            command.Parameters.AddWithValue("$asset",asset);command.Parameters.AddWithValue("$overrides",(long)specific.Overrides);command.Parameters.AddWithValue("$effects",EncodeEffects(specific.Effects));command.ExecuteNonQuery();
            transaction.Commit();
        }finally {effectTransaction=null;}
    }
}
