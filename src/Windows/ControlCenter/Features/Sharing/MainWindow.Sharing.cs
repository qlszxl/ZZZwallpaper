using System.Text.Json;
using LumaWall.Contracts;
using LumaWall.Contracts.Presets;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Screen=System.Windows.Forms.Screen;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    // Keep the legacy automation adapter independent from the ZIP-only preset UI.
    private SharingPresenter sharing=null!;
    private PresetSelection presetPreferences=new();
    private CheckBox presetPlaying=null!,presetLibrary=null!,presetEffects=null!,presetWidgets=null!,presetPrivacy=null!;
    private TextBox presetName=null!;
    private TextBlock presetStatus=null!,presetListCount=null!;
    private StackPanel presetEditor=null!,presetPrivacyPanel=null!,presetStatusPanel=null!;
    private ContentControl presetLibraryHost=null!;
    private Grid presetChoiceGrid=null!,presetHeader=null!,presetCards=null!;
    private Button presetSave=null!,presetImport=null!,presetCancel=null!,presetUndo=null!;
    private ContentDialog? presetSaveDialog;
    private ContentDialog? presetDetailsDialog;
    private TextBox? presetDetailsName;
    private static BitmapImage? presetLogo;
    private ProgressRing presetProgress=null!;
    private bool presetBusy;
    private int presetListEpoch;
    private CancellationTokenSource? presetCancellation;
    private sealed record PresetScreenChoice(string? Id,string Label) {public override string ToString()=>Label;}

    private void InitializeSharingPresenter()
    {
        var activation=new SharingActivationCoordinator(()=>widgets,()=>wallpapers);
        activation.WidgetsActivated+=()=> {ResetWidgetNavigation();RefreshWidgets();};
        activation.EffectsActivated+=()=>LoadEffectDraft(true);
        sharing=new SharingPresenter(modules.Settings,modules.Sharing,activation);
    }

    private void InitializeSharingSettings()
    {
        sharing.LoadPreferences();
        try {presetPreferences=JsonSerializer.Deserialize<PresetSelection>(modules.Settings.Get("presets.options.v1")??"null")??new();}
        catch(Exception ex) {AppLog.Warning("Presets","Could not read preset preferences",ex);presetPreferences=new();}
    }

    private static StackPanel PresetPanel()=>new() {Spacing=ParameterUi.FieldGap};
    private Button PresetButton(string title,Func<Task> action,bool primary=false)=>
        PackageButton(title,()=>presetBusy?Task.CompletedTask:action(),primary);
    private Button PackageButton(string title,Func<Task> action,bool primary=false)
    {
        var button=new Button {Style=HomeStyle(primary?"HomePrimaryButton":"HomeButton"),HorizontalAlignment=HorizontalAlignment.Stretch};
        UtilityButtonContent(button,title);
        button.Click+=async(_,_)=> {
            try {await action();}
            catch(Exception ex) {if(!libraryShutdown)ShowError(title+UiLanguage.Text("未完成：")+ex.Message,ex);}
        };
        return button;
    }

    private CheckBox PresetChoice(string title,string icon,bool value)
    {
        var check=SelectionCard(title,value,source=>UtilityIcon(icon,source,24));
        check.VerticalAlignment=VerticalAlignment.Top;
        check.Checked+=(_,_)=>PresetOptionsChanged();
        check.Unchecked+=(_,_)=>PresetOptionsChanged();
        return check;
    }

    private void InitializeSharingPage()
    {
        SharingPanel.Children.Clear();
        presetEditor=PresetPanel();
        presetName=new TextBox {Style=HomeStyle("UtilityTextBox"),MaxLength=80,Text=UiLanguage.Text("我的桌面")};
        // The dialog's buttons commit the name; typing must not require a second edit gesture.
        DoubleClickEdit.SetEnabled(presetName,false);
        AutomationProperties.SetName(presetName,UiLanguage.Text("预设名称"));
        presetEditor.Children.Add(WidgetField(UiLanguage.Text("名称"),presetName));

        presetChoiceGrid=new Grid {ColumnSpacing=ParameterUi.TileGap,RowSpacing=ParameterUi.TileGap};
        presetPlaying=PresetChoice(UiLanguage.Text("正在播放"),"Play",presetPreferences.Playing);
        presetLibrary=PresetChoice(UiLanguage.Text("素材库"),"Image",presetPreferences.Library);
        presetEffects=PresetChoice(UiLanguage.Text("画面效果"),"Settings",presetPreferences.Effects);
        presetWidgets=PresetChoice(UiLanguage.Text("组件"),"Widgets",presetPreferences.Widgets);
        presetChoiceGrid.Children.Add(presetPlaying);
        presetChoiceGrid.Children.Add(presetLibrary);
        presetChoiceGrid.Children.Add(presetEffects);
        var component=PresetPanel();component.Spacing=8;component.Children.Add(presetWidgets);
        presetPrivacyPanel=PresetPanel();presetPrivacyPanel.Spacing=6;
        presetPrivacy=new CheckBox {Style=HomeStyle("LibraryCheckBox"),Content=UiLanguage.Text("包含隐私内容"),IsChecked=presetPreferences.IncludeWidgetPrivateData};
        AutomationProperties.SetName(presetPrivacy,UiLanguage.Text("包含组件隐私内容"));
        presetPrivacy.Checked+=(_,_)=>PresetOptionsChanged();presetPrivacy.Unchecked+=(_,_)=>PresetOptionsChanged();
        presetPrivacyPanel.Children.Add(presetPrivacy);
        component.Children.Add(presetPrivacyPanel);presetChoiceGrid.Children.Add(component);
        void LayoutChoices(double width)=>FlowCards(presetChoiceGrid,width>=400?2:1);
        presetChoiceGrid.SizeChanged+=(_,e)=>LayoutChoices(e.NewSize.Width);LayoutChoices(0);
        presetEditor.Children.Add(presetChoiceGrid);

        var library=PresetPanel();
        presetListCount=UtilityText(UiLanguage.Text("预设"),"UtilityHeading");
        var headerActions=new StackPanel {Orientation=Orientation.Horizontal,Spacing=12,HorizontalAlignment=HorizontalAlignment.Right};
        presetImport=PresetButton(UiLanguage.Text("导入 ZIP"),ImportPreset);headerActions.Children.Add(presetImport);
        presetSave=PresetButton(UiLanguage.Text("保存当前配置"),ShowSavePresetDialog,true);headerActions.Children.Add(presetSave);
        presetHeader=PackageLibraryHeader(presetListCount,headerActions);
        library.Children.Add(presetHeader);
        presetCards=new Grid {ColumnSpacing=LibraryPresentation.CardSpacing,RowSpacing=LibraryPresentation.CardSpacing};
        presetCards.SizeChanged+=(_,e)=>LayoutPackageCards(presetCards,e.NewSize.Width);
        library.Children.Add(presetCards);
        var listCard=UtilityCard(library,hasOwnHeader:true);
        presetLibraryHost=listCard;
        SharingPanel.Children.Add(listCard);
        InitializePresetDrop();

        presetStatusPanel=PresetPanel();presetStatusPanel.Spacing=8;
        presetStatus=UtilityText("");presetStatus.Visibility=Visibility.Collapsed;presetStatus.IsTextSelectionEnabled=true;presetStatusPanel.Children.Add(presetStatus);
        var operation=new StackPanel {Orientation=Orientation.Horizontal,Spacing=12};
        presetProgress=new ProgressRing {Width=22,Height=22,IsActive=false,Visibility=Visibility.Collapsed};
        operation.Children.Add(presetProgress);
        presetCancel=new Button {Content=UiLanguage.Text("取消"),Style=HomeStyle("UtilityButton"),Visibility=Visibility.Collapsed};
        presetCancel.Click+=(_,_)=> {presetCancellation?.Cancel();presetCancel.IsEnabled=false;SetPresetStatus(UiLanguage.Text("正在取消…"));};
        operation.Children.Add(presetCancel);
        presetUndo=PresetButton(UiLanguage.Text("撤销应用预设"),async()=> {await RestoreHistory(false);RefreshSharingPage();});
        operation.Children.Add(presetUndo);presetStatusPanel.Children.Add(operation);
        SharingPanel.Children.Add(presetStatusPanel);
        Closed+=(_,_)=>presetCancellation?.Cancel();
        UpdatePresetControls();RefreshPresetCards();
        DecorateParameterSections(SharingPanel,UiLanguage.Text("预设"));
    }

    private async Task ShowSavePresetDialog()
    {
        if(presetSaveDialog!=null||presetBusy)return;
        presetEditor.Width=Math.Max(240,Math.Min(540,Root.ActualWidth-140));
        var scroll=new ScrollViewer {Style=HomeStyle("HomeScrollViewer"),Content=presetEditor,MaxHeight=Math.Max(180,Root.ActualHeight-230),
            VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        var dialog=new ContentDialog {XamlRoot=Root.XamlRoot,Title=UiLanguage.Text("保存当前配置"),Content=scroll,
            PrimaryButtonText=UiLanguage.Text("保存"),SecondaryButtonText=UiLanguage.Text("导出 ZIP"),CloseButtonText=UiLanguage.Text("取消"),
            DefaultButton=ContentDialogButton.Primary,Style=HomeStyle("LibraryDialog")};
        dialog.Resources["ContentDialogMaxWidth"]=640d;
        presetSaveDialog=dialog;UpdatePresetControls();
        ContentDialogResult result;
        try {result=await dialog.ShowAsync();}
        finally {scroll.Content=null;dialog.Content=null;presetSaveDialog=null;}
        if(libraryShutdown)return;
        if(result==ContentDialogResult.Primary)await SaveCurrentPreset();
        else if(result==ContentDialogResult.Secondary)await ExportCurrentPreset();
    }

    private PresetSelection CurrentPresetSelection()=>new() {
        Playing=presetPlaying.IsChecked==true,Library=presetLibrary.IsChecked==true,
        Effects=presetEffects.IsChecked==true,Widgets=presetWidgets.IsChecked==true,
        IncludeWidgetPrivateData=presetWidgets.IsChecked==true&&presetPrivacy.IsChecked==true
    };

    private void PresetOptionsChanged()
    {
        if(presetPrivacy==null||presetWidgets==null)return;
        presetPreferences=CurrentPresetSelection();
        try {modules.Settings.Set("presets.options.v1",JsonSerializer.Serialize(presetPreferences));}
        catch(Exception ex) {ShowError(UiLanguage.Text("预设选项未保存：")+ex.Message,ex);}
        UpdatePresetControls();
    }

    private void UpdatePresetControls()
    {
        if(presetSave==null)return;
        presetLibraryHost.IsEnabled=!presetBusy;presetImport.IsEnabled=!presetBusy;
        bool any=CurrentPresetSelection().Any;
        presetSave.IsEnabled=!presetBusy;
        if(presetSaveDialog!=null)presetSaveDialog.IsPrimaryButtonEnabled=presetSaveDialog.IsSecondaryButtonEnabled=!presetBusy&&any;
        presetPrivacy.IsEnabled=presetWidgets.IsChecked==true;presetPrivacyPanel.Opacity=presetPrivacy.IsEnabled?1:0.5;
        presetProgress.IsActive=presetBusy;presetProgress.Visibility=presetBusy?Visibility.Visible:Visibility.Collapsed;
        RefreshPresetUndo();
    }

    private void RefreshPresetUndo()
    {
        if(presetUndo!=null) {
            presetUndo.IsEnabled=!presetBusy&&!historyBusy&&store.UndoLabel?.StartsWith("应用预设：",StringComparison.Ordinal)==true;
            presetUndo.Visibility=presetUndo.IsEnabled?Visibility.Visible:Visibility.Collapsed;
        }
        RefreshPresetActivity();
    }

    private void SetPresetStatus(string text)
    {
        presetStatus.Text=text;presetStatus.Visibility=text.Length==0?Visibility.Collapsed:Visibility.Visible;
        RefreshPresetActivity();
    }

    private void RefreshPresetActivity()
    {
        if(presetStatusPanel==null||presetUndo==null)return;
        presetStatusPanel.Visibility=presetBusy||presetStatus.Text.Length>0||presetUndo.Visibility==Visibility.Visible?Visibility.Visible:Visibility.Collapsed;
    }

    private void RefreshSharingPage()
    {
        if(presetName==null)return;
        EndEffectEditing();widgets?.FlushPendingSave();
        UpdatePresetControls();RefreshPresetCards();
    }

    private static Grid PackageLibraryHeader(TextBlock heading,FrameworkElement actions)
    {
        var header=new Grid {ColumnSpacing=12,RowSpacing=12};
        header.ColumnDefinitions.Add(new());header.ColumnDefinitions.Add(new() {Width=GridLength.Auto});
        header.RowDefinitions.Add(new() {Height=GridLength.Auto});
        heading.VerticalAlignment=VerticalAlignment.Center;header.Children.Add(heading);
        Grid.SetColumn(actions,1);header.Children.Add(actions);
        header.SizeChanged+=(_,e)=> {
            bool narrow=e.NewSize.Width<520;
            if(narrow&&header.RowDefinitions.Count==1)header.RowDefinitions.Add(new() {Height=GridLength.Auto});
            if(!narrow&&header.RowDefinitions.Count==2)header.RowDefinitions.RemoveAt(1);
            Grid.SetRow(actions,narrow?1:0);Grid.SetColumn(actions,narrow?0:1);Grid.SetColumnSpan(actions,narrow?2:1);
        };
        return header;
    }

    private static void LayoutPackageCards(Grid cards,double width)
    {
        if(double.IsFinite(width)&&width>0)FlowCards(cards,LibraryPresentation.ResponsiveGrid(width).Columns);
    }

    private Border PresetTile(PresetSummary entry)
    {
        var actions=new Grid {ColumnSpacing=6,RowSpacing=6};
        actions.ColumnDefinitions.Add(new());
        actions.ColumnDefinitions.Add(new());
        actions.RowDefinitions.Add(new() {Height=GridLength.Auto});
        actions.RowDefinitions.Add(new() {Height=GridLength.Auto});
        var buttons=new[] {PresetButton(UiLanguage.Text("应用"),()=>ApplyPreset(entry),true),
            PresetButton(UiLanguage.Text("详情"),()=>ShowPresetDetails(entry)),
            PresetButton(UiLanguage.Text("导出 ZIP"),()=>ExportSavedPreset(entry)),PresetButton(UiLanguage.Text("删除"),()=>DeletePreset(entry))};
        for(int i=0;i<buttons.Length;i++) {
            var button=buttons[i];button.Padding=new Thickness(2,6,2,6);button.MinWidth=0;button.MinHeight=40;
            AutomationProperties.SetName(button,AutomationProperties.GetName(button)+"："+entry.Name);
            if(button.Content is StackPanel content)foreach(var label in content.Children.OfType<TextBlock>())label.FontSize=12;
            Grid.SetColumn(button,i%2);Grid.SetRow(button,i/2);actions.Children.Add(button);
        }
        var logo=presetLogo??=new BitmapImage {UriSource=new Uri("ms-appx:///Assets/Branding/icon.png"),DecodePixelWidth=240};
        return PackageTile(entry.Id,entry.Name,logo,actions);
    }

    private static Border PackageTile(string id,string name,ImageSource image,FrameworkElement actions)
    {
        var body=new Grid {RowSpacing=10,Padding=new Thickness(10)};
        body.RowDefinitions.Add(new());
        body.RowDefinitions.Add(new() {Height=GridLength.Auto});
        body.RowDefinitions.Add(new() {Height=GridLength.Auto});
        var cover=new Border {Background=HomeBrush("HomeWellBrush"),CornerRadius=new CornerRadius(10)};
        var icon=new Image {Source=image,
            MaxWidth=120,MaxHeight=104,Margin=new Thickness(10),Stretch=Stretch.Uniform};
        icon.HorizontalAlignment=HorizontalAlignment.Center;icon.VerticalAlignment=VerticalAlignment.Center;
        icon.IsHitTestVisible=false;AutomationProperties.SetAccessibilityView(icon,AccessibilityView.Raw);
        cover.Child=icon;body.Children.Add(cover);

        var title=UtilityText(name,"UtilityHeading");
        title.FontSize=16;title.TextWrapping=TextWrapping.NoWrap;title.TextTrimming=TextTrimming.CharacterEllipsis;
        ToolTipService.SetToolTip(title,name);
        Grid.SetRow(title,1);body.Children.Add(title);
        Grid.SetRow(actions,2);body.Children.Add(actions);
        var tile=new Border {Tag=id,Background=HomeBrush("HomeControlRecess"),BorderBrush=HomeBrush("HomeRimBrush"),
            BorderThickness=new Thickness(3),CornerRadius=new CornerRadius(18),Child=body,
            HorizontalAlignment=HorizontalAlignment.Stretch,VerticalAlignment=VerticalAlignment.Top};
        tile.SizeChanged+=(_,e)=> {
            if(e.NewSize.Width>0&&(double.IsNaN(tile.Height)||Math.Abs(tile.Height-e.NewSize.Width)>.1))tile.Height=e.NewSize.Width;
        };
        AutomationProperties.SetName(tile,name);
        return tile;
    }

    private async void RefreshPresetCards()
    {
        int epoch=++presetListEpoch;
        try {
            var entries=await Task.Run(()=>modules.Presets.List());
            if(epoch!=presetListEpoch||libraryShutdown)return;
            presetCards.Children.Clear();presetListCount.Text=UiLanguage.Format($"预设 · {entries.Count}");
            foreach(var entry in entries)presetCards.Children.Add(PresetTile(entry));
            LayoutPackageCards(presetCards,presetCards.ActualWidth);
        } catch(Exception ex) {
            if(epoch==presetListEpoch&&!libraryShutdown) {SetPresetStatus(UiLanguage.Text("预设列表读取失败：")+ex.Message);AppLog.Warning("Presets","Could not list presets",ex);}
        }
    }

    private static string PresetCategoryText(PresetSelection selection)
    {
        var values=new List<string>();
        if(selection.Playing)values.Add(UiLanguage.Text("正在播放"));if(selection.Library)values.Add(UiLanguage.Text("素材库"));
        if(selection.Effects)values.Add(UiLanguage.Text("画面效果"));if(selection.Widgets)values.Add(UiLanguage.Text("组件"));
        return string.Join(" · ",values)+(selection.Widgets&&selection.IncludeWidgetPrivateData?UiLanguage.Text(" · 含组件隐私内容"):"");
    }

    private PresetDocument CaptureCurrentPreset()
    {
        FlushHistoryEdits();
        string name=presetName.Text.Trim();if(name.Length==0)name=UiLanguage.Text("我的预设");
        var screens=Screen.AllScreens.Select(s=>s.DeviceName).ToArray();
        var bindings=wallpapers?.Running.Values.Select(item=>item.Binding).ToArray();
        return modules.Presets.Capture(name,screens,CurrentPresetSelection(),bindings,Screen.PrimaryScreen?.DeviceName);
    }

    private async Task RunPresetOperation(string status,Func<CancellationToken,Task> operation,bool cancellable=true)
    {
        if(presetBusy||libraryFileBusy)return;
        presetBusy=true;presetCancellation=new();SetPresetStatus(status);
        presetCancel.Visibility=cancellable?Visibility.Visible:Visibility.Collapsed;presetCancel.IsEnabled=true;
        UpdatePresetControls();
        try {await operation(presetCancellation.Token);}
        catch(OperationCanceledException) {if(!libraryShutdown)SetPresetStatus("");}
        catch(Exception ex) {if(!libraryShutdown)SetPresetStatus(UiLanguage.Text("操作未完成：")+ex.Message);throw;}
        finally {
            presetCancellation.Dispose();presetCancellation=null;presetBusy=false;
            if(!libraryShutdown) {presetCancel.Visibility=Visibility.Collapsed;UpdatePresetControls();RefreshPresetCards();}
        }
    }

    private Task SaveCurrentPreset()=>RunPresetOperation(UiLanguage.Text("正在保存预设…"),async token=> {
        var document=CaptureCurrentPreset();
        await Task.Run(()=>modules.Presets.SaveAsync(document,token),token);
        if(!libraryShutdown)SetPresetStatus("");
    });

    private async Task<string?> PickPresetExportPath(string name)
    {
        string fileName=string.Concat(name.Select(c=>Path.GetInvalidFileNameChars().Contains(c)?'_':c));
        var picker=new FileSavePicker {SuggestedFileName="ZZZwallpaper-"+fileName,SuggestedStartLocation=PickerLocationId.Desktop};
        picker.FileTypeChoices.Add(UiLanguage.Text("预设压缩包"),new List<string> {".zip"});
        WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(this));
        return (await picker.PickSaveFileAsync())?.Path;
    }

    private Task ExportCurrentPreset()=>RunPresetOperation(UiLanguage.Text("请选择 ZIP 保存位置…"),async token=> {
        var path=await PickPresetExportPath(presetName.Text.Trim().Length==0?UiLanguage.Text("我的预设"):presetName.Text.Trim());
        token.ThrowIfCancellationRequested();
        if(path==null) {SetPresetStatus("");return;}
        var document=CaptureCurrentPreset();SetPresetStatus(UiLanguage.Text("正在导出…"));
        await Task.Run(()=>modules.Presets.ExportAsync(document,path,token),token);
        if(!libraryShutdown)SetPresetStatus("");
    });

    private Task ExportSavedPreset(PresetSummary entry)=>RunPresetOperation(UiLanguage.Text("请选择 ZIP 保存位置…"),async token=> {
        var path=await PickPresetExportPath(entry.Name);token.ThrowIfCancellationRequested();
        if(path==null) {SetPresetStatus("");return;}
        SetPresetStatus(UiLanguage.Text("正在导出…"));await Task.Run(()=>modules.Presets.ExportAsync(entry.Id,path,token),token);
        if(!libraryShutdown)SetPresetStatus("");
    });

    private Task ImportPreset()=>RunPresetOperation(UiLanguage.Text("请选择预设 ZIP…"),async token=> {
        var picker=new FileOpenPicker {SuggestedStartLocation=PickerLocationId.Downloads};picker.FileTypeFilter.Add(".zip");
        WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(this));
        var file=await picker.PickSingleFileAsync();token.ThrowIfCancellationRequested();
        if(file==null) {SetPresetStatus("");return;}
        await ImportPresetFile(file.Path,token);
    });

    private Task ImportPresetPath(string path)=>RunPresetOperation(UiLanguage.Text("正在导入…"),token=>ImportPresetFile(path,token));
    private async Task ImportPresetFile(string path,CancellationToken token)
    {
        SetPresetStatus(UiLanguage.Text("正在读取预设…"));
        var preview=await modules.Presets.PreviewPackageAsync(path,token);
        token.ThrowIfCancellationRequested();
        string? directory=null;
        if(preview.MediaCount>0) {
            directory=await ConfirmPresetMediaDirectory(preview.Name,preview.MediaCount,preview.MediaBytes,InitialPresetMediaDirectory(path),token);
            if(directory==null) {SetPresetStatus("");return;}
        }
        var options=new PresetImportOptions {MediaDirectory=directory,ExistingAssets=modules.Library.Assets().ToArray(),ExpectedFingerprint=preview.Fingerprint};
        SetPresetStatus(UiLanguage.Text("正在导入…"));
        await Task.Run(()=>modules.Presets.ImportAsync(path,options,token),token);
        if(!libraryShutdown) {
            string status="";
            if(directory!=null)try {modules.Settings.Set(PresetMediaDirectorySetting,directory);}
            catch(Exception ex) {
                AppLog.Warning("Presets","Import committed; could not remember the selected media directory",ex);
                status=UiLanguage.Text("预设已导入，未能记住素材保存位置。");
            }
            SetPresetStatus(status);
        }
    }

    private Task ApplyPreset(PresetSummary entry)=>RunPresetOperation(UiLanguage.Text("正在读取预设…"),async token=> {
        EndEffectEditing();widgets?.FlushPendingSave();FlushHistoryEdits();
        string revision=store.ConfigurationRevision();
        var existing=modules.Library.Assets().ToArray();
        var document=await modules.Presets.PrepareApplyAsync(entry.Id,existing,token);
        var content=PresetPanel();
        content.Children.Add(UtilityText(PresetCategoryText(document.Selection)));
        var locals=Screen.AllScreens.Select(screen=>new PresetScreenChoice(screen.DeviceName,new ScreenRow(screen).ToString())).ToArray();
        var mappings=new Dictionary<string,ComboBox>(StringComparer.Ordinal);var assigned=new HashSet<string>(StringComparer.Ordinal);
        foreach(string source in document.Screens) {
            var choices=new[]{new PresetScreenChoice(null,UiLanguage.Text("跳过此显示器"))}.Concat(locals).ToArray();
            var picker=new ComboBox {Style=HomeStyle("UtilityComboBox"),ItemsSource=choices,HorizontalAlignment=HorizontalAlignment.Stretch};
            var target=locals.FirstOrDefault(s=>s.Id==source&&!assigned.Contains(s.Id!))??locals.FirstOrDefault(s=>!assigned.Contains(s.Id!));
            picker.SelectedItem=target??choices[0];if(target?.Id is { } id)assigned.Add(id);
            content.Children.Add(WidgetField(UiLanguage.Text("预设显示器 ")+source.Replace(@"\\.\DISPLAY",""),picker));mappings[source]=picker;
        }
        var scroll=new ScrollViewer {Content=content,MaxHeight=Math.Max(180,Root.ActualHeight-230),VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        var dialog=new ContentDialog {XamlRoot=Root.XamlRoot,Title=UiLanguage.Format($"应用“{entry.Name}”？"),Content=scroll,PrimaryButtonText=UiLanguage.Text("应用预设"),CloseButtonText=UiLanguage.Text("取消"),DefaultButton=ContentDialogButton.Close,Style=HomeStyle("LibraryDialog")};
        if(await dialog.ShowAsync()!=ContentDialogResult.Primary||libraryShutdown) {SetPresetStatus("");return;}
        var map=mappings.Where(p=>(p.Value.SelectedItem as PresetScreenChoice)?.Id!=null).ToDictionary(p=>p.Key,p=>((PresetScreenChoice)p.Value.SelectedItem).Id!,StringComparer.Ordinal);
        SetPresetStatus(UiLanguage.Text("正在应用预设…"));
        EndEffectEditing();widgets?.FlushPendingSave();
        var result=modules.Presets.Apply(document,map,expectedRevision:revision,primaryScreen:Screen.PrimaryScreen?.DeviceName);
        effectSession.ClearRetained();automationUndo.Clear();scopedEffectUndo.Clear();
        var warnings=await ActivatePreset(result);
        SetPresetStatus(warnings);
        RefreshPresetUndo();
    },cancellable:false);

    private async Task<string> ActivatePreset(PresetApplyResult result)
    {
        var warnings=new List<string>();
        if(result.WidgetsChanged)try {widgets?.ReloadHistoryState();ResetWidgetNavigation();RefreshWidgets();}catch(Exception ex) {warnings.Add(UiLanguage.Text("组件刷新失败：")+ex.Message);}
        if(result.LibraryChanged) {RefreshFolders();RefreshLibrary();}
        LoadEffectDraft(true);LoadPlaybackSettings();
        if(wallpapers!=null) {
            if(result.PlayingChanged) {
                var assets=modules.Library.Assets().ToDictionary(a=>a.Id);
                foreach(string screenId in result.Screens)try {
                    wallpapers.ReloadPlayback(screenId);wallpapers.ReloadVideoSound(screenId);
                    var binding=store.Bindings().FirstOrDefault(b=>b.ScreenId==screenId);
                    if(binding==null) {wallpapers.CancelWallpaper(screenId);continue;}
                    var screen=Screen.AllScreens.FirstOrDefault(s=>s.DeviceName==screenId);
                    if(screen==null||!assets.TryGetValue(binding.AssetId,out var asset))throw new IOException(UiLanguage.Text("显示器或壁纸素材已不可用，请重新应用预设。"));
                    await wallpapers.Apply(asset,screen,binding.Fill,binding.UserPaused,saveProfile:false);
                } catch(Exception ex) {
                    // A failed replacement must not let the previous player
                    // rewrite the committed preset binding during later updates.
                    try {wallpapers.CancelWallpaper(screenId,preserveBinding:true);}catch(Exception stopError) {AppLog.Warning("Presets","Could not stop stale wallpaper",stopError);}
                    warnings.Add(UiLanguage.Text("壁纸同步失败：")+ex.Message);
                }
            }
            if(result.EffectsChanged)try {await wallpapers.ActivatePresetEffects(result.Screens);}catch(Exception ex) {warnings.Add(UiLanguage.Text("画面效果同步失败：")+ex.Message);}
        }
        RefreshStatus();return warnings.Count==0?"":UiLanguage.Text("配置已保存；")+string.Join("；",warnings);
    }

    private Task DeletePreset(PresetSummary entry)=>RunPresetOperation(UiLanguage.Text("请选择是否删除预设…"),async token=> {
        var dialog=new ContentDialog {XamlRoot=Root.XamlRoot,Title=UiLanguage.Format($"删除预设“{entry.Name}”？"),PrimaryButtonText=UiLanguage.Text("删除预设"),CloseButtonText=UiLanguage.Text("取消"),DefaultButton=ContentDialogButton.Close,Style=HomeStyle("LibraryDialog")};
        if(await dialog.ShowAsync()!=ContentDialogResult.Primary||libraryShutdown) {SetPresetStatus("");return;}
        SetPresetStatus(UiLanguage.Text("正在删除预设…"));
        await Task.Run(()=>modules.Presets.Delete(entry.Id),token);
        if(!libraryShutdown)SetPresetStatus("");
    },cancellable:false);
}

