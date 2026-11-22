namespace LumaWall.DesktopIntegration;

/// <summary>The volume belonging to the screen where the desktop menu was opened.</summary>
public sealed record DesktopMenuVolume(string ScreenId,int Volume,bool SoundEnabled)
{
    public int DisplayedVolume=>SoundEnabled?Volume:0;
}

// Mouse movement updates this draft only. Playback previews are rate limited;
// persistence receives one snapshot at the end of a gesture, including dismissal.
internal sealed class MenuVolumeSession
{
    internal const int PreviewIntervalMilliseconds=80;
    private DesktopMenuVolume committed,previewed;
    private long? lastPreviewAt;
    public DesktopMenuVolume Current {get;private set;}
    public bool PreviewPending=>Current!=previewed;

    public MenuVolumeSession(DesktopMenuVolume initial)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(initial.ScreenId);
        Current=initial with {Volume=Math.Clamp(initial.Volume,1,100)};
        committed=previewed=Current;
    }
    public bool SetVolume(int value)
    {
        value=Math.Clamp(value,0,100);
        var next=Current with {Volume=value>0?value:Current.Volume,SoundEnabled=value>0};
        if(next==Current)return false;
        Current=next;return true;
    }
    public void ToggleMute()=>Current=Current with {SoundEnabled=!Current.SoundEnabled};
    public DesktopMenuVolume? TakePreview(long now,bool force=false)
    {
        if(!PreviewPending||(!force&&lastPreviewAt is { } last&&now-last<PreviewIntervalMilliseconds))return null;
        previewed=Current;lastPreviewAt=now;return Current;
    }
    public DesktopMenuVolume? Commit()
    {
        if(Current==committed)return null;
        committed=Current;return Current;
    }
    public static int VolumeAt(int x,int left,int width)=>width<=0?0:
        (int)Math.Round(Math.Clamp((double)(x-left)/width,0,1)*100,MidpointRounding.AwayFromZero);
}
