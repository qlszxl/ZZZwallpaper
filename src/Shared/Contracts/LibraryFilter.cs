namespace LumaWall.Contracts;

public sealed record LibraryFilter(string Query="",string Kind="all",string Orientation="all",string Resolution="all",string Sort="recent")
{
    public static readonly string[] Kinds=["all","video","image"];
    public static readonly string[] Orientations=["all","landscape","portrait","square","unknown"];
    public static readonly string[] Resolutions=["all","below720","720p","1080p","1440p","2160p","unknown"];
    public static readonly string[] Sorts=["recent","name","size","duration"];
    public LibraryFilter Validate()
    {
        if(Query.Length>200||!Kinds.Contains(Kind)||!Orientations.Contains(Orientation)||!Resolutions.Contains(Resolution)||!Sorts.Contains(Sort))
            throw new ArgumentException("无效的素材筛选条件");
        return this with {Query=Query.Trim()};
    }
    public bool IsFiltered=>Query.Length!=0||Kind!="all"||Orientation!="all"||Resolution!="all";
    public bool Matches(MediaAsset asset)
    {
        if(Query.Length!=0&&!asset.Name.Contains(Query,StringComparison.OrdinalIgnoreCase)&&!System.IO.Path.GetFileName(asset.Path).Contains(Query,StringComparison.OrdinalIgnoreCase))return false;
        if(Kind!="all"&&asset.Kind!=Kind)return false;
        string orientation=asset.Width<=0||asset.Height<=0?"unknown":asset.Width==asset.Height?"square":asset.Width>asset.Height?"landscape":"portrait";
        if(Orientation!="all"&&Orientation!=orientation)return false;
        int edge=Math.Min(asset.Width,asset.Height);
        string resolution=edge<=0?"unknown":edge<720?"below720":edge<1080?"720p":edge<1440?"1080p":edge<2160?"1440p":"2160p";
        return Resolution=="all"||Resolution==resolution;
    }
    // Input order is most recently imported first; OrderBy is stable for ties.
    public List<MediaAsset> Apply(IEnumerable<MediaAsset> assets,CancellationToken token=default)
    {
        var matches=assets.Where(asset=>{token.ThrowIfCancellationRequested();return Matches(asset);});
        return (Sort switch {
            "name"=>matches.OrderBy(a=>a.Name,StringComparer.OrdinalIgnoreCase),
            "size"=>matches.OrderByDescending(a=>a.Bytes),
            "duration"=>matches.OrderByDescending(a=>a.Duration),
            _=>matches
        }).ToList();
    }
}

public sealed record LibraryQueryResult(List<MediaAsset> Items,int Total);
