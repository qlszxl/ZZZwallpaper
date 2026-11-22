namespace LumaWall.Contracts;

[Flags]
public enum PauseReason { None = 0, User = 1, SessionLocked = 2, DisplayOff = 4, Suspend = 8, Fullscreen = 16, HighResourceLoad = 32 }
public enum DecoderMode { Auto, Hardware, Software }
public enum FillMode { Cover, Contain }
public sealed record MediaAsset(string Id, string Path, string Name, long Bytes, double Duration, int Width, int Height, string Codec, string Color, string Decoder)
{
    public bool IsImage=>MediaFormats.IsImage(Path);
    public string Kind=>IsImage?"image":"video";
}
public sealed record ScreenBinding(string ScreenId, string AssetId, FillMode Fill, bool UserPaused, long Revision)
{
    public VideoEffects Effects {get;init;}=VideoEffects.Original;
}
public sealed record ProbeResult(bool Ok, string Error, double Duration, int Width, int Height, string Codec, string Color, string Decoder)
{
    public System.Text.Json.JsonElement? Metadata {get;init;}
    public bool? HasAudio {get;init;}
    public double PosterSeconds {get;init;}
    public double SourceFps {get;init;}
}

// Immutable snapshot: no Explorer calls, disk access, or locks in the input callback.
public sealed record HitSnapshot(long CapturedAt, int X, int Y, bool IsBlank, string ScreenId, long Epoch);
public sealed record MenuRequest(int X, int Y, string ScreenId);
public readonly record struct DesktopRect(int Left,int Top,int Right,int Bottom)
{
    public bool Contains(int x,int y) => x>=Left&&x<Right&&y>=Top&&y<Bottom;
}
public sealed record DesktopScreen(string Id,DesktopRect Bounds);
public sealed record DesktopSceneSnapshot(long CapturedAt,long Epoch,DesktopRect Desktop,DesktopScreen[] Screens,DesktopRect[] Icons,DesktopRect[] Occlusions)
{
    // Layered windows can have transparent pixels inside their bounding rectangle.
    // Only an actual physical-point HWND hit can resolve those pixels; a geometry-
    // only caller stays conservative until that result is available.
    public DesktopRect[] PerPixelOcclusions {get;init;}=[];
    public HitSnapshot Hit(int x,int y,bool? desktopAtPoint=null)
    {
        var screen=Array.Find(Screens,s=>s.Bounds.Contains(x,y));
        bool blank=screen!=null&&Desktop.Contains(x,y)&&!Array.Exists(Icons,r=>r.Contains(x,y))&&!Array.Exists(Occlusions,r=>r.Contains(x,y))
            &&(desktopAtPoint??!Array.Exists(PerPixelOcclusions,r=>r.Contains(x,y)));
        return new(CapturedAt,x,y,blank,screen?.Id??"",Epoch);
    }
}
public readonly record struct InputDecision(bool Suppress, MenuRequest? Menu);

public static class DesktopEventPolicy
{
    public static bool ChangesGeometry(uint evt,int objectId,int childId,bool desktop,bool topLevel)
    {
        if(evt is 3 or 0x20)return true; // Foreground Z-order or input-desktop switch.
        // Focus, selection and name notifications do not move icons or windows.
        // Invalidating on every list-view notification starves the geometry cache.
        if(evt is not (0x8000 or 0x8001 or 0x8002 or 0x8003 or 0x8004 or 0x800B))return false;
        return desktop||(topLevel&&objectId==0&&childId==0);
    }
}

public sealed class RightButtonSequence
{
    private bool captured;
    private int startX, startY;
    private string screen = "";
    private bool dragged;
    public bool IsCaptured => captured;

    public InputDecision Down(int x, int y, HitSnapshot? snapshot, long now, long epoch, bool ready)
    {
        if (captured) return new(true, null);
        // A fresh, point-specific snapshot avoids assuming an arbitrary foreground window is desktop.
        if (!ready || snapshot is null || !snapshot.IsBlank || snapshot.Epoch != epoch || now - snapshot.CapturedAt is < 0 or > 120
            || Math.Abs((long)x - snapshot.X) > 2 || Math.Abs((long)y - snapshot.Y) > 2) return new(false, null);
        captured = true; dragged = false; startX = x; startY = y; screen = snapshot.ScreenId;
        return new(true, null);
    }

    public void Move(int x, int y, int dragX, int dragY)
    {
        if (captured && (Math.Abs((long)x - startX) > dragX || Math.Abs((long)y - startY) > dragY)) dragged = true;
    }

    public InputDecision Up(int x, int y, bool ready)
    {
        if (!captured) return new(false, null);
        captured = false;
        return new(true, !dragged && ready ? new MenuRequest(x, y, screen) : null);
    }
}

public sealed class PauseState
{
    public PauseReason Reasons { get; private set; }
    public bool IsPaused => Reasons != PauseReason.None;
    public void Set(PauseReason reason, bool active) => Reasons = active ? Reasons | reason : Reasons & ~reason;
}

public sealed class RevisionGate
{
    private long revision;
    public long Next() => Interlocked.Increment(ref revision);
    public bool IsCurrent(long candidate) => candidate == Interlocked.Read(ref revision);
}
