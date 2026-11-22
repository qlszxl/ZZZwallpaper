namespace LumaWall.Contracts;

public enum SearchEngine { Bing, Baidu, Google, DuckDuckGo, Custom }

public sealed record SearchWidgetOptions
{
    public SearchEngine Engine {get;init;}=SearchEngine.Bing;
    public string CustomUrl {get;init;}="";
    [System.Text.Json.Serialization.JsonIgnore]
    public string Template=>Engine switch {
        SearchEngine.Baidu=>"https://www.baidu.com/s?wd={query}",SearchEngine.Google=>"https://www.google.com/search?q={query}",
        SearchEngine.DuckDuckGo=>"https://duckduckgo.com/?q={query}",SearchEngine.Custom=>CustomUrl,_=>"https://www.bing.com/search?q={query}"
    };
    public static string Label(SearchEngine engine)=>engine switch {SearchEngine.Bing=>"必应",SearchEngine.Baidu=>"百度",SearchEngine.Google=>"Google",SearchEngine.DuckDuckGo=>"DuckDuckGo",_=>"自定义"};
    public int Width {get;init;}=360;
    public int Height {get;init;}=44;
    public double Radius {get;init;}=22;

    public double RadiusFor(double width,double height)=>Math.Min(Radius,Math.Max(0,Math.Min(width,height)/2));
    public void Validate()
    {
        if(!Enum.IsDefined(Engine)||CustomUrl==null||CustomUrl.Length>4096)throw new ArgumentException("搜索引擎设置无效");
        if(Engine==SearchEngine.Custom||CustomUrl.Length>0) {
            if(!CustomUrl.Contains("{query}",StringComparison.Ordinal))throw new ArgumentException("搜索地址须包含 {query}，用于替换搜索词");
            var uri=NavigationLinks.Website(CustomUrl.Replace("{query}","ZZZwallpaperQuery",StringComparison.Ordinal));
            if(uri.Host.Contains("ZZZwallpaperQuery",StringComparison.OrdinalIgnoreCase)||!uri.PathAndQuery.Contains("ZZZwallpaperQuery",StringComparison.Ordinal))throw new ArgumentException("{query} 须放在地址路径或查询参数中");
        }
        if(Width is <120 or >16384||Height is <44 or >16384||!double.IsFinite(Radius)||Radius is <0 or >8192)
            throw new ArgumentException("搜索框尺寸或圆角超出范围");
    }
}
