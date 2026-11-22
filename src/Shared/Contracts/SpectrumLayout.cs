namespace LumaWall.Contracts;

public static class SpectrumLayout
{
    // Keep the footprint stable when individual layers are hidden.
    public static (int Width,int Height) Size(SpectrumOptions spectrum)
        => (Math.Max(spectrum.Width,spectrum.SubSpectra.Select(s=>s.Width).DefaultIfEmpty(0).Max()),
            Math.Max(spectrum.Height,spectrum.SubSpectra.Select(s=>s.Height).DefaultIfEmpty(0).Max()));
}
