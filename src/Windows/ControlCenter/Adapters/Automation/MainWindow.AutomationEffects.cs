using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LumaWall.Automation;
using LumaWall.Contracts;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private sealed record ScopedEffectUndo(WallpaperProfile Screen,AssetEffects Asset,string AfterRevision,long Bytes,long Order);
    private readonly Dictionary<string,ScopedEffectUndo> scopedEffectUndo=new();
    private long scopedEffectUndoSequence;
    private string ScopedEffectRevision(string screen,string? asset)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
        screen,asset,baseline=modules.Effects.ScreenProfile(screen),specific=asset==null?null:modules.Effects.AssetProfile(asset)
    }))));
    private async Task<OperationReply> AutomationEffects(OperationRequest request,CancellationToken token)
    {
        var args=request.Arguments;var screen=OperationScreen(Arg(args,"screenId"));
        string? asset=args.TryGetProperty("assetId",out var assetArg)?assetArg.GetString():null;
        if(asset!=null&&!modules.Library.Assets().Any(a=>a.Id==asset))throw new OperationFault("resource_not_found","素材不存在，请读取 media_list。","arguments.assetId");
        string scope=request.Name=="effect_copy_screen_to_asset"?"asset":args.TryGetProperty("scope",out var scopeArg)?scopeArg.GetString()!:"screen";
        if((scope is "asset" or "effective"||request.Name=="effect_set_scope")&&asset==null)throw new OperationFault("invalid_argument","素材效果必须明确指定 assetId。",null);
        if(request.Name=="effect_get")FlushEffectChanges();else EndEffectEditing();
        token.ThrowIfCancellationRequested();
        var baseline=modules.Effects.ScreenProfile(screen.DeviceName)??new WallpaperProfile(FillMode.Cover,VideoEffects.Original);
        var specific=asset==null?new AssetEffects():modules.Effects.AssetProfile(asset);
        string revision=ScopedEffectRevision(screen.DeviceName,asset);
        if(request.Name=="effect_get") {
            var resolved=specific.Resolve(baseline.Effects);
            var effects=scope=="screen"?baseline.Effects:scope=="asset"?specific.OwnEffects():resolved;
            return OperationReply.Success(new {
                screenId=screen.DeviceName,assetId=asset,scope,revision,fill=baseline.Fill.ToString(),
                parameters=EffectReadModel.Parameters(effects),

                enabled=EffectPatch.Groups.Keys.ToDictionary(name=>name,name=>EffectPatch.Enabled(effects,name)),
                overrides=EffectPatch.Groups.Keys.ToDictionary(name=>name,name=>(specific.Overrides&Enum.Parse<EffectKind>(name))!=0),
                audioParameters=AudioParameters.Selectable.Select(p=>new {p.Id,p.Key,p.EffectName,p.Name,p.Minimum,Maximum=p.Key=="RgbOffset"?Math.Min(p.Maximum,screen.Bounds.Width*.1/(screen.Bounds.Height/1080d)):p.Maximum,DisplayScale=p.Key=="RgbOffset"?screen.Bounds.Height/1080d:p.DisplayScale,p.Unit,p.Phase,p.Trigger}),
                audioRules=effects.AudioRules,ownAudioRules=scope=="screen"?baseline.Effects.AudioRules:specific.Effects.AudioRules,
                collection=effects.GetCollection(),editableGroups=EffectPatch.Groups,parameterSchema=EffectPatch.ParameterSchema,
                particleAppearances=ParticleArtwork.All.Where(p=>p.File==null||ExpansionCatalog.ProvidesEffect(EffectKind.Particles,"particles.artwork")).Select(p=>new {id=p.Id,name=p.Name,builtInImage=p.File!=null,randomCandidate=p.Id!=5,subLayer=p.Id!=5}),
                masks=effects.Masks.Select(m=>new {m.Id,m.Name,m.Shape,hasImage=m.Image.Length>0}),effectMasks=effects.EffectMasks,
                appliedScreens=wallpapers!.Running.Where(p=>p.Value.Asset.Id==asset).Select(p=>p.Key)
            });
        }
        if(Arg(args,"expectedRevision")!=revision)throw OperationFault.Conflict(Arg(args,"expectedRevision"),revision,"effect_get");
        bool materialChange=scope=="asset"||request.Name=="effect_set_scope";
        string key=screen.DeviceName+"|"+(materialChange?asset:"screen");
        var nextScreen=baseline;var nextAsset=specific;
        if(request.Name=="effect_undo") {
            if(!scopedEffectUndo.TryGetValue(key,out var undo)||undo.AfterRevision!=revision)throw new OperationFault("invalid_state","没有可撤销的修改，或配置已被后续操作更改。",null);
            nextScreen=undo.Screen;nextAsset=undo.Asset;
        } else if(request.Name=="effect_order") {
            var own=scope=="asset"?specific.Effects:baseline.Effects;
            var order=args.GetProperty("effects").EnumerateArray().Select(v=>Enum.Parse<EffectKind>(v.GetString()!)).ToArray();
            var visible=EffectOrder.Default.Where(kind=>(own.GetCollection().Added&kind)!=0).ToArray();
            if(order.Length!=visible.Length||!order.ToHashSet().SetEquals(visible))throw new OperationFault("invalid_argument","排序必须包含此范围全部已添加的视觉效果各一次",null);
            var collection=own.GetCollection() with {Layers=order.Select((kind,index)=>(kind,index)).ToDictionary(p=>p.kind,p=>p.index*10)};
            var edited=own with {Collection=collection};
            if(scope=="asset")nextAsset=specific with {Effects=edited};else nextScreen=baseline with {Effects=edited};
        } else if(request.Name=="effect_clear") {
            if(scope=="asset")nextAsset=new() {Overrides=AssetEffects.Allowed,Effects=VideoEffects.Original};
            else nextScreen=baseline with {Effects=VideoEffects.Original};
        } else if(request.Name is "audio_set_rule" or "audio_remove_rule") {
            var own=scope=="asset"?specific.Effects:baseline.Effects;
            AudioRule[] rules;
            if(request.Name=="audio_set_rule") {
                var rule=args.GetProperty("rule").Deserialize<AudioRule>()!.Validate();
                if(rule.Parameter=="RgbOffset"&&Math.Max(rule.Weak,rule.Strong)*screen.Bounds.Height/1080d>screen.Bounds.Width*.1)throw new OperationFault("invalid_argument","RGB 偏移超出当前显示器范围。",null);
                rules=AudioParameters.Upsert(own.AudioRules,rule);
            } else {string parameter=Arg(args,"parameter");var p=AudioParameters.Find(parameter);if(AudioParameters.TriggerEffect(p.Effect))parameter=p.Effect+".Trigger";rules=own.AudioRules.Where(r=>r.Parameter!=parameter).ToArray();}
            if(scope=="asset")nextAsset=specific with {Effects=own with {AudioRules=rules}};
            else nextScreen=baseline with {Effects=own with {AudioRules=rules}};
        } else if(request.Name=="effect_copy_screen_to_asset") {
            nextAsset=AssetEffects.FromScreen(baseline.Effects);
        } else {
            var kind=Enum.Parse<EffectKind>(Arg(args,"effect"));
            if(request.Name=="effect_set_scope") {
                nextAsset=scope=="asset"?specific.Overrides.HasFlag(kind)?specific:specific with {Overrides=specific.Overrides|kind,Effects=AssetEffects.PrivateCopy(specific.Resolve(baseline.Effects),specific.Effects,kind) with {AudioRules=specific.Effects.AudioRules}}:specific with {Overrides=specific.Overrides&~kind};
            } else {
                var source=scope=="asset"?specific.OwnEffects():baseline.Effects;
                var parameters=args.TryGetProperty("parameters",out var p)?p:ToolCatalog.Json(new {});
                var edited=EffectPatch.Apply(source,kind.ToString(),args.GetProperty("enabled").GetBoolean(),args.TryGetProperty("strength",out var strength)?strength.GetDouble():null,parameters,screen.Bounds.Width,screen.Bounds.Height);
                if(args.TryGetProperty("maskId",out var maskArg)) {
                    string maskId=maskArg.GetString()!;var links=new Dictionary<EffectKind,string>(edited.EffectMasks);
                    if(maskId=="none")links.Remove(kind);
                    else {
                        if(kind==EffectKind.AudioReactive||!edited.Masks.Any(m=>m.Id==maskId))throw new OperationFault("resource_not_found","遮罩不存在或此效果不支持遮罩，请读取相同范围的 effect_get。",null);
                        links[kind]=maskId;
                    }
                    edited=(edited with {EffectMasks=links}).Validate();
                }
                if(scope=="asset")nextAsset=specific with {Overrides=specific.Overrides|kind,Effects=specific.Overrides.HasFlag(kind)?AssetEffects.Copy(edited,specific.Effects,kind,true):AssetEffects.PrivateCopy(edited,specific.Effects,kind)};
                else nextScreen=baseline with {Effects=edited};
                if(scope=="asset")nextAsset=nextAsset with {Effects=nextAsset.Effects with {AudioRules=AudioParameters.ApplyDelta(specific.Effects.AudioRules,source.AudioRules,edited.AudioRules)}};
            }
        }
        token.ThrowIfCancellationRequested();
        modules.Effects.Commit(screen.DeviceName,asset,nextScreen,nextAsset);
        string after=ScopedEffectRevision(screen.DeviceName,asset);
        if(request.Name=="effect_undo")scopedEffectUndo.Remove(key);
        else {
            scopedEffectUndo.Remove(key);
            scopedEffectUndo[key]=new(baseline,specific,after,2L*JsonSerializer.Serialize(new {baseline,specific}).Length,++scopedEffectUndoSequence);
            // A valid screen + asset image set may exceed the historical soft
            // budget by itself. Evict older targets, never the just-created undo.
            while(scopedEffectUndo.Count>1&&(scopedEffectUndo.Count>32||scopedEffectUndo.Values.Sum(v=>(long)v.Bytes)>32L*1024*1024))
                scopedEffectUndo.Remove(scopedEffectUndo.MinBy(p=>p.Value.Order).Key);
        }
        string? sendError=null;
        try {wallpapers!.RefreshLiveEffects(materialChange?EffectChange.Asset(asset!):EffectChange.Screen(screen.DeviceName));}catch(IOException ex) {sendError=ex.Message;}
        if(effectTargetAssetId==asset||SelectedScreen?.DeviceName==screen.DeviceName)LoadEffectDraft(true);
        var active=wallpapers!.Running.Where(p=>materialChange?p.Value.Asset.Id==asset:p.Key==screen.DeviceName).Select(p=>p.Value.Player).ToArray();
        bool confirmed=sendError==null;
        foreach(var player in active)confirmed&=await player.WaitForConfiguration(token);
        return new(!confirmed,ToolCatalog.Json(new {screenId=screen.DeviceName,assetId=asset,scope,revision=after,saved=true,
            renderConfirmed=active.Length>0?(bool?)confirmed:null,applied=active.Length>0,
            message=sendError??(active.Length==0?"已保存，应用素材后生效":confirmed?"已应用并保存":"已保存，部分渲染确认未完成，请重新读取状态。") }));
    }
}
