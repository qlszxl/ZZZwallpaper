namespace LumaWall.Contracts;

/// <summary>Scale each frequency bin by its own Gaussian position weight, without mixing bins.</summary>
public static class SpectrumEnvelope
{
    // Weight screen positions, after selecting each position's input bin.
    // Reversing audio order must not reverse an asymmetric weight curve.
    public static void Shape(ReadOnlySpan<double> source,Span<double> destination,ReadOnlySpan<double> weights,bool reverse)
    {
        if(source.Length!=destination.Length||source.Length!=weights.Length)throw new ArgumentException("频谱长度不匹配");
        for(int i=0;i<source.Length;i++) {
            double amplitude=source[reverse?source.Length-1-i:i];
            destination[i]=(double.IsFinite(amplitude)?Math.Clamp(amplitude,0,1):0)*Math.Clamp(weights[i],0,1);
        }
    }
    // Offset and normalize the truncated Gaussian so the centre is 100% and
    // the two outermost bins reach exactly zero, without discarding any bins.
    public static double HeightLimit(int index,int count)
    {
        if(count<2)return 1;
        if(index==0||index==count-1)return 0;
        double x=(2d*index-(count-1))/(count-1),nearest=count%2==0?1d/(count-1):0;
        double gaussian=Math.Exp(Math.Log(.05)*Math.Clamp((x*x-nearest*nearest)/(1-nearest*nearest),0,1));
        return Math.Clamp((gaussian-.05)/.95,0,1);
    }
    public static void Shape(ReadOnlySpan<double> source,Span<double> destination,bool center)
    {
        if(destination.Length!=source.Length)throw new ArgumentException("频谱长度不匹配");
        for(int i=0;i<source.Length;i++) {
            double amplitude=double.IsFinite(source[i])?Math.Clamp(source[i],0,1):0;
            // Only this bin's input contributes to this bin's output. In particular,
            // a quiet bin stays quiet even when a different frequency is loud.
            destination[i]=amplitude*(center?HeightLimit(i,source.Length):1);
        }
    }
}
