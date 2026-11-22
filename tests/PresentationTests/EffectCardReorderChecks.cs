using LumaWall.ControlCenter;
using LumaWall.Contracts;

internal static class EffectCardReorderChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var initial=new EffectCollection {Added=EffectKind.WaterWave|EffectKind.Blur|EffectKind.Color|EffectKind.Glow|EffectKind.Noise|EffectKind.Particles|EffectKind.AudioReactive|EffectKind.MaskLibrary,
            ColorEnabled=false,Blur=.35,Particles=.7,AudioReactive=.4};
        var original=initial.OrderedKinds();
        var drag=new EffectCardReorderSession(initial,"display-a|asset-a",EffectKind.WaterWave);
        check(drag.TargetKey=="display-a|asset-a"&&drag.Source==EffectKind.WaterWave&&drag.IsActive&&!drag.HasChanges&&ReferenceEquals(drag.CurrentCollection,initial),"Reorder session captures its target and starts without changing stored state");
        check(drag.PreviewOrder.SequenceEqual(new[]{EffectKind.WaterWave,EffectKind.Blur,EffectKind.Color,EffectKind.Glow,EffectKind.Noise,EffectKind.Particles}),"Visible drag order excludes auxiliary and hidden effects");
        int slot=EffectCardReorderSession.TargetIndex(184,184,368,3,6);
        check(slot==4&&drag.MoveToIndex(slot)&&drag.PreviewOrder.SequenceEqual(new[]{EffectKind.Blur,EffectKind.Color,EffectKind.Glow,EffectKind.Noise,EffectKind.WaterWave,EffectKind.Particles}),"Dragging across a row inserts at the final visible slot and makes intervening cards yield");
        var preview=drag.CurrentCollection;
        check(!drag.MoveToIndex(slot)&&ReferenceEquals(preview,drag.CurrentCollection),"Repeated hover over the same slot does not oscillate or rebuild the preview");
        var hidden=original.Where(kind=>(initial.Added&kind)==0).ToArray();
        var after=preview.OrderedKinds();
        check(hidden.All(kind=>Array.IndexOf(original,kind)==Array.IndexOf(after,kind))&&after.Length==EffectOrder.Default.Count&&after.Distinct().Count()==EffectOrder.Default.Count,"Preview preserves hidden stage positions and retains every visual kind exactly once");
        check((preview with {Layers=initial.Layers})==initial&&initial.Layers==null,"Reordering changes only ordering metadata and leaves source membership, enable flags and strengths untouched");
        check(drag.MoveToIndex(0)&&!drag.HasChanges&&ReferenceEquals(drag.CurrentCollection,initial),"Dragging back to the initial slot removes the change and restores the original collection exactly");
        check(ReferenceEquals(drag.Commit(),initial)&&!drag.IsActive&&!drag.MoveToIndex(3),"A returned-to-origin commit is a no-op and the closed session ignores late hover");

        var cancel=new EffectCardReorderSession(initial,"display-a|screen",EffectKind.Color);
        cancel.MoveToIndex(5);
        check(ReferenceEquals(cancel.Cancel(),initial)&&!cancel.HasChanges&&!cancel.IsActive&&cancel.PreviewOrder.SequenceEqual(initial.OrderedKinds().Where(kind=>(initial.Added&kind)!=0)),"Cancel discards all drag previews and restores the captured visible order");
        check(ReferenceEquals(cancel.Commit(),initial),"A late release after cancellation cannot commit a discarded order");
        var commit=new EffectCardReorderSession(initial,"display-b|screen",EffectKind.Glow);
        commit.MoveToIndex(int.MinValue);
        check(commit.PreviewOrder[0]==EffectKind.Glow,"Negative drag destinations clamp to the first visible card");
        commit.MoveToIndex(int.MaxValue);
        check(commit.PreviewOrder[^1]==EffectKind.Glow,"Oversized drag destinations clamp to the last visible card");
        var committed=commit.Commit();
        check(commit.HasChanges&&!commit.IsActive&&ReferenceEquals(commit.Cancel(),committed)&&ReferenceEquals(commit.Commit(),committed),"A completed drag returns one stable result without saving or later cancellation changing it");

        var sparse=initial with {Layers=new() {[EffectKind.Glow]=-50,[EffectKind.Particles]=999}};
        var sparseDrag=new EffectCardReorderSession(sparse,"display|asset",EffectKind.Glow);
        sparseDrag.MoveToIndex(4);sparseDrag.MoveToIndex(0);
        check(ReferenceEquals(sparseDrag.Commit(),sparse)&&sparse.Layers.Count==2&&sparse.Layers[EffectKind.Particles]==999,"A roundtrip drag preserves an existing sparse override rather than replacing it with normalized ranks");

        // Three 112px cards with 16px gaps, a second full row, and a final two-card row.
        int At(double x,double y,int count=8)=>EffectCardReorderSession.TargetIndex(x,y,368,3,count);
        check(At(56,56)==0&&At(184,56)==1&&At(312,56)==2&&At(56,184)==3&&At(184,312)==7,"Panel coordinates map card centers in row-major order across all rows");
        check(At(119,56)==0&&At(120,56)==1&&At(56,119)==0&&At(56,120)==3,"Column and row gaps switch exactly at their midpoint");
        check(At(119.9,119.9)==0&&At(120.1,120.1)==4,"Diagonal movement through a gap junction resolves deterministically");
        check(At(312,312)==7&&At(10000,312)==7&&At(312,10000)==7,"Empty cells in the final row and panel overflow resolve to a stable final card");
        check(At(-100,-100)==0&&At(double.NegativeInfinity,double.NegativeInfinity)==0&&At(double.PositiveInfinity,double.PositiveInfinity)==7,"Out-of-bounds pointer coordinates clamp to valid grid slots");
        check(At(312,56,1)==0&&At(312,312,0)==-1&&EffectCardReorderSession.TargetIndex(100,100,0,3,8)==0,"Single-card, empty and unmeasured panels have safe deterministic targets");
        check(EffectCardReorderSession.TargetIndex(20,84,240,1,4,80,8)==1&&EffectCardReorderSession.TargetIndex(20,83.9,240,1,4,80,8)==0,"Target calculation respects supplied card height and spacing");
    }
}
