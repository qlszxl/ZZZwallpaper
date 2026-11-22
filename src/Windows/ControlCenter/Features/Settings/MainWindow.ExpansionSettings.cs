using LumaWall.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Pickers;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private readonly List<Action> expansionRefreshers=[];
    private bool expansionBusy;
    private Grid expansionCards=null!;
    private TextBlock expansionCount=null!;
    private Button expansionImport=null!;

    private void InitializeExpansionSettings()
    {
        var library=PresetPanel();
        expansionCount=UtilityText("","UtilityHeading");
        expansionImport=PackageButton(UiLanguage.Text("导入 ZIP"),()=>RunExpansionOperation(PickExpansion),true);
        expansionImport.HorizontalAlignment=HorizontalAlignment.Right;
        library.Children.Add(PackageLibraryHeader(expansionCount,expansionImport));
        expansionCards=new Grid {ColumnSpacing=LibraryPresentation.CardSpacing,RowSpacing=LibraryPresentation.CardSpacing};
        expansionCards.SizeChanged+=(_,e)=>LayoutPackageCards(expansionCards,e.NewSize.Width);
        library.Children.Add(expansionCards);
        ExpansionPanel.Children.Add(UtilityCard(library,hasOwnHeader:true));
        RefreshExpansionList();
    }

    private void RefreshExpansionList()
    {
        var installed=ExpansionCatalog.Packages.Where(p=>p.IsInstalled).ToArray();
        expansionCards.Children.Clear();
        expansionCount.Text=UiLanguage.Format($"已安装扩展包 · {installed.Length+1}");
        var core=ExpansionCatalog.Base;
        var coreFooter=new StackPanel {Spacing=8};
        coreFooter.Children.Add(UtilityText(UiLanguage.Format($"画面效果 · {core.Effects.Length}　组件 · {core.Widgets.Length}")));
        coreFooter.Children.Add(UtilityText(UiLanguage.Text("内置 · v")+FeaturePacks.InstalledBaseVersion+(FeaturePacks.InstalledBaseVersion!=core.Version?UiLanguage.Text(" · 重启软件生效"):"")));
        var logo=presetLogo??=new BitmapImage {UriSource=new Uri("ms-appx:///Assets/Branding/icon.png"),DecodePixelWidth=240};
        expansionCards.Children.Add(PackageTile(core.Id,UiLanguage.Text(core.Name),logo,coreFooter));
        foreach(var pack in installed)AddExpansionCard(pack,logo);
        LayoutPackageCards(expansionCards,expansionCards.ActualWidth);
    }

    private void AddExpansionCard(IExpansionPackage pack,BitmapImage fallback)
    {
        var footer=new StackPanel {Spacing=8};
        footer.Children.Add(UtilityText(pack.Summary));
        var remove=PackageButton(UiLanguage.Text("删除"),()=>RunExpansionOperation(()=>ConfirmRemoveExpansion(pack)));
        AutomationProperties.SetName(remove,UiLanguage.Text("删除：")+pack.Descriptor.Name);
        footer.Children.Add(remove);
        var cover=pack.CoverPath is { } path?new BitmapImage {DecodePixelWidth=200,UriSource=new Uri(path)}:fallback;
        expansionCards.Children.Add(PackageTile(pack.Descriptor.Id,pack.Descriptor.Name,cover,footer));
    }

    private async Task RunExpansionOperation(Func<Task> operation)
    {
        if(expansionBusy)return;
        expansionBusy=true;expansionImport.IsEnabled=false;
        try {await operation();}
        finally {expansionBusy=false;expansionImport.IsEnabled=true;}
    }

    private async Task PickExpansion()
    {
        var picker=new FileOpenPicker();
        picker.FileTypeFilter.Add(".zip");
        WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(this));
        var file=await picker.PickSingleFileAsync();
        if(file!=null&&!libraryShutdown)await ImportExpansion(file.Path);
    }

    private async Task ConfirmRemoveExpansion(IExpansionPackage pack)
    {
        var dialog=new ContentDialog {XamlRoot=Root.XamlRoot,Title=UiLanguage.Format($"删除“{pack.Descriptor.Name}”？"),
            Content=pack.RemovalMessage,
            PrimaryButtonText=UiLanguage.Text("删除扩展包"),CloseButtonText=UiLanguage.Text("取消"),DefaultButton=ContentDialogButton.Close,Style=HomeStyle("LibraryDialog")};
        if(await dialog.ShowAsync()==ContentDialogResult.Primary&&!libraryShutdown)await UpdateExpansion(pack.Remove);
    }

    private Task ImportExpansion(string path)=>UpdateExpansion(()=>ExpansionCatalog.Import(path));
    private async Task UpdateExpansion(Action change)
    {
        var previous=FeaturePacks.Widgets.Select(w=>w.Key+"@"+w.Directory).Order().ToArray();
        widgets?.FlushPendingSave();
        await Task.Run(change);
        if(libraryShutdown)return;
        widgets?.RefreshInstalledWidgets();
        RefreshExpansionList();
        foreach(var refresh in expansionRefreshers)refresh();
        effectsOverviewKey="";widgetOverviewKey="";widgetDetailKey=null;
        RefreshEffectCollection();
        if(previous.SequenceEqual(FeaturePacks.Widgets.Select(w=>w.Key+"@"+w.Directory).Order()))widgets?.Refresh();
        else widgets?.Restart();
        wallpapers?.RefreshLiveEffects();
        if(widgets!=null)await widgets.Retirement;
        await Task.Run(FeaturePacks.CollectUnused);
    }
}
