using LumaWall.Contracts;

namespace LumaWall.ControlCenter;

// The overview shows only added visual effects, while the persisted ordering
// also retains absent effects so removing a card never discards its layer.
internal static class EffectLayerPresentation
{
    public static EffectKind[] AuxiliaryKinds(bool material)=>Enum.GetValues<EffectKind>()
        .Where(kind=>(kind&EffectCollection.All)!=0&&!EffectOrder.Supports(kind)&&(!material||kind!=EffectKind.HideTaskbar)).ToArray();

    public static EffectKind[] Visible(EffectCollection collection)=>collection.OrderedKinds()
        .Where(kind=>(collection.Added&kind)!=0).ToArray();

    public static EffectCollection Move(EffectCollection collection,EffectKind source,EffectKind target,bool after)
    {
        if(source==target||!EffectOrder.Supports(source)||!EffectOrder.Supports(target)
            ||(collection.Added&source)==0||(collection.Added&target)==0)return collection;
        var remaining=collection.OrderedKinds().Where(kind=>kind!=source).ToArray();
        int index=Array.IndexOf(remaining,target)+(after?1:0);
        return collection.Move(source,index);
    }
    public static EffectCollection MoveAdjacent(EffectCollection collection,EffectKind source,int direction)
    {
        var visible=Visible(collection);int index=Array.IndexOf(visible,source),next=index+Math.Sign(direction);
        if(index<0||next<0||next>=visible.Length||next==index)return collection;
        return Move(collection,source,visible[next],direction>0);
    }
    public static bool CanDrop(string? capturedTarget,string? currentTarget,EffectCollection collection,EffectKind source,EffectKind target)=>
        capturedTarget!=null&&capturedTarget==currentTarget&&source!=target&&EffectOrder.Supports(source)&&EffectOrder.Supports(target)
        &&(collection.Added&source)!=0&&(collection.Added&target)!=0;
}
