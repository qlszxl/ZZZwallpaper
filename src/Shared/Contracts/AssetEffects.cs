using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using System.Text;
using LumaWall.Contracts.Sharing;

namespace LumaWall.Contracts;

public sealed record AssetEffects
{
    private EffectKind overrides;
    public EffectKind Overrides {get=>overrides;init=>overrides=value&~EffectCollection.Retired;}
    public VideoEffects Effects {get;init;}=VideoEffects.Original;
    public const EffectKind Allowed=EffectCollection.All&~(EffectKind.HideTaskbar|EffectKind.MaskLibrary);
    public static AssetEffects FromScreen(VideoEffects source)
    {
        var kinds=source.GetCollection().Added&Allowed;
        var copied=PrivateCopy(source,VideoEffects.Original,kinds);
        return new() {Overrides=kinds,Effects=copied with {PackageEffects=source.PackageEffects.ToArray(),AudioRules=source.AudioRules,Collection=copied.GetCollection() with {Layers=EffectOrder.Clone(source.GetCollection().Layers)}}};
    }
    public AssetEffects Validate()
    {
        if((Overrides&~Allowed)!=0)throw new ArgumentException("任务栏和遮罩资源库不支持素材专属范围。");
        Effects.Validate();return this;
    }
    public VideoEffects Resolve(VideoEffects screen)
    {
        var resolved=Copy(Effects,screen,Overrides) with {PackageEffects=screen.PackageEffects.Concat(Effects.PackageEffects).GroupBy(p=>p.Id).Select(g=>g.Last()).OrderByDescending(p=>Effects.PackageEffects.Any(e=>e.Id==p.Id)).Take(16).ToArray(),AudioRules=AudioParameters.Merge(screen.AudioRules,Effects.AudioRules)};
        var masks=resolved.Masks.Select(m=>!resolved.EffectMasks.ContainsValue(m.Id)?Effects.Masks.FirstOrDefault(own=>own.Id==m.Id)??m:m)
            .Concat(Effects.Masks.Where(m=>!resolved.Masks.Any(old=>old.Id==m.Id))).ToArray();
        var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        masks=masks.Select(mask=> {
            string name=mask.Name;int suffix=2;
            while(!names.Add(name.Trim()))name=mask.Name[..Math.Min(30,mask.Name.Length)]+" "+suffix++;
            return mask with {Name=name};
        }).ToArray();
        return (resolved with {Masks=masks,Collection=resolved.GetCollection() with {
            Layers=EffectOrder.Merge(screen.GetCollection().Layers,Effects.GetCollection().Layers),
            Added=masks.Length>0?resolved.GetCollection().Added|EffectKind.MaskLibrary:resolved.GetCollection().Added}}).Validate();
    }
    // Editing a material shows only its own layer. Playback continues to use
    // Resolve(screen), including explicit disabled overrides and rule merging.
    public VideoEffects OwnEffects(VideoEffects? screen=null)
    {
        var own=Copy(Effects,VideoEffects.Original,Overrides);
        return (own with {PackageEffects=Effects.PackageEffects,Masks=Effects.Masks,AudioRules=Effects.AudioRules,
            Collection=own.GetCollection() with {Layers=EffectOrder.Merge(screen?.GetCollection().Layers,Effects.GetCollection().Layers),
                Added=Effects.Masks.Length>0?own.GetCollection().Added|EffectKind.MaskLibrary:own.GetCollection().Added}}).Validate();
    }
    // Compare complete groups, including explicit off, strengths and mask geometry.
    // Editing a material promotes only the changed groups; unrelated display effects inherit.
    public static EffectKind ChangedKinds(VideoEffects before,VideoEffects after,bool includeOrder=true)
    {
        var from=JsonSerializer.SerializeToElement(before);var to=JsonSerializer.SerializeToElement(after);
        var old=before.GetCollection();var next=after.GetCollection();EffectKind changed=0;
        string Mask(VideoEffects value,EffectKind kind)=>value.EffectMasks.TryGetValue(kind,out var id)?JsonSerializer.Serialize(value.Masks.Single(m=>m.Id==id)):"";
        foreach(var kind in Enum.GetValues<EffectKind>().Where(k=>(k&Allowed)!=0)) {
            bool different=(old.Added&kind)!=(next.Added&kind)||old.Strength(kind)!=next.Strength(kind)||
                (kind==EffectKind.Color&&old.ColorEnabled!=next.ColorEnabled)||Mask(before,kind)!=Mask(after,kind);
            different|=ShareProjection.EffectFields[kind].Split(' ',StringSplitOptions.RemoveEmptyEntries).Where(field=>field!=nameof(VideoEffects.AudioRules)).Any(field=>from.GetProperty(field).GetRawText()!=to.GetProperty(field).GetRawText());
            if(includeOrder&&EffectOrder.Supports(kind))different|=old.GetLayer(kind)!=next.GetLayer(kind)||
                (old.HasCustomOrder!=next.HasCustomOrder&&((old.Added|next.Added)&kind)!=0);
            if(different)changed|=kind;
        }
        return changed;
    }

    // A complete effect replaces the corresponding screen effect, including an
    // explicit disabled/removed state. Unrelated screen effects remain intact.
    public static VideoEffects Copy(VideoEffects source,VideoEffects target,EffectKind kinds,bool replaceMasks=false)
    {
        var from=JsonSerializer.SerializeToNode(source)!.AsObject();
        var to=JsonSerializer.SerializeToNode(target)!.AsObject();
        var collection=target.GetCollection();var incoming=source.GetCollection();
        collection=collection with {Layers=EffectOrder.CopySelected(incoming,collection,kinds)};
        var links=new Dictionary<EffectKind,string>(target.EffectMasks);
        var masks=target.Masks.ToList();
        if((kinds&EffectKind.MaskLibrary)!=0) {
            masks=source.Masks.ToList();
            foreach(var key in links.Keys.ToArray())if(!masks.Any(m=>m.Id==links[key]))links.Remove(key);
        }
        foreach(var kind in Enum.GetValues<EffectKind>()) {
            if((kinds&kind)==0||kind==EffectKind.MaskLibrary)continue;
            foreach(var field in ShareProjection.EffectFields[kind].Split(' ',StringSplitOptions.RemoveEmptyEntries))to[field]=from[field]?.DeepClone();
            collection=collection.WithStrength(kind,incoming.Strength(kind)) with {Added=(collection.Added&~kind)|(incoming.Added&kind)};
            if(kind==EffectKind.Color)collection=collection with {ColorEnabled=incoming.ColorEnabled};
            if(kind==EffectKind.HideTaskbar)collection=collection with {HideTaskbarEnabled=incoming.HideTaskbarEnabled,ReleaseTaskbarSpace=incoming.ReleaseTaskbarSpace,TaskbarStyle=incoming.TaskbarStyle,TaskbarVisual=incoming.TaskbarVisual};
            links.Remove(kind);
            if(source.EffectMasks.TryGetValue(kind,out var id)) {
                var mask=source.Masks.First(m=>m.Id==id);
                var existing=masks.Find(m=>m.Id==id);
                if(existing!=null&&existing!=mask&&replaceMasks) {
                    masks[masks.IndexOf(existing)]=mask;existing=mask;
                }
                if(existing!=null&&existing!=mask) {
                    var content=JsonSerializer.Serialize(mask with {Id="",Name=""});
                    mask=mask with {Id="m-"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)))[..24]};
                }
                if(!masks.Any(m=>m.Id==mask.Id)) {
                    var name=mask.Name;int suffix=2;
                    while(masks.Any(m=>string.Equals(m.Name.Trim(),name.Trim(),StringComparison.OrdinalIgnoreCase)))name=mask.Name[..Math.Min(30,mask.Name.Length)]+" "+suffix++;
                    masks.Add(mask with {Name=name});
                }
                links[kind]=mask.Id;
            }
        }
        // Only referenced masks are needed when combining two effect layers.
        // The full editable library is retained in its own stored profile.
        if(masks.Count>16)masks=masks.Where(m=>links.ContainsValue(m.Id)).ToList();
        if(masks.Count>16)throw new ArgumentException("合并后的效果引用超过 16 个遮罩，请减少当前素材使用的遮罩。");
        if(masks.Count>0)collection=collection with {Added=collection.Added|EffectKind.MaskLibrary};
        to["Collection"]=JsonSerializer.SerializeToNode(collection);
        to["Masks"]=JsonSerializer.SerializeToNode(masks);to["EffectMasks"]=JsonSerializer.SerializeToNode(links);
        return to.Deserialize<VideoEffects>()!.Validate();
    }
    public static VideoEffects PrivateCopy(VideoEffects source,VideoEffects target,EffectKind kinds)
    {
        var keep=target.EffectMasks.Where(p=>(p.Key&kinds)==0).ToDictionary(p=>p.Key,p=>p.Value);
        target=target with {EffectMasks=keep,Masks=target.Masks.Where(m=>keep.ContainsValue(m.Id)).ToArray()};
        var ids=source.EffectMasks.Where(p=>(p.Key&kinds)!=0).Select(p=>p.Value).Distinct().ToDictionary(id=>id,_=>Guid.NewGuid().ToString("N"));
        var masks=source.Masks.Select(m=>ids.TryGetValue(m.Id,out var id)?m with {Id=id}:m).ToArray();
        var links=source.EffectMasks.ToDictionary(p=>p.Key,p=>ids.GetValueOrDefault(p.Value,p.Value));
        return Copy(source with {Masks=masks,EffectMasks=links},target,kinds,true);
    }
}
