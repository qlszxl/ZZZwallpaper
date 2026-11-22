namespace LumaWall.ControlCenter;

// Stage durations are elapsed time, not a breakdown of process CPU utilization.
// GPU values come from asynchronous timestamp samples and exclude hardware decode.
public sealed record PerformanceFrame(
    DateTimeOffset SampledAt,double PresentedFps,long Frames,
    double? DecodeMs,double? AudioMs,double? RenderCpuMs,double? PresentMs,
    double? GpuTotalMs,double? GpuBaseMs,double? GpuGlowMs,double? GpuBlurMs,
    bool GpuValid,int GpuSamples,int Passes,int Width,int Height,int VideoWidth,int VideoHeight,
    string Decoder,bool Paused);
