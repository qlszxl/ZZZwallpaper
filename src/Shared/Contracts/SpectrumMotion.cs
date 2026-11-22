namespace LumaWall.Contracts;

/// <summary>Audio attack/release with a finite, exact end to a silent release.</summary>
public static class SpectrumMotion
{
    /// <summary>A per-bin response with headroom, before the position's height limit.</summary>
    public static double Target(double amplitude,double sensitivity)
    {
        const double noiseFloor=.0001,knee=.6,headroom=1-knee;
        if(!double.IsFinite(amplitude)||!double.IsFinite(sensitivity)||amplitude<=noiseFloor||sensitivity<=0)return 0;
        // Match the capture contract and option bounds, not a 0 dB visual ceiling.
        // Root amplitude keeps quieter bins visible without the old 70 dB mapping
        // squeezing almost all loud input into the same few pixels at the top.
        double level=Math.Sqrt((Math.Min(amplitude,8)-noiseFloor)*Math.Min(sensitivity,4));
        if(level<=knee)return level;
        // Continuous value and slope at the knee. Every louder in-range input still
        // produces a larger output, including gain > 1; it never clips to 100%.
        return 1-headroom*headroom/(headroom+level-knee);
    }
    public static double Step(double current,double target,double seconds,double releaseSeconds)
    {
        double duration=target>current?.035:releaseSeconds;
        double next=current+(target-current)*(1-Math.Exp(-seconds/duration));
        // Exponential decay alone never reaches zero. Snap only a silent release,
        // well below one pixel even on the largest supported spectrum canvas.
        return target==0&&next<.00001?0:next;
    }
    public static bool HasGeometry(ReadOnlySpan<double> values,double minimumHeight)
    {
        if(values.Length==0)return false;
        if(minimumHeight>0)return true;
        foreach(double value in values)if(value>0)return true;
        return false;
    }
}
