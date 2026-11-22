namespace LumaWall.Contracts;

public readonly record struct SpectrumSegmentSpan(double Start,double Length);

/// <summary>Whole illuminated cells, anchored to an edge or the mirror centre.</summary>
public static class SpectrumSegments
{
    private static double VisibleLength(double extent,double height,double gap)
    {
        double count=Math.Floor((extent+gap)/(height+gap));
        return count<=0?0:count*height+(count-1)*gap;
    }
    public static (SpectrumSegmentSpan First,SpectrumSegmentSpan Second) Layout(double length,double extent,double height,double gap,bool mirror,bool forward)
    {
        extent=Math.Clamp(extent,0,length);
        if(mirror) {
            double span=VisibleLength(Math.Max(0,(extent-gap)/2),height,gap);
            return span<=0?default:(new(length/2-gap/2-span,span),new(length/2+gap/2,span));
        }
        double visible=VisibleLength(extent,height,gap);
        return visible<=0?default:(new(forward?0:length-visible,visible),default);
    }
}
