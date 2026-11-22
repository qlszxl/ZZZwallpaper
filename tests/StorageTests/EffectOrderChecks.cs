using System.Text.Json;
using LumaWall.Application;
using LumaWall.Contracts;
using LumaWall.Contracts.Sharing;
using LumaWall.Storage;

internal static class EffectOrderChecks
{
    internal static void Run(string root,Action<bool,string> check)
    {
        string path=Path.Combine(root,"ordered-effect-stack");
        var initial=new VideoEffects {Glow=true,GlowStrength=.2,Brightness=.1,Collection=new() {Added=EffectKind.Color|EffectKind.Glow}};
        var screen=initial with {Collection=initial.GetCollection().WithLayer(EffectKind.Color,-10)};
        string reorderedWire;
        using(var store=new LibraryStore(path)) {
            store.Upsert(new("material",Path.Combine(path,"material.mp4"),"material",0,1,1920,1080,"h264","sdr","test"));
            store.SaveProfile("left","",new(FillMode.Cover,screen));store.HistoryEnabled=true;
            var service=new EffectsService(store);var history=new HistoryService(store);
            var before=service.EditingProfile("left","material");
            var moved=before with {Effects=before.Effects with {Collection=before.Effects.GetCollection().WithLayer(EffectKind.Glow,-20)}};
            var write=store.QueueEffectWrite(service.Prepare("left","material",moved,before.Effects));store.CompleteEffectWrite(write);
            var asset=store.AssetProfile("material");var resolved=store.EffectiveProfile("left","material").Effects;
            check(asset.Overrides==0&&asset.Effects.GetCollection().Layers is {Count:1}&&asset.Effects.GetCollection().Layers![EffectKind.Glow]==-20,"Background order-only material save stores one sparse layer without promoting any parameter scopes");
            check(resolved.GlowStrength==.2&&resolved.Brightness==.1&&resolved.GetCollection().GetLayer(EffectKind.Color)==-10&&resolved.GetCollection().OrderedKinds()[0]==EffectKind.Glow,"Material ordering composes with inherited screen parameters and layers");
            check(EffectOrder.Equivalent(store.ScreenProfile("left")!.Effects.GetCollection().Layers,screen.GetCollection().Layers),"Material reorder leaves display ordering untouched");
            history.Restore(false);
            check(store.AssetProfile("material").Effects.GetCollection().Layers==null&&store.EffectiveProfile("left","material").Effects.ToWire()==screen.ToWire(),"Undo of asynchronous material reorder restores display stack inheritance");
            history.Restore(true);
            check(store.EffectiveProfile("left","material").Effects.GetCollection().GetLayer(EffectKind.Glow)==-20,"Redo restores the sparse material order");

            store.HistoryEnabled=false;
            store.SaveBinding(new("left","material",FillMode.Cover,false,1) {Effects=store.EffectiveProfile("left","material").Effects},true);
            check(EffectOrder.Equivalent(store.ScreenProfile("left")!.Effects.GetCollection().Layers,screen.GetCollection().Layers),"Applying material ordering never bakes its independent layer override into the display profile");
            var updated=screen with {GlowStrength=.7,Collection=screen.GetCollection().WithLayer(EffectKind.Color,-30)};
            service.SaveScreenProfile("left",new(FillMode.Cover,updated));
            resolved=service.EffectiveProfile("left","material").Effects;
            check(resolved.GlowStrength==.7&&resolved.GetCollection().GetLayer(EffectKind.Glow)==-20&&resolved.GetCollection().GetLayer(EffectKind.Color)==-30,"Later screen edits flow through unchanged material parameters and unrelated stage layers");
            before=service.EditingProfile("left","material");
            service.SaveTargetEffects("left","material",before with {Effects=before.Effects with {Collection=before.Effects.GetCollection().ResetOrder()}},before.Effects);
            check(store.AssetProfile("material").Effects.GetCollection().Layers==null&&service.EffectiveProfile("left","material").Effects.ToWire()==updated.ToWire(),"Material reset removes local order and returns to the current display composition");
            service.SetEffectScope("left","material",EffectKind.Glow,true);
            check(store.AssetProfile("material").Effects.GetCollection().Layers==null,"Enabling a parameter override does not silently capture inherited stage layers");
            service.SetEffectScope("left","material",EffectKind.Glow,false);
            before=service.EditingProfile("left","material");
            service.SaveTargetEffects("left","material",before with {Effects=before.Effects with {Collection=before.Effects.GetCollection().WithLayer(EffectKind.Glow,-40)}},before.Effects);
            var savedLayers=EffectOrder.Clone(store.AssetProfile("material").Effects.GetCollection().Layers);
            service.SetEffectScope("left","material",EffectKind.Glow,true);service.SetEffectScope("left","material",EffectKind.Glow,false);
            check(EffectOrder.Equivalent(store.AssetProfile("material").Effects.GetCollection().Layers,savedLayers),"Toggling parameter inheritance preserves independent material layer adjustments");
            service.CopyScreenEffectsToAsset("left","material");
            check(EffectOrder.Equivalent(store.AssetProfile("material").Effects.GetCollection().Layers,updated.GetCollection().Layers),"Explicit copy-display-to-material persists complete stage order");

            var imports=new ConfigurationImportService(store);
            var shared=ShareCode.Decode(ShareCode.Encode(ShareProjection.Create(initial with {Collection=initial.GetCollection().Move(EffectKind.Glow,0)},new WidgetConfiguration(),new ShareSelection {Effects=EffectKind.Glow|EffectKind.Color})));
            imports.Apply(shared,"left");
            var imported=store.ScreenProfile("left")!.Effects.GetCollection();
            check(imported.OrderedKinds().First(kind=>(imported.Added&kind)!=0)==EffectKind.Glow,"Configuration import commits the shared visual stack order");
            imports.Restore();
            check(EffectOrder.Equivalent(store.ScreenProfile("left")!.Effects.GetCollection().Layers,updated.GetCollection().Layers),"Import restore retains the previous display stage layers");
            var backup=new ConfigurationImportService.Backup(1,new WidgetConfiguration(),"left",null,new(FillMode.Cover,initial with {Version=28}));
            store.SetSetting("sharing.undo.v1",JsonSerializer.Serialize(backup));
            imports.Restore();
            check(store.ScreenProfile("left")!.Effects.Version==VideoEffects.Original.Version&&store.ScreenProfile("left")!.Effects.ToWire()==initial.ToWire(),"A saved JSON 28 import backup migrates before validation and retains its parameters with default ordering");

            store.HistoryEnabled=true;store.BreakUndoGesture();
            var current=service.EditingProfile("left",null);
            var reordered=current.Effects with {Collection=current.Effects.GetCollection().Move(EffectKind.Glow,0)};
            reorderedWire=reordered.ToWire();
            write=store.QueueEffectWrite(service.Prepare("left",null,current with {Effects=reordered},current.Effects));store.CompleteEffectWrite(write);
            check(reorderedWire!=current.Effects.ToWire()&&store.ScreenProfile("left")!.Effects.ToWire()==reorderedWire&&store.ScreenProfile("left")!.Effects.GetCollection().OrderedKinds()[0]==EffectKind.Glow,"Background display reorder persists the requested rendering sequence");
            history.Restore(false);
            check(store.ScreenProfile("left")!.Effects.ToWire()==initial.ToWire(),"Undo display ordering restores the original parameters and default sequence");
            history.Restore(true);
        }
        using(var reopened=new LibraryStore(path))
            check(reopened.ScreenProfile("left")!.Effects.GetCollection().OrderedKinds()[0]==EffectKind.Glow&&reopened.ScreenProfile("left")!.Effects.ToWire()==reorderedWire,"Reopening the database retains reordered effects and their rendering sequence");
    }
}
