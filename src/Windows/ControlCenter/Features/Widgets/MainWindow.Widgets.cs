using LumaWall.Contracts;
using LumaWall.WidgetSupport;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Screen=System.Windows.Forms.Screen;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private WidgetController? widgets;
    private bool applyingWidgetLive,syncingWidgetEditor;
    private string? selectedWidgetId,widgetDetailKey;
    private string? selectedLayoutGroupId;
    private string widgetOverviewKey="";
    private int widgetEditorEpoch;
    private Action? syncWidgetEditor;
    private TextBlock? widgetSaveStatus;
    public void ConnectWidgets(WidgetController controller) {widgets=controller;controller.Changed+=RefreshWidgets;RefreshWidgets();}
    private static Button WidgetButton(string text,Action action,string? icon=null,bool primary=false,bool iconOnly=false)
    {
        var button=new Button {Style=HomeStyle(primary?"UtilityPrimaryButton":"UtilityButton"),HorizontalAlignment=HorizontalAlignment.Stretch};
        if(iconOnly) {button.Style=HomeStyle("HomeIconButton");button.Width=button.Height=44;}
        UtilityButtonContent(button,text,icon,iconOnly);button.Click+=(_,_)=>action();return button;
    }
    private void WidgetEdit(Action action) {try {action();}catch(Exception ex) {ShowError(ex.Message,ex);}}
    private static string WidgetTitle(WidgetKind kind)=>kind==WidgetKind.Extension?UiLanguage.Text("扩展组件"):UiLanguage.Text(ExpansionCatalog.Widget(kind).Title);
    private static string WidgetTitle(WidgetInstance widget)=>widget.Kind==WidgetKind.Extension?FeaturePacks.Widgets.FirstOrDefault(x=>x.Key==widget.PackageWidgetId)?.Entry.Title??UiLanguage.Text("扩展组件"):WidgetTitle(widget.Kind);
    private static string WidgetGlyph(WidgetKind kind)=>kind==WidgetKind.Extension?"Widgets":ExpansionCatalog.Widget(kind).Icon;
    private bool LiveWidget(string id,Func<WidgetInstance,WidgetInstance> update)
    {
        if(widgets==null||syncingWidgetEditor)return false;
        var current=widgets.State.Widgets.FirstOrDefault(w=>w.Id==id);if(current==null)return false;
        try {
            var next=update(current);if(next==current)return true;
            applyingWidgetLive=true;widgets.UpdateLive(widgets.State with {Widgets=widgets.State.Widgets.Select(w=>w.Id==id?next:w).ToArray()});
        } catch(Exception ex) {ShowError(ex.Message,ex);return false;}
        finally {applyingWidgetLive=false;}
        syncWidgetEditor?.Invoke();UpdateWidgetSaveStatus();return true;
    }
    private void UpdateWidgetSaveStatus() {if(widgetSaveStatus!=null&&widgets!=null)widgetSaveStatus.Text=widgets.SaveStatus;}
    private WidgetInstance NewWidget(WidgetKind kind,string? packageId=null)
    {
        var screen=SelectedScreen??WidgetPlacement.Resolve(null);
        var widget=new WidgetInstance {Kind=kind,PackageWidgetId=packageId,Screen=screen.DeviceName,Y=Math.Min(.8,.12+(widgets?.State.Widgets.Length??0)*.05)};
        if(TimeWidgets.IsTime(kind))widget=widget with {Visual=new() {Width=360,Height=360,FontSize=40,Background=true,FillOpacity=1},Clock=new() {ShowSeconds=false},Countdown=new() {Target=DateTimeOffset.Now.AddDays(7)}};
        if(FeaturePacks.GridFor(widget) is {} grid)widget=widget with {GridOptions=grid.Options(widget)};
        var size=WidgetPlacement.Measure(widget,screen);var area=screen.WorkingArea;
        return widget with {Width=Math.Clamp(size.Width/(double)area.Width,.05,1),Height=Math.Clamp(size.Height/(double)area.Height,.03,1)};
    }
    private void AddWidget(WidgetKind kind,string? groupId=null,string? packageId=null)=>WidgetEdit(()=> {
        if(widgets==null)return;
        RememberNavigation();
        int order=groupId==null?0:widgets.State.Widgets.Where(w=>w.LayoutGroupId==groupId).Select(w=>w.LayoutOrder).DefaultIfEmpty(-1).Max()+1;
        var added=NewWidget(kind,packageId) with {LayoutGroupId=groupId,LayoutOrder=order};CloseWidgetDrawer(false);selectedWidgetId=added.Id;selectedLayoutGroupId=null;widgetDetailKey=null;
        widgets.Save(widgets.State with {Widgets=[..widgets.State.Widgets,added]});WidgetsScroll.ChangeView(null,0,null,true);
    });
    private void ResetWidgetNavigation()
    {
        CaptureWidgetDisclosures();activeWidgetDisclosure=null;widgetLayerInputs.Clear();CloseWidgetDrawer(false);selectedWidgetId=selectedLayoutGroupId=null;widgetDetailKey=null;syncWidgetEditor=null;widgetSaveStatus=null;widgetOverviewKey="";
        focusAddAfterWidgetBack=false;WidgetsScroll.ChangeView(null,0,null,true);
    }
    private void WidgetBack()
    {
        CommitSpectrumInput();RememberNavigation();
        widgets?.FlushPendingSave();ResetWidgetNavigation();focusAddAfterWidgetBack=true;RefreshWidgets();
    }
    private void OpenWidget(string id)
    {
        CommitSpectrumInput();if(selectedWidgetId!=id)RememberNavigation();
        widgets?.FlushPendingSave();CloseWidgetDrawer(false);selectedWidgetId=id;selectedLayoutGroupId=null;widgetDetailKey=null;RefreshWidgets();WidgetsScroll.ChangeView(null,0,null,true);
    }
    public Task EditWidgetCard(string id) {if(id.StartsWith("group:",StringComparison.Ordinal))OpenWidgetGroup(id[6..]);else if(widgets?.State.Widgets.Any(w=>w.Id==id&&ExpansionCatalog.WidgetAvailable(w))==true)OpenWidget(id);return Task.CompletedTask;}
    private void RenderWidgetPage()
    {
        if(widgets==null)return;UpdateWidgetSaveStatus();if(applyingWidgetLive)return;
        var state=ExpansionCatalog.AvailableWidgets(widgets.State);var selected=state.Widgets.FirstOrDefault(w=>w.Id==selectedWidgetId);
        var selectedGroup=state.LayoutGroups.FirstOrDefault(g=>g.Id==selectedLayoutGroupId);
        if(selectedLayoutGroupId!=null&&selectedGroup==null)ResetWidgetNavigation();
        if(selectedGroup!=null) {
            WidgetsOverview.Visibility=Visibility.Collapsed;WidgetEditorPanel.Visibility=Visibility.Visible;
            if(widgetDetailKey!="group:"+selectedGroup.Id) {
                CloseWidgetDrawer(false);widgetDetailKey="group:"+selectedGroup.Id;WidgetsHeaderActions.Children.Clear();WidgetsHeaderActions.Children.Add(WidgetButton(UiLanguage.Text("返回组件"),WidgetBack,"Back"));
                CaptureWidgetDisclosures();widgetEditorEpoch++;WidgetEditorPanel.Children.Clear();BuildWidgetGroupEditor(selectedGroup,state);RestoreWidgetDisclosures("group:"+selectedGroup.Id);
            }
            syncWidgetEditor?.Invoke();UpdateWidgetSaveStatus();LayoutWidgetCards();return;
        }
        if(selectedWidgetId!=null&&selected==null)ResetWidgetNavigation();
        bool detail=selected!=null;WidgetsOverview.Visibility=detail?Visibility.Collapsed:Visibility.Visible;WidgetEditorPanel.Visibility=detail?Visibility.Visible:Visibility.Collapsed;
        if(detail) {
            // A ready notification or live property change updates values in place.
            // It never replaces a captured drag, focused TextBox, or open drawer.
            if(widgetDetailKey!=selected!.Id) {
                CloseWidgetDrawer(false);widgetDetailKey=selected.Id;WidgetsHeaderActions.Children.Clear();
                WidgetsHeaderActions.Children.Add(selected.LayoutGroupId is { } parent?WidgetButton(UiLanguage.Text("返回组件组"),()=>OpenWidgetGroup(parent),"Back"):WidgetButton(UiLanguage.Text("返回组件"),WidgetBack,"Back"));
                string removingId=selected.Id;WidgetsHeaderActions.Children.Add(WidgetButton(UiLanguage.Text("移除组件"),()=>RemoveWidget(removingId),"Trash"));
                CaptureWidgetDisclosures();widgetEditorEpoch++;WidgetEditorPanel.Children.Clear();BuildWidgetEditor(selected,state);RestoreWidgetDisclosures(selected.Id);
            }
            syncWidgetEditor?.Invoke();UpdateWidgetSaveStatus();
        } else {
            if(WidgetEditorPanel.Children.Count!=0)WidgetEditorPanel.Children.Clear();
            string key=(state with {Widgets=state.Widgets.Select(w=>w with {Layer=0}).ToArray()}).ToJson();if(key!=widgetOverviewKey) {
                widgetOverviewKey=key;WidgetsHeaderActions.Children.Clear();WidgetsHeaderActions.Children.Add(WidgetButton(UiLanguage.Text("重新显示"),()=>widgets.Restart(),"Restart"));
                widgetLayerInputs.Clear();WidgetListPanel.Children.Clear();WidgetCountText.Text=UiLanguage.Format($"我的组件 · {state.Widgets.Length} 个组件 · {state.LayoutGroups.Length} 个组");
                if(state.Widgets.Length==0&&state.LayoutGroups.Length==0)WidgetListPanel.Children.Add(UtilityCard(UtilityText(UiLanguage.Text("暂无组件"))));
                foreach(var group in state.LayoutGroups) {
                    var button=WidgetButton(group.Name,()=>OpenWidgetGroup(group.Id),"Widgets");button.HorizontalContentAlignment=HorizontalAlignment.Stretch;
                    var row=new Grid {ColumnSpacing=16};row.ColumnDefinitions.Add(new() {Width=new GridLength(32)});row.ColumnDefinitions.Add(new());
                    var icon=UtilityIcon("Widgets",button,32);SetOverviewIconActive(icon,state.Widgets.Any(w=>w.LayoutGroupId==group.Id&&w.Enabled));row.Children.Add(icon);
                    var label=UtilityText(group.Name,"UtilityHeading");label.VerticalAlignment=VerticalAlignment.Center;label.TextWrapping=TextWrapping.NoWrap;label.TextTrimming=TextTrimming.CharacterEllipsis;Grid.SetColumn(label,1);row.Children.Add(label);button.Content=row;
                    AutomationProperties.SetItemStatus(button,UiLanguage.Format($"组件组 · {state.Widgets.Count(w=>w.LayoutGroupId==group.Id)} 个组件 · {GroupFlowLabel(group.Flow)}"));
                    WidgetListPanel.Children.Add(WidgetGroupCard(button,group));
                }
                foreach(var widget in state.Widgets.Where(w=>w.LayoutGroupId==null)) {
                    var card=state.Shortcuts.FirstOrDefault(c=>c.Id==widget.ShortcutId);string name=widget.Kind==WidgetKind.Shortcuts?card?.Name??UiLanguage.Text("待设置卡片"):WidgetTitle(widget);
                    var button=WidgetButton(name,()=>OpenWidget(widget.Id));button.HorizontalContentAlignment=HorizontalAlignment.Stretch;
                    var row=new Grid {ColumnSpacing=16};row.ColumnDefinitions.Add(new() {Width=new GridLength(32)});row.ColumnDefinitions.Add(new());
                    row.Children.Add(WidgetImage(widget.Icon??card?.Icon,WidgetGlyph(widget.Kind),button,32,overview:true,active:widget.Enabled));
                    var title=UtilityText(name,"UtilityHeading");title.VerticalAlignment=VerticalAlignment.Center;title.TextWrapping=TextWrapping.NoWrap;title.TextTrimming=TextTrimming.CharacterEllipsis;
                    Grid.SetColumn(title,1);row.Children.Add(title);button.Content=row;
                    AutomationProperties.SetItemStatus(button,WidgetTitle(widget)+" · "+(widget.Enabled?UiLanguage.Text("已显示"):UiLanguage.Text("已隐藏")));
                    WidgetListPanel.Children.Add(WidgetLayerCard(button,widget));
                }
            }
        }
        SyncWidgetLayers();LayoutWidgetCards();
    }
    private static FrameworkElement WidgetImage(string? encoded,string fallback,DependencyObject source,double size=20,bool overview=false,bool active=false)
    {
        if(encoded!=null) {var image=new Image {Width=size,Height=size,Stretch=Stretch.Uniform};_=LoadWidgetImage(image,encoded);return image;}
        var icon=UtilityIcon(fallback,source,size);if(overview)SetOverviewIconActive(icon,active);return icon;
    }
    private static async Task LoadWidgetImage(Image image,string encoded)
    {
        try {using var stream=new InMemoryRandomAccessStream();using(var writer=new DataWriter(stream)) {writer.WriteBytes(Convert.FromBase64String(encoded));await writer.StoreAsync();writer.DetachStream();}
            stream.Seek(0);var bitmap=new BitmapImage();await bitmap.SetSourceAsync(stream);image.Source=bitmap;
        } catch { }
    }
    private static StackPanel WidgetField(string title,UIElement control)
    {
        var field=new StackPanel {Spacing=ParameterUi.LabelGap};var label=UtilityText(title,"UtilityLabel");field.Children.Add(label);field.Children.Add(control);
        if(control is ScrubNumberInput number)number.AttachDragHandle(label);
        return field;
    }
    private static ToggleSwitch WidgetToggle(string name,bool value)
    {
        var toggle=new ToggleSwitch {Style=HomeStyle("EffectsToggleSwitch"),IsOn=value,OnContent="",OffContent="",Width=40,MinWidth=40,HorizontalAlignment=HorizontalAlignment.Right};
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(toggle,name);return toggle;
    }
    private static Grid WidgetToggleRow(string title,ToggleSwitch toggle)
    {
        var row=new Grid {ColumnSpacing=16,MinHeight=40};row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new() {Width=GridLength.Auto});
        var label=UtilityText(title,"UtilityLabel");label.VerticalAlignment=VerticalAlignment.Center;row.Children.Add(label);Grid.SetColumn(toggle,1);row.Children.Add(toggle);return row;
    }

    private async Task<string?> PickShortcutIcon()
    {
        var picker=new FileOpenPicker();foreach(var extension in new[]{".png",".jpg",".jpeg",".webp"})picker.FileTypeFilter.Add(extension);
        WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(this));var file=await picker.PickSingleFileAsync();if(file==null)return null;
        return await ReadWidgetIcon(file);
    }
    private static async Task<string> ReadWidgetIcon(StorageFile file)
    {
        using var input=await file.OpenAsync(FileAccessMode.Read);var decoder=await BitmapDecoder.CreateAsync(input);
        if(decoder.PixelWidth==0||decoder.PixelHeight==0||(ulong)decoder.PixelWidth*decoder.PixelHeight>16_000_000)throw new ArgumentException(UiLanguage.Text("图标图片最多 1600 万像素"));
        double scale=Math.Min(64.0/decoder.PixelWidth,64.0/decoder.PixelHeight);uint width=Math.Max(1,(uint)Math.Round(decoder.PixelWidth*scale)),height=Math.Max(1,(uint)Math.Round(decoder.PixelHeight*scale));
        var pixels=await decoder.GetPixelDataAsync(BitmapPixelFormat.Rgba8,BitmapAlphaMode.Straight,new BitmapTransform {ScaledWidth=width,ScaledHeight=height,InterpolationMode=BitmapInterpolationMode.Fant},ExifOrientationMode.IgnoreExifOrientation,ColorManagementMode.ColorManageToSRgb);
        using var output=new InMemoryRandomAccessStream();var encoder=await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId,output);encoder.SetPixelData(BitmapPixelFormat.Rgba8,BitmapAlphaMode.Straight,width,height,96,96,pixels.DetachPixelData());await encoder.FlushAsync();
        output.Seek(0);using var reader=new DataReader(output.GetInputStreamAt(0));await reader.LoadAsync((uint)output.Size);var bytes=new byte[(int)output.Size];reader.ReadBytes(bytes);return Convert.ToBase64String(bytes);
    }
}
