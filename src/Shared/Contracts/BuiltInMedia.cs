namespace LumaWall.Contracts;

// Only older installations have this asset-only generation. New builds never create it.
public sealed class LegacyMaterials : IExpansionPackage
{
    public static LegacyMaterials Current {get;}=new();
    private const string Id="warehouse-particles-v1";
    private static string Root=>AppContext.GetData("LumaWall.ExpansionPackRoot") as string??Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LumaWall","ExpansionPacks");
    private static IEnumerable<string> Directories(){
        if(!Directory.Exists(Root))yield break;
        foreach(var dir in Directory.EnumerateDirectories(Root,Id+"*")){
            var name=Path.GetFileName(dir);
            if(name==Id||name==Id+".removed"||(name.StartsWith(Id+".",StringComparison.Ordinal)&&Guid.TryParseExact(name[(Id.Length+1)..],"N",out _)))yield return dir;
        }
    }
    public bool IsInstalled {
        get{
            var marker=Path.Combine(Root,Id+".active");
            if(!File.Exists(marker))return Directory.Exists(Path.Combine(Root,Id));
            var key=File.ReadAllText(marker);
            return Guid.TryParseExact(key,"N",out _)&&Directory.Exists(Path.Combine(Root,Id+"."+key));
        }
    }
    public ExpansionManifest Descriptor {get;}=new(1,Id,"旧版物资素材包");
    public string Summary=>"请导入新版物资功能包以启用组件。";
    public string RemovalMessage=>"删除旧版物资素材和对应桌面实例。";
    public string? CoverPath=>null;
    public bool Provides(ExpansionContribution contribution)=>false;
    public void Import(string path)=>ExpansionCatalog.Import(path);
    public void Remove(){
        if(!Directory.Exists(Root))return;
        var marker=Path.Combine(Root,Id+".active");
        var pending=marker+".removing";
        File.WriteAllText(pending,"");File.Move(pending,marker,true);
        FinishRemoval();
    }
    internal static void ClearOldCache(){
        // Prior versions wrote their derived atlas and media to this dedicated directory.
        var dir=Path.Combine(Path.GetTempPath(),"ZZZwallpaper-scan-cards");
        if(!Directory.Exists(dir))return;
        try{
            if((File.GetAttributes(dir)&FileAttributes.ReparsePoint)!=0)return;
            foreach(var file in Directory.EnumerateFiles(dir)){
                var key=Path.GetFileNameWithoutExtension(file);
                if(key.Length==64&&key.All(char.IsAsciiHexDigit)&&Path.GetExtension(file) is ".rgba" or ".video" or ".media" or ".gif" or ".geometry" or ".webp")
                    File.Delete(file);
            }
            if(!Directory.EnumerateFileSystemEntries(dir).Any())Directory.Delete(dir);
        }catch(IOException){}catch(UnauthorizedAccessException){}
    }
    internal static void FinishRemoval(){
        var marker=Path.Combine(Root,Id+".active");
        if(!File.Exists(marker)||new FileInfo(marker).Length!=0)return;
        foreach(var directory in Directories()){
            try{Directory.Delete(directory,true);}catch(IOException){return;}catch(UnauthorizedAccessException){return;}
        }
        File.Delete(marker);
    }
}
