using Microsoft.UI.Xaml.Media.Imaging;

namespace LumaWall.ControlCenter;

// Accessed only by the XAML thread. Images are decoded on demand by realized
// rows, and a bounded LRU retains recently used covers across list rebuilds.
internal static class PosterImageCache
{
    private sealed record Entry(BitmapImage Image,long Cost,LinkedListNode<(string,int)> Node);
    private static readonly Dictionary<(string,int),Entry> entries=new();
    private static readonly LinkedList<(string,int)> recent=new();
    private static long bytes;
    private const long Budget=48L*1024*1024;
    internal static int Count=>entries.Count;
    internal static long EstimatedBytes=>bytes;
    internal static void TrimIdle()=>Trim(8L*1024*1024,16);
    private static void Trim(long budget,int count)
    {
        while((bytes>budget||entries.Count>count)&&recent.First is { } oldest)Remove(oldest.Value);
    }
    internal static int SizeBucket(double pixels)
    {
        foreach(int bucket in new[]{128,192,256,384,512,768,1024,1536,2048})
            if(pixels<=bucket)return bucket;
        return 2048;
    }
    internal static BitmapImage? Load(string? path,int width=640,double aspect=1)
    {
        if(string.IsNullOrEmpty(path))return null;
        string full;
        try {full=Path.GetFullPath(path);}
        catch(Exception error) when(error is ArgumentException or NotSupportedException or PathTooLongException) {return null;}
        var key=(full,width);
        if(entries.TryGetValue(key,out var cached)) {
            recent.Remove(cached.Node);recent.AddLast(cached.Node);return cached.Image;
        }
        // UriSource uses WinUI's asynchronous image loader. A synchronous stat
        // here would stall scrolling on slow disks even on a cache hit.
        // Poster writers and Steam rescans explicitly invalidate changed paths.
        var image=new BitmapImage {DecodePixelWidth=width,DecodePixelType=DecodePixelType.Physical,
            CreateOptions=BitmapCreateOptions.IgnoreImageCache,UriSource=new Uri(full)};
        // Portrait covers can cost much more than width squared.
        long cost=Math.Max(1,(long)(width*(double)width*4/Math.Max(.05,aspect)));
        entries[key]=new(image,cost,recent.AddLast(key));bytes+=cost;
        Trim(Budget,96);
        return image;
    }
    internal static void Invalidate(string path)
    {
        string full=Path.GetFullPath(path);
        foreach(var key in entries.Keys.Where(k=>string.Equals(k.Item1,full,StringComparison.OrdinalIgnoreCase)).ToArray())Remove(key);
    }
    private static void Remove((string,int) key)
    {
        if(entries.Remove(key,out var entry)) {bytes-=entry.Cost;recent.Remove(entry.Node);}
    }
}
