using LumaWall.Contracts;

namespace LumaWall.ControlCenter;

// Drag previews are local to one captured editor target. The caller decides
// whether a committed collection is still current and when to save it.
internal sealed class EffectCardReorderSession
{
    private readonly EffectKind[] initialOrder,initialVisible;
    private EffectKind[] previewOrder;

    public EffectCollection InitialCollection {get;}
    public EffectCollection CurrentCollection {get;private set;}
    public string TargetKey {get;}
    public EffectKind Source {get;}
    public IReadOnlyList<EffectKind> PreviewOrder {get;private set;}
    public bool HasChanges {get;private set;}
    public bool IsActive {get;private set;}=true;

    public EffectCardReorderSession(EffectCollection initial,string targetKey,EffectKind source)
    {
        ArgumentNullException.ThrowIfNull(initial);
        if(string.IsNullOrEmpty(targetKey))throw new ArgumentException("Missing effect editor target.",nameof(targetKey));
        initial.Validate();
        if(!EffectOrder.Supports(source)||(initial.Added&source)==0)
            throw new ArgumentException("Only a visible visual effect can be reordered.",nameof(source));
        InitialCollection=CurrentCollection=initial;TargetKey=targetKey;Source=source;
        initialOrder=initial.OrderedKinds();
        initialVisible=initialOrder.Where(kind=>(initial.Added&kind)!=0).ToArray();
        previewOrder=initialVisible.ToArray();PreviewOrder=Array.AsReadOnly(previewOrder);
    }

    // index is the source's final position among visible cards, not an insertion
    // boundary in the full 19-stage sequence. Repeated hover is therefore stable.
    public bool MoveToIndex(int index)
    {
        if(!IsActive)return false;
        index=Math.Clamp(index,0,previewOrder.Length-1);
        int previous=Array.IndexOf(previewOrder,Source);
        if(previous==index)return false;
        var next=previewOrder.ToList();next.RemoveAt(previous);next.Insert(index,Source);
        previewOrder=next.ToArray();PreviewOrder=Array.AsReadOnly(previewOrder);
        HasChanges=!previewOrder.SequenceEqual(initialVisible);
        if(!HasChanges)CurrentCollection=InitialCollection;
        else {
            // Fill only the original visible slots. Hidden stages keep their
            // positions and relative order; all non-order settings stay intact.
            int visible=0;
            var all=initialOrder.Select(kind=>(InitialCollection.Added&kind)!=0?previewOrder[visible++]:kind).ToArray();
            CurrentCollection=InitialCollection with {Layers=all.Select((kind,position)=>(kind,position))
                .ToDictionary(pair=>pair.kind,pair=>pair.position*10)};
        }
        return true;
    }

    public EffectCollection Commit()
    {
        IsActive=false;return CurrentCollection;
    }
    public EffectCollection Cancel()
    {
        if(IsActive) {
            IsActive=false;HasChanges=false;CurrentCollection=InitialCollection;
            previewOrder=initialVisible.ToArray();PreviewOrder=Array.AsReadOnly(previewOrder);
        }
        return CurrentCollection;
    }

    // Coordinates are local to the visual-card panel. Split each gap at its
    // midpoint, so either half belongs to its nearest row/column. Empty cells
    // in the last row resolve to the last card rather than a nonexistent slot.
    public static int TargetIndex(double x,double y,double width,int columns,int count,double cardHeight=112,double gap=16)
    {
        if(count<=0)return -1;
        columns=Math.Max(1,columns);
        if(!double.IsFinite(width)||width<=0)return 0; // Panel has not been measured yet.
        if(!double.IsFinite(cardHeight)||cardHeight<=0)cardHeight=112;
        if(!double.IsFinite(gap)||gap<0)gap=0;
        if(double.IsNaN(x))x=0;if(double.IsNaN(y))y=0;
        double columnPitch=(width+gap)/columns,rowPitch=cardHeight+gap;
        int column=(int)Math.Clamp(Math.Floor((x+gap/2)/columnPitch),0,columns-1);
        int row=(int)Math.Clamp(Math.Floor((y+gap/2)/rowPitch),0,(count-1)/columns);
        return Math.Min(row*columns+column,count-1);
    }
}
