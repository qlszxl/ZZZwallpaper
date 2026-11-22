namespace LumaWall.Contracts;

public static class MediaFormats
{
    public static readonly string[] Images=[".jpg",".jpeg",".png",".bmp",".webp",".tif",".tiff"];
    public static readonly string[] Videos=[".mp4",".mkv",".mov",".webm",".avi",".m4v",".wmv",".mpg",".mpeg",".ts",".mts",".m2ts",".flv",".ogv",".3gp",".3g2",".m2v",".vob"];
    public static bool IsImage(string path)=>Images.Contains(System.IO.Path.GetExtension(path),StringComparer.OrdinalIgnoreCase);
    public static bool Supported(string path)=>IsImage(path)||Videos.Contains(System.IO.Path.GetExtension(path),StringComparer.OrdinalIgnoreCase);
    public static string LocalImportPath(string path)
    {
        if(string.IsNullOrWhiteSpace(path)||!System.IO.Path.IsPathFullyQualified(path)||path.StartsWith("\\\\")||path.Contains('\0')||path.AsSpan(2).Contains(':'))
            throw new ArgumentException("请选择本机完整路径的视频或图片，不支持网址、网络路径或设备路径。");
        var full=System.IO.Path.GetFullPath(path);
        if(!Supported(full))throw new ArgumentException("不支持此素材格式，请选择视频或 JPG、PNG、BMP、WebP、TIFF 图片。");
        return full;
    }
}
