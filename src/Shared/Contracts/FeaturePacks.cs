using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LumaWall.Contracts;

public sealed record PackageItem(string Key, ExpansionEntry Entry, string Directory)
{
    public string FilePath => Path.Combine(Directory, Entry.Implementation!);
    public string Generation=>Path.GetFileName(Directory)=="base"?"builtin":Path.GetFileName(Directory);
}

public static class FeaturePacks
{
    public static readonly string Root = Environment.GetEnvironmentVariable("ZZZWALLPAPER_FEATURE_PACK_ROOT")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LumaWall","FeaturePacks");
    private static string Bundled {
        get {
            var local=Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
            if(Path.GetFileName(local) is "Widgets" or "Mcp") local=Path.GetDirectoryName(local)!;
            return Path.Combine(local,"ExpansionPacks","base");
        }
    }
    private sealed record Installed(ExpansionManifest Manifest,string Directory);
    private static readonly object Gate=new();
    private static Dictionary<string,Installed>? cached;
    private static Dictionary<string,Installed> Items {
        get {lock(Gate) return cached??=ReadInstalled();}
    }
    private static ExpansionManifest Read(string dir) {
        using var file=File.OpenRead(Path.Combine(dir,"expansion.json"));
        return ExpansionManifest.Read(file);
    }
    private static bool Token(string? value)=>value is {Length:>0 and <=64} && Regex.IsMatch(value,@"\A[a-zA-Z0-9][a-zA-Z0-9_-]*\z");
    private static bool Digest(string? value)=>value is {Length:64} && value.All(c=>char.IsAsciiHexDigit(c));
    private static string VersionDir(string digest)=>Path.Combine(Root,"versions",digest);
    private static Dictionary<string,Installed> ReadInstalled() {
        var map=new Dictionary<string,Installed>(StringComparer.OrdinalIgnoreCase);
        if(!Directory.Exists(Root))return map;
        foreach(var pointer in Directory.EnumerateFiles(Root,"*.active")) {
            try {
                var digest=File.ReadAllText(pointer);
                if(!Digest(digest))continue;
                var path=VersionDir(digest);
                var manifest=Read(path);
                if(!System.Version.TryParse(manifest.Version,out _)||manifest.Files==null||!manifest.Files.Keys.All(name=>Path.GetFileName(name)==name&&File.Exists(Path.Combine(path,name))))continue;
                if(Token(manifest.Id)&&manifest.Id==Path.GetFileNameWithoutExtension(pointer))map[manifest.Id]=new(manifest,path);
            } catch(IOException){} catch(JsonException){} catch(InvalidDataException){}
        }
        return map;
    }
    private static bool RetiredBase(ExpansionManifest m)=>m.Files.ContainsKey("ZZZwallpaper.WarehouseMedia.dll")||m.Widgets.Any(w=>w.Id=="Warehouse");
    private static FileStream? baseLease;
    private static readonly Lazy<string> Base=new(()=> {
        var pinned=Environment.GetEnvironmentVariable("ZZZWALLPAPER_BASE_DIRECTORY");
        var selected=pinned??(Items.TryGetValue("base",out var pack)&&!RetiredBase(pack.Manifest)&&System.Version.Parse(pack.Manifest.Version)>=System.Version.Parse(Read(Bundled).Version)?pack.Directory:Bundled);
        if(!string.Equals(selected,Bundled,StringComparison.OrdinalIgnoreCase))baseLease=File.OpenRead(Path.Combine(selected,"expansion.json"));
        return selected;
    });
    public static string BaseDirectory=>Base.Value;
    public static string InstalledBaseVersion=>Items.TryGetValue("base",out var p)&&!RetiredBase(p.Manifest)&&System.Version.Parse(p.Manifest.Version)>System.Version.Parse(Read(Bundled).Version)?p.Manifest.Version:Read(Bundled).Version;
    private static readonly Lazy<Installed> ActiveBase =new(()=>new(Read(BaseDirectory),BaseDirectory));
    private static IEnumerable<PackageItem> Enumerate(bool widgets)=>Items.Values.Where(p=>p.Manifest.Id!="base").Prepend(ActiveBase.Value)
        .SelectMany(p=>(widgets?p.Manifest.Widgets:p.Manifest.Effects).Where(e=>e.Implementation!=null).Select(e=>new PackageItem(p.Manifest.Id+"/"+e.Id,e,p.Directory)));
    public static IEnumerable<PackageItem> Effects=>Enumerate(false);
    public static IEnumerable<PackageItem> Widgets=>Enumerate(true);
    public static IReadOnlyList<ParticleAppearance> ParticleStyles=>Items.Values.FirstOrDefault(p=>p.Manifest.Contributions.Contains(new("effect","Particles","particles.artwork")))?.Manifest.ParticleStyles??[];
    public static string? ResourceDirectory(ExpansionContribution target)=>Items.Values.FirstOrDefault(p=>p.Manifest.Contributions.Contains(target))?.Directory;
    public static PackageGrid? GridFor(WidgetInstance widget)=>widget.Kind==WidgetKind.Extension
        ?Widgets.FirstOrDefault(w=>w.Key==widget.PackageWidgetId)?.Entry.Grid:null;
    public static PackageItem? FindWidget(string? key) {
        var item=Widgets.FirstOrDefault(x=>x.Key==key);
        if(item==null){lock(Gate) cached=null;item=Widgets.FirstOrDefault(x=>x.Key==key);}
        return item;
    }
    private sealed class Provider(Installed pack):IExpansionPackage {
        public ExpansionManifest Descriptor=>pack.Manifest;
        public bool IsInstalled=>true;
        public string Summary=>$"v{Descriptor.Version} · 画面效果 {Descriptor.Effects.Length} · 组件 {Descriptor.Widgets.Length}";
        public string RemovalMessage=>Descriptor.Id=="warehouse-particles"?"删除扩展包会移除已添加的搜索物资组件和专属设置。再次导入后需要重新添加。":"删除扩展包后，对应项目暂停使用，已保存的设置会保留。";
        public string? CoverPath=>null;
        public bool Provides(ExpansionContribution contribution)=>Descriptor.Contributions.Contains(contribution);
        public void Import(string path)=>ExpansionCatalog.Import(path);
        public void Remove(){lock(Gate){
            using var transaction=new FileStream(Path.Combine(Root,"install.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
            File.Delete(Path.Combine(Root,Descriptor.Id+".active"));cached=null;
            Prune();
        }}
    }
    public static IEnumerable<IExpansionPackage> Addons=>Items.Values.Where(p=>p.Manifest.Id!="base").Select(p=>new Provider(p));

    public static void CollectUnused(){
        LegacyMaterials.FinishRemoval();LegacyMaterials.ClearOldCache();
        if(!Directory.Exists(Root))return;
        lock(Gate){try{
            using var transaction=new FileStream(Path.Combine(Root,"install.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
            if(ReadInstalled().TryGetValue("base",out var p)&&RetiredBase(p.Manifest)){
                File.Delete(Path.Combine(Root,"base.active"));cached=null;
            }
            Prune();
        }catch(IOException){}catch(UnauthorizedAccessException){}}
    }
    private static void Prune() {
        var versions=Path.Combine(Root,"versions");
        if(!Directory.Exists(versions))return;
        var keep=ReadInstalled().Values.Select(v=>v.Directory).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach(var dir in Directory.EnumerateDirectories(versions)){
            if(!Digest(Path.GetFileName(dir))||keep.Contains(dir))continue;
            try{
                ParticleArtwork.Release(dir);
                var manifest=Path.Combine(dir,"expansion.json");
                if(File.Exists(manifest))using(File.Open(manifest,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){}
                var retired=Path.Combine(Root,"retired-"+Path.GetFileName(dir)+"-"+Guid.NewGuid().ToString("N"));
                Directory.Move(dir,retired);
            }catch(IOException){}catch(UnauthorizedAccessException){}
        }
        foreach(var retired in Directory.EnumerateDirectories(Root,"retired-*")){
            try{Directory.Delete(retired,true);}catch(IOException){}catch(UnauthorizedAccessException){}
        }
    }
    private static byte[] Bytes(ZipArchiveEntry? entry,int max) {
        if(entry==null||entry.Length<=0||entry.Length>max)throw new InvalidDataException("扩展包内容不完整。");
        using var input=entry.Open();var result=new byte[(int)entry.Length];input.ReadExactly(result);
        if(input.ReadByte()!=-1)throw new InvalidDataException("扩展包数据长度无效。");
        return result;
    }
    internal static void Import(ZipArchive archive,ExpansionManifest manifest) {
        var header=Bytes(archive.GetEntry("expansion.json"),65536);
        var signature=Bytes(archive.GetEntry("publisher.sig"),1024);
        using var rsa=RSA.Create();
        using var pem=new StreamReader(typeof(FeaturePacks).Assembly.GetManifestResourceStream("FeaturePackPublisher")!);
        rsa.ImportFromPem(pem.ReadToEnd());
        if(!rsa.VerifyData(header,signature,HashAlgorithmName.SHA256,RSASignaturePadding.Pss))throw new InvalidDataException("扩展包发布者签名无效。");
        if(!Token(manifest.Id)||!System.Version.TryParse(manifest.Version,out var version)
            ||manifest.Files==null||manifest.Files.Count is 0 or >64||archive.Entries.Count!=manifest.Files.Count+2
            ||archive.Entries.Select(x=>x.FullName).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=archive.Entries.Count
            ||archive.Entries.Sum(x=>x.Length)>128L*1024*1024)throw new InvalidDataException("扩展包标识、版本或文件列表无效。");
        foreach(var file in manifest.Files)
            if(!Regex.IsMatch(file.Key,@"\A[A-Za-z0-9_-][A-Za-z0-9_.-]{0,90}\.(dll|cso|exe|png|br|zip)\z")||!Digest(file.Value))throw new InvalidDataException("扩展包文件名或摘要无效。");
        if(manifest.ParticleStyles==null||manifest.ParticleStyles.Length>0&&(
            !manifest.Contributions.Contains(new("effect","Particles","particles.artwork"))||
            !manifest.Files.ContainsKey("builtins.rgba.br")||manifest.ParticleStyles.Length!=ParticleArtwork.Count||
            manifest.ParticleStyles.Where((s,i)=>s==null||s.Id!=i+ParticleArtwork.FirstStyle||string.IsNullOrWhiteSpace(s.Name)
                ||s.File==null||!s.File.EndsWith(".png",StringComparison.Ordinal)||!manifest.Files.ContainsKey(s.File)).Any()))
            throw new InvalidDataException("粒子图片清单无效。");
        foreach(var widget in manifest.Widgets)widget.Grid?.Validate();
        if(manifest.Id=="base"){
            var original=Read(Bundled);
            if(RetiredBase(manifest)||version<System.Version.Parse(original.Version)||!new[]{"BasePlayback.dll","ZZZwallpaper.BaseWidgets.dll","TaskbarHost.exe"}.All(manifest.Files.ContainsKey)
                ||!original.Effects.Where(e=>e.Implementation==null).Select(x=>x.Id).Order().SequenceEqual(manifest.Effects.Where(e=>e.Implementation==null).Select(x=>x.Id).Order())
                ||!original.Widgets.Where(e=>e.Implementation==null).Select(x=>x.Id).Order().SequenceEqual(manifest.Widgets.Where(e=>e.Implementation==null).Select(x=>x.Id).Order()))
                throw new InvalidDataException("基础包版本或接口不兼容。");
        }
        {
            foreach(var (entries,suffix) in new[]{(manifest.Widgets,".dll"),(manifest.Effects,".cso")}){
                if(entries.Length>64||entries.Select(e=>e.Id).Distinct().Count()!=entries.Length)throw new InvalidDataException("扩展注册项重复或过多。");
                foreach(var e in entries.Where(e=>manifest.Id!="base"||e.Implementation!=null))if(!Token(e.Id)||string.IsNullOrWhiteSpace(e.Title)||e.Implementation==null
                    ||!e.Implementation.EndsWith(suffix,StringComparison.Ordinal)||!manifest.Files.ContainsKey(e.Implementation)
                    ||(suffix==".dll"&&string.IsNullOrWhiteSpace(e.EntryPoint)))throw new InvalidDataException("扩展注册项缺少实现。");
            }
        }
        lock(Gate){
            Directory.CreateDirectory(Root);
            using var transaction=new FileStream(Path.Combine(Root,"install.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
            var digest=Convert.ToHexString(SHA256.HashData(header)).ToLowerInvariant();
            var dest=VersionDir(digest);
            if(ReadInstalled().TryGetValue(manifest.Id,out var before)&&
               (System.Version.Parse(before.Manifest.Version)>version||(before.Manifest.Version==manifest.Version&&before.Directory!=dest)))throw new InvalidDataException("已安装相同或更新版本。");
            var stage=Path.Combine(Root,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
            try{
                foreach(var f in manifest.Files){
                    var entry=archive.GetEntry(f.Key);
                    if(entry==null||entry.Length<=0||entry.Length>96L*1024*1024)throw new InvalidDataException("扩展文件大小无效。");
                    using var source=entry.Open();
                    using var target=new FileStream(Path.Combine(stage,f.Key),FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None);
                    var buffer=new byte[65536];long remaining=entry.Length;
                    while(remaining>0){var read=source.Read(buffer,0,(int)Math.Min(buffer.Length,remaining));if(read==0)throw new EndOfStreamException();target.Write(buffer,0,read);remaining-=read;}
                    target.Position=0;
                    if(source.ReadByte()!=-1||!Convert.ToHexString(SHA256.HashData(target)).Equals(f.Value,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("扩展包校验失败："+f.Key);
                }
                File.WriteAllBytes(Path.Combine(stage,"expansion.json"),header);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                if(!Directory.Exists(dest))Directory.Move(stage,dest);
                var marker=Path.Combine(Root,manifest.Id+".active");File.WriteAllText(marker+".new",digest);File.Move(marker+".new",marker,true);cached=null;Prune();
                if(manifest.Id=="warehouse-particles"&&LegacyMaterials.Current.IsInstalled)LegacyMaterials.Current.Remove();
            } finally{if(Directory.Exists(stage))Directory.Delete(stage,true);}
        }
    }
}
