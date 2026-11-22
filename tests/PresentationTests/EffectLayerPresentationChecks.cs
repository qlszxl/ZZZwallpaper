using LumaWall.ControlCenter;
using LumaWall.Contracts;

internal static class EffectLayerPresentationChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var initial=new EffectCollection {
            Added=EffectKind.Color|EffectKind.Blur|EffectKind.Particles|EffectKind.AudioReactive|EffectKind.HideTaskbar|EffectKind.MaskLibrary,
            ColorEnabled=false,Color=.42,Blur=.7,Particles=.23,HideTaskbarEnabled=false
        };
        check(!initial.HasCustomOrder&&EffectLayerPresentation.Visible(initial).SequenceEqual(new[]{EffectKind.Blur,EffectKind.Color,EffectKind.Particles}),"Overview follows the processing order without writing a local ordering override");
        check(EffectLayerPresentation.Visible(initial).All(EffectOrder.Supports),"Audio, taskbar and mask library stay outside the visual layer area");
        var auxiliary=EffectLayerPresentation.AuxiliaryKinds(false);
        check(auxiliary.SequenceEqual(new[]{EffectKind.AudioReactive,EffectKind.HideTaskbar,EffectKind.MaskLibrary}),"Display add choices put audio, taskbar and mask library in the auxiliary group");
        check(EffectLayerPresentation.AuxiliaryKinds(true).SequenceEqual(new[]{EffectKind.AudioReactive,EffectKind.MaskLibrary}),"Material add choices keep their auxiliary group without the display-only taskbar action");
        check(!EffectOrder.Default.Intersect(auxiliary).Any()&&EffectOrder.Default.Concat(auxiliary).ToHashSet().SetEquals(Enum.GetValues<EffectKind>().Where(kind=>EffectCollection.All.HasFlag(kind))),"The two add groups cover every effect exactly once");
        var earlier=EffectLayerPresentation.MoveAdjacent(initial,EffectKind.Color,-1);
        check(earlier.HasCustomOrder&&EffectLayerPresentation.Visible(earlier).SequenceEqual(new[]{EffectKind.Color,EffectKind.Blur,EffectKind.Particles}),"Move earlier skips absent effects and stores the new card order");
        var later=EffectLayerPresentation.MoveAdjacent(initial,EffectKind.Color,1);
        check(EffectLayerPresentation.Visible(later).SequenceEqual(new[]{EffectKind.Blur,EffectKind.Particles,EffectKind.Color}),"Move later places the source after the next visible effect despite gaps");
        var before=EffectLayerPresentation.Move(initial,EffectKind.Particles,EffectKind.Blur,false);
        check(EffectLayerPresentation.Visible(before).SequenceEqual(new[]{EffectKind.Particles,EffectKind.Blur,EffectKind.Color}),"Dropping on a card's upper half inserts before that effect");
        var after=EffectLayerPresentation.Move(initial,EffectKind.Blur,EffectKind.Particles,true);
        check(EffectLayerPresentation.Visible(after).SequenceEqual(new[]{EffectKind.Color,EffectKind.Particles,EffectKind.Blur}),"Dropping on a card's lower half inserts after it with the correct final index");
        check((after with {Layers=null})==initial,"Reordering preserves added membership, disabled states and every effect strength");
        check(ReferenceEquals(EffectLayerPresentation.MoveAdjacent(initial,EffectKind.Blur,-1),initial)&&ReferenceEquals(EffectLayerPresentation.MoveAdjacent(initial,EffectKind.Particles,1),initial),"Arrow keys at either end leave card order unchanged without writes");
        check(ReferenceEquals(EffectLayerPresentation.Move(initial,EffectKind.AudioReactive,EffectKind.Blur,false),initial)&&ReferenceEquals(EffectLayerPresentation.Move(initial,EffectKind.Glow,EffectKind.Blur,false),initial),"Auxiliary and absent effects cannot be dragged into the stack");
        check(ReferenceEquals(EffectLayerPresentation.Move(initial,EffectKind.Blur,EffectKind.Blur,false),initial),"Dropping onto the source card does nothing");
        check(EffectLayerPresentation.CanDrop("DISPLAY1|asset","DISPLAY1|asset",initial,EffectKind.Blur,EffectKind.Color),"An in-scope visual drag is accepted");
        check(!EffectLayerPresentation.CanDrop("DISPLAY1|asset","DISPLAY2|asset",initial,EffectKind.Blur,EffectKind.Color)
            &&!EffectLayerPresentation.CanDrop("DISPLAY1|asset","DISPLAY1|screen",initial,EffectKind.Blur,EffectKind.Color)
            &&!EffectLayerPresentation.CanDrop(null,null,initial,EffectKind.Blur,EffectKind.Color),"A drag captured before a display or target change cannot edit the new scope");
        var removed=initial with {Added=initial.Added&~EffectKind.Blur};
        check(!EffectLayerPresentation.CanDrop("one","one",removed,EffectKind.Blur,EffectKind.Color),"A removed source cannot be resurrected by a late drop");
        var saved=initial.WithLayer(EffectKind.Particles,EffectOrder.MinLayer);
        check(EffectLayerPresentation.Visible(saved)[0]==EffectKind.Particles,"Existing saved layer values still determine the card order after numeric controls are removed");

        var clock=new ManualClock();using var session=new EffectEditSession();
        var original=VideoEffects.Original with {Collection=initial,Blur=true,Brightness=.12};
        session.ChangeTarget("DISPLAY1|asset");session.AdoptOriginal(original);session.Initialize(clock.Create);
        int previews=0,saves=0;session.PreviewRequested+=()=>previews++;session.SaveRequested+=()=>saves++;
        var ordered=original with {Collection=earlier};session.Queue("DISPLAY1","asset",FillMode.Cover,ordered,false,10);
        clock.Advance(80);check(previews==1&&saves==0&&session.Pending?.Effects.Collection?.HasCustomOrder==true,"Layer gestures use the editor's existing 80 ms live preview boundary");
        clock.Advance(320);check(saves==1,"Layer edits use the same 400 ms deferred save boundary");
        var reordered=original with {Collection=later};
        session.QueuePendingSave();session.Queue("DISPLAY1","asset",FillMode.Cover,reordered,false,11);session.QueuePendingSave();
        var first=session.TakeQueuedSave()!;var second=session.TakeQueuedSave()!;
        check(first.Gesture==10&&first.Before==original&&first.Effects==ordered&&second.Gesture==11&&second.Effects==reordered,"Consecutive card movements retain independent ordered undo snapshots");
        check(first.Effects.Blur&&first.Effects.Brightness==original.Brightness&&first.Effects.Collection?.ColorEnabled==false,"Saving a layer change retains parameters and individual disabled effects");
    }
}
