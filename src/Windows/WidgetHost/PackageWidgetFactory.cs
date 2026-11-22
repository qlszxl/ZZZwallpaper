using System.Reflection;
using System.Runtime.Loader;
using System.Collections.Concurrent;
using LumaWall.Contracts;
using W=System.Windows;
using C=System.Windows.Controls;

namespace LumaWall.WidgetHost;

public interface IPackageWidgetV1 : IDisposable
{
    W.FrameworkElement Create(WidgetInstance settings);
}

internal static class PackageWidgetFactory
{
    private static readonly Lazy<Func<WidgetContentContext,SearchFieldState,WidgetContent>> Base=new(()=>{
        var assembly=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(FeaturePacks.BaseDirectory,"ZZZwallpaper.BaseWidgets.dll"));
        return assembly.GetType("LumaWall.WidgetHost.WidgetContentFactory",true)!.GetMethod("Create",BindingFlags.Static|BindingFlags.NonPublic)!
            .CreateDelegate<Func<WidgetContentContext,SearchFieldState,WidgetContent>>();
    });
    internal static WidgetContent Create(WidgetContentContext context,SearchFieldState search){
        var widget=context.Instance();
        if(widget.Kind!=WidgetKind.Extension)return Base.Value(context,search);
        var entry=FeaturePacks.FindWidget(widget.PackageWidgetId);
        if(entry?.Entry.Grid!=null){
            var assembly=Contexts.GetOrAdd(entry.Directory,dir=>new PackageContext(dir)).LoadFromAssemblyPath(entry.FilePath);
            var type=assembly.GetType(entry.Entry.EntryPoint!,true)!;
            return (WidgetContent)Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public,null,[context],null)!;
        }
        return new ExternalContent(context);
    }

    private sealed class PackageContext(string directory):AssemblyLoadContext(directory)
    {
        protected override Assembly? Load(AssemblyName assemblyName){
            if(assemblyName.Name is not { } name||name!=Path.GetFileName(name))return null;
            if(name==typeof(IPackageWidgetV1).Assembly.GetName().Name)return typeof(IPackageWidgetV1).Assembly;
            if(name==typeof(WidgetInstance).Assembly.GetName().Name)return typeof(WidgetInstance).Assembly;
            var path=Path.Combine(directory,name+".dll");
            return File.Exists(path)?LoadFromAssemblyPath(path):null;
        }
    }
    private static readonly ConcurrentDictionary<string,PackageContext> Contexts=new(StringComparer.OrdinalIgnoreCase);
    private sealed class ExternalContent:WidgetContent
    {
        private readonly IPackageWidgetV1? plugin;
        public ExternalContent(WidgetContentContext context):base(context){
            try{
                var entry=FeaturePacks.FindWidget(instance.PackageWidgetId)??throw new FileNotFoundException();
                var assembly=Contexts.GetOrAdd(entry.Directory,dir=>new PackageContext(dir)).LoadFromAssemblyPath(entry.FilePath);
                var type=assembly.GetType(entry.Entry.EntryPoint!,true)!;
                plugin=(IPackageWidgetV1)Activator.CreateInstance(type)!;
                Element=plugin.Create(instance);
            }catch(Exception error){AppLog.Warning("ExtensionWidget",instance.PackageWidgetId??"Unknown widget",error);Element=new C.TextBlock {Text=UiLanguage.Text("扩展组件暂不可用"),Foreground=System.Windows.Media.Brushes.White};}
        }
        public override void Dispose(){try{plugin?.Dispose();}catch(Exception error){AppLog.Warning("ExtensionWidget","Release widget",error);}}
    }
}
