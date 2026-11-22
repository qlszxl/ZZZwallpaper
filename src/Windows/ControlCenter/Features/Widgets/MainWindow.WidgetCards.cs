using LumaWall.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private FrameworkElement BuildWidgetCardFields(WidgetInstance widget,WidgetConfiguration initial)
    {
        string id=widget.Id;int epoch=widgetEditorEpoch;var old=initial.Shortcuts.FirstOrDefault(c=>c.Id==widget.ShortcutId);
        string cardId=old?.Id??Guid.NewGuid().ToString("N");
        var panel=new StackPanel {Spacing=16};panel.Children.Add(UtilityText(UiLanguage.Text("卡片内容"),"UtilityHeading"));
        var choices=new StackPanel {Spacing=4};
        string pickerLabel=UiLanguage.Text("使用已有卡片");
        var picker=new Expander {Style=HomeStyle("EffectsExpander"),Header=pickerLabel,
            Content=new ScrollViewer {Content=choices,MaxHeight=240,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,
                HorizontalScrollMode=ScrollMode.Disabled,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled}};
        panel.Children.Add(picker);
        picker.Expanding+=(_,_)=> {
            choices.Children.Clear();
            if(widgets==null)return;
            var state=widgets.State;
            var editing=state.Widgets.FirstOrDefault(w=>w.Id==id);
            string? selected=editing?.ShortcutId;
            var saved=state.Shortcuts.OrderBy(c=>c.Order).GroupBy(c=>c.ContentKey())
                .Select(copies=>copies.FirstOrDefault(c=>c.Id==selected)??copies.FirstOrDefault(c=>c.Group==editing?.Group)??copies.First()).ToArray();
            var repeatedNames=saved.GroupBy(c=>c.Name).Where(g=>g.Count()>1).Select(g=>g.Key).ToHashSet();
            foreach(var card in saved) {
                var button=WidgetButton(card.Name,()=> {
                    if(epoch!=widgetEditorEpoch||widgets.State.Shortcuts.All(c=>c.Id!=card.Id))return;
                    picker.IsExpanded=false;
                    if(card.Id==selected||!LiveWidget(id,w=>w with {ShortcutId=card.Id}))return;
                    widgetDetailKey=null;RefreshWidgets();
                });
                button.HorizontalAlignment=HorizontalAlignment.Stretch;button.HorizontalContentAlignment=HorizontalAlignment.Left;
                if(repeatedNames.Contains(card.Name)) {
                    var row=new Grid {ColumnSpacing=8};
                    row.ColumnDefinitions.Add(new() {Width=GridLength.Auto});row.ColumnDefinitions.Add(new());
                    row.Children.Add(WidgetImage(card.Icon,WidgetGlyph(WidgetKind.Shortcuts),button,20));
                    var detail=new StackPanel();
                    detail.Children.Add(UtilityText(card.Name,"UtilityLabel"));
                    string destination=card.Url;
                    if(card.Kind==ShortcutKind.Command) {
                        if(!string.IsNullOrEmpty(card.WorkingDirectory))destination+=" · "+card.WorkingDirectory;
                        if(card.ShowCommandWindow)destination+=" · "+UiLanguage.Text("显示命令窗口");
                    }
                    detail.Children.Add(UtilityText(destination));
                    Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button,card.Name+" · "+destination);
                    Grid.SetColumn(detail,1);row.Children.Add(detail);button.Content=row;
                }
                ChoiceSelection.SetIsSelected(button,card.Id==selected);
                choices.Children.Add(button);
            }
            if(choices.Children.Count==0)choices.Children.Add(UtilityText(UiLanguage.Text("暂无可选卡片")));
        };
        var name=new TextBox {Style=HomeStyle("UtilityTextBox"),Text=old?.Name??"",MaxLength=40,PlaceholderText=UiLanguage.Text("例如：工作文件夹")};
        var kind=new ComboBox {Style=HomeStyle("UtilityComboBox"),HorizontalAlignment=HorizontalAlignment.Stretch};
        foreach(var type in Enum.GetValues<ShortcutKind>())kind.Items.Add(new ComboBoxItem {Content=ShortcutTargets.Label(type),Tag=type});kind.SelectedIndex=(int)(old?.Kind??ShortcutKind.Website);
        var target=new TextBox {Style=HomeStyle("UtilityTextBox"),Text=old?.Url??"",MaxLength=4096};
        var targetLabel=UtilityText(UiLanguage.Text("网址"),"UtilityLabel");var targetField=new StackPanel {Spacing=8};targetField.Children.Add(targetLabel);targetField.Children.Add(target);
        var directory=new TextBox {Style=HomeStyle("UtilityTextBox"),Text=old?.WorkingDirectory??"",MaxLength=4096,PlaceholderText=UiLanguage.Text("留空使用用户目录")};
        var directoryField=WidgetField(UiLanguage.Text("工作目录（可选）"),directory);
        var showWindow=new CheckBox {Style=HomeStyle("LibraryCheckBox"),Content=UiLanguage.Text("显示命令窗口"),IsChecked=old?.ShowCommandWindow??false};
        var feedback=UtilityText("");feedback.Visibility=Visibility.Collapsed;
        var browse=WidgetButton(UiLanguage.Text("选择文件夹…"),()=>{},"Add");
        browse.Click+=async(_,_)=> {
            try {var picker=new FolderPicker();picker.FileTypeFilter.Add("*");WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(this));var folder=await picker.PickSingleFolderAsync();if(folder!=null&&selectedWidgetId==id&&epoch==widgetEditorEpoch){target.Text=folder.Path;Commit();}}
            catch(Exception ex) {AppLog.Error("MainWindow.WidgetCards","Operation failed",ex);feedback.Text=UiLanguage.Text("无法选择文件夹：")+ex.Message;feedback.Visibility=Visibility.Visible;}
        };
        ShortcutKind SelectedKind()=>kind.SelectedItem is ComboBoxItem {Tag:ShortcutKind type}?type:ShortcutKind.Website;
        void UpdateKind() {
            var type=SelectedKind();targetLabel.Text=type==ShortcutKind.Website?UiLanguage.Text("网址"):type==ShortcutKind.Folder?UiLanguage.Text("文件夹路径"):UiLanguage.Text("命令");
            target.PlaceholderText=type==ShortcutKind.Website?"https://example.com":type==ShortcutKind.Folder?UiLanguage.Text(@"C:\Users 或 \\server\share"):UiLanguage.Text("例如：notepad.exe");
            directoryField.Visibility=showWindow.Visibility=type==ShortcutKind.Command?Visibility.Visible:Visibility.Collapsed;
            browse.Visibility=type==ShortcutKind.Folder?Visibility.Visible:Visibility.Collapsed;
        }
        void Commit() {
            if(widgets==null||syncingWidgetEditor||selectedWidgetId!=id||widgetDetailKey!=id||epoch!=widgetEditorEpoch)return;
            var current=widgets.State.Widgets.FirstOrDefault(w=>w.Id==id);if(current==null)return;
            try {
                string label=name.Text.Trim();if(string.IsNullOrEmpty(label))throw new ArgumentException(UiLanguage.Text("请填写卡片名称。"));
                var type=SelectedKind();string destination=ShortcutTargets.Normalize(type,target.Text);
                string? working=type==ShortcutKind.Command&&!string.IsNullOrWhiteSpace(directory.Text)?ShortcutTargets.Folder(directory.Text):null;
                var previous=widgets.State.Shortcuts.FirstOrDefault(c=>c.Id==cardId);
                var card=new Shortcut(cardId,label,destination,previous?.Group??"common",previous?.Order??widgets.State.Shortcuts.Length,previous?.Icon,type,working,type==ShortcutKind.Command&&showWindow.IsChecked==true);
                if(card!=previous||current.ShortcutId!=cardId) {
                    var next=widgets.State with {Shortcuts=previous==null?[..widgets.State.Shortcuts,card]:widgets.State.Shortcuts.Select(c=>c.Id==cardId?card:c).ToArray(),Widgets=widgets.State.Widgets.Select(w=>w.Id==id?w with {ShortcutId=cardId}:w).ToArray()};
                    applyingWidgetLive=true;widgets.UpdateLive(next);
                }
                feedback.Text="";feedback.Visibility=Visibility.Collapsed;
            } catch(Exception ex) {AppLog.Error("MainWindow.WidgetCards","Operation failed",ex);feedback.Text=ex.Message;feedback.Visibility=Visibility.Visible;}
            finally {applyingWidgetLive=false;}
            syncWidgetEditor?.Invoke();UpdateWidgetSaveStatus();
        }
        panel.Children.Add(WidgetField(UiLanguage.Text("名称"),name));panel.Children.Add(WidgetField(UiLanguage.Text("点击卡片时"),kind));panel.Children.Add(targetField);panel.Children.Add(browse);panel.Children.Add(directoryField);panel.Children.Add(showWindow);panel.Children.Add(feedback);
        UpdateKind();DoubleClickEdit.OnAccepted(name,Commit);DoubleClickEdit.OnAccepted(target,Commit);DoubleClickEdit.OnAccepted(directory,Commit);showWindow.Checked+=(_,_)=>Commit();showWindow.Unchecked+=(_,_)=>Commit();kind.SelectionChanged+=(_,_)=> {UpdateKind();Commit();};
        return UtilityCard(panel,UiLanguage.Text("快捷卡片内容"));
    }
}
