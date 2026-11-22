using System.ComponentModel;
using LumaWall.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace LumaWall.ControlCenter;

public sealed class AudioRulePickerRow(AudioParameter parameter,bool added) : INotifyPropertyChanged
{
    private bool selected,alreadyAdded=added;
    public AudioParameter Parameter {get;}=parameter;
    public string Name=>$"{Parameter.DisplayEffectName} · {Parameter.DisplayName}";
    public bool AlreadyAdded=>alreadyAdded;
    public bool CanSelect=>!alreadyAdded;
    public Visibility AddedVisibility=>alreadyAdded?Visibility.Visible:Visibility.Collapsed;
    public string AccessibleName=>Name+(alreadyAdded?UiLanguage.Text("，已添加"):"");
    public bool IsSelected {
        get=>alreadyAdded||selected;
        set {
            if(alreadyAdded||selected==value)return;
            selected=value;PropertyChanged?.Invoke(this,new(nameof(IsSelected)));SelectionChanged?.Invoke();
        }
    }
    internal bool PendingSelection=>selected&&!alreadyAdded;
    internal Action? SelectionChanged;
    public event PropertyChangedEventHandler? PropertyChanged;
    internal void RefreshAdded(bool value)
    {
        if(value==alreadyAdded)return;
        alreadyAdded=value;if(value)selected=false;
        PropertyChanged?.Invoke(this,new(null));
    }
}

public sealed record AudioRulePickerGroup(EffectKind? Effect,string Name,int Available,int Total)
{
    public string Description=>UiLanguage.Format($"{Total} 项");
    public string AccessibleName=>$"{Name}，{Description}";
}

public sealed partial class MainWindow
{
    private bool audioRulePickerOpen;

    private async void AddAudioRule()
    {
        if(audioRulePickerOpen||SelectedScreen is not { } screen)return;
        audioRulePickerOpen=true;
        OperationContext? operation=null;
        try {
            // Finish previous gestures before opening. This picker changes no draft until confirmed.
            FlushHistoryEdits();
            string? asset=effectTargetAssetId;
            operation=OperationTrace.Begin("display","audio_rule_picker","AudioReactive");
            RememberTrace(operation);lastUiOperation=operation;
            using var trace=OperationTrace.Enter(operation);
            AudioParameter[] selected=await PickAudioRuleParametersAsync(operation);
            if(selected.Length==0) {OperationTrace.Record(operation,"audio_rule_picker","cancelled");return;}
            if(libraryShutdown||SelectedScreen?.DeviceName!=screen.DeviceName||effectTargetAssetId!=asset) {
                OperationTrace.Record(operation,"audio_rule_picker","target_changed");
                if(!libraryShutdown)Inform(UiLanguage.Text("编辑对象已变化，请在当前对象中重新添加联动。"));return;
            }
            // Re-read current rules after the modal: external edits must not be overwritten.
            var rules=effectSession.ExtendedDraft.AudioRules;
            var additions=selected.Where(p=>!rules.Any(r=>r.Parameter==p.Key)).ToArray();
            if(additions.Length==0)return;
            if(rules.Length+additions.Length>128)throw new InvalidOperationException(UiLanguage.Text("音频联动最多支持 128 项，请减少选择后重试。"));
            var effects=DraftEffects;
            var next=rules.Concat(additions.Select(p=> {
                double maximum=AudioRulePickerMaximum(p);
                double current=Math.Clamp(p.Read(effects),p.Minimum,maximum),strong=Math.Clamp(current+(maximum-p.Minimum)*.2,p.Minimum,maximum);
                if(Math.Abs(strong-current)<.00001)strong=p.Minimum;
                return new AudioRule {Parameter=p.Key,Weak=p.Trigger?0:current,Strong=p.Trigger?1:strong,Response=p.Trigger?1:0,Release=p.Trigger&&p.Effect is EffectKind.Glitch or EffectKind.DotRipple or EffectKind.ZoomTunnel?.35:null,Band=Math.Max(0,AudioBandPicker.SelectedIndex)};
            })).OrderBy(r=>AudioParameters.Find(r.Parameter).Id).ToArray();
            AudioParameters.Validate(next);
            // One draft change/save and a separate gesture keep a batch as one undo entry.
            BeginHistoryGesture();
            try {
                effectSession.ExtendedDraft=effectSession.ExtendedDraft with {AudioRules=next};
                audioRuleToExpand=additions.Length==1?additions[0].Key:null;
                DraftChanged();FlushEffectChanges();RefreshAudioRules();
                OperationTrace.Record(operation,"audio_rules_add","submitted",$"count={additions.Length}; parameters={string.Join(",",additions.Select(p=>p.Key))}");
                var first=audioRuleList.Children.OfType<ContentControl>().FirstOrDefault(c=>c.Tag as string=="effect-fold:audio-rule:"+additions[0].Key);
                if(first!=null)DispatcherQueue.TryEnqueue(()=> {if(!libraryShutdown&&first.IsLoaded)first.StartBringIntoView(new BringIntoViewOptions {AnimationDesired=false});});
            } finally {store.BreakUndoGesture();}
        } catch(Exception error) {OperationTrace.Fault(operation,"audio_rules_add",error.ToString());ShowError(UiLanguage.Text("添加效果联动失败：")+error.Message,error);}
        finally {audioRulePickerOpen=false;if(!libraryShutdown)audioAddRuleButton.Focus(FocusState.Programmatic);}
    }

    private double AudioRulePickerMaximum(AudioParameter parameter)=>parameter.Key=="RgbOffset"?Math.Min(parameter.Maximum,RgbSlider.Maximum/effectRgbScale):parameter.Maximum;

    private async Task<AudioParameter[]> PickAudioRuleParametersAsync(OperationContext operation)
    {
        var existing=effectSession.ExtendedDraft.AudioRules.Select(r=>r.Parameter).ToHashSet(StringComparer.Ordinal);
        var rows=AudioParameters.Selectable.Select(p=>new AudioRulePickerRow(p,existing.Contains(p.Key))).ToArray();
        var body=new Grid {RowSpacing=12};
        body.RowDefinitions.Add(new() {Height=GridLength.Auto});
        body.RowDefinitions.Add(new() {Height=new GridLength(1,GridUnitType.Star)});
        body.RowDefinitions.Add(new() {Height=GridLength.Auto});
        controlTraces.GetOrCreateValue(body).Operation=operation;
        var search=new TextBox {Style=HomeStyle("UtilityTextBox"),PlaceholderText=UiLanguage.Text("搜索效果或参数"),MaxLength=120,Padding=new Thickness(42,8,12,8)};
        DoubleClickEdit.SetEnabled(search,false);
        AutomationProperties.SetName(search,UiLanguage.Text("搜索联动效果或参数"));AutomationProperties.SetAutomationId(search,"AudioRulePickerSearch");var searchHost=new Grid();searchHost.Children.Add(search);var searchIcon=UtilityIcon("Search",search,16);searchIcon.HorizontalAlignment=HorizontalAlignment.Left;searchIcon.VerticalAlignment=VerticalAlignment.Center;searchIcon.Margin=new Thickness(16,0,0,0);searchIcon.IsHitTestVisible=false;searchHost.Children.Add(searchIcon);body.Children.Add(searchHost);

        var columns=new Grid {ColumnSpacing=16};columns.ColumnDefinitions.Add(new() {Width=new GridLength(210)});columns.ColumnDefinitions.Add(new() {Width=new GridLength(1,GridUnitType.Star)});
        Grid.SetRow(columns,1);body.Children.Add(columns);
        ListView MakeList(string id,string template,Style itemStyle,ListViewSelectionMode selection)=>new() {
            Name=id,Style=HomeStyle("AudioRulePickerList"),ItemContainerStyle=itemStyle,
            ItemTemplate=(DataTemplate)Microsoft.UI.Xaml.Application.Current.Resources[template],
            SelectionMode=selection,IsItemClickEnabled=false,HorizontalAlignment=HorizontalAlignment.Stretch,VerticalAlignment=VerticalAlignment.Stretch
        };
        var categories=MakeList("AudioRulePickerEffects","AudioRulePickerGroupTemplate",HomeStyle("AudioRulePickerCategoryItem"),ListViewSelectionMode.Single);
        var parameters=MakeList("AudioRulePickerParameters","AudioRulePickerParameterTemplate",HomeStyle("AudioRulePickerParameterItem"),ListViewSelectionMode.None);
        AutomationProperties.SetName(categories,UiLanguage.Text("联动效果分类"));AutomationProperties.SetAutomationId(categories,"AudioRulePickerEffects");
        AutomationProperties.SetName(parameters,UiLanguage.Text("可多选的联动参数"));AutomationProperties.SetAutomationId(parameters,"AudioRulePickerParameters");
        var resultTitle=UtilityText(UiLanguage.Text("全部联动"),"UtilityHeading");
        var empty=UtilityText(UiLanguage.Text("无匹配项"));empty.TextWrapping=TextWrapping.Wrap;empty.HorizontalAlignment=HorizontalAlignment.Center;empty.VerticalAlignment=VerticalAlignment.Center;empty.Margin=new Thickness(20);empty.IsHitTestVisible=false;
        var parameterHost=new Grid();parameterHost.Children.Add(parameters);parameterHost.Children.Add(empty);
        Grid Panel(string title,FrameworkElement list,TextBlock? heading=null) {
            var panel=new Grid {RowSpacing=10};panel.RowDefinitions.Add(new() {Height=GridLength.Auto});panel.RowDefinitions.Add(new() {Height=new GridLength(1,GridUnitType.Star)});
            panel.Children.Add(heading??UtilityText(title,"UtilityHeading"));Grid.SetRow(list,1);panel.Children.Add(list);return panel;
        }
        Border Region(FrameworkElement content)=>new() {Child=content,Padding=new Thickness(12),CornerRadius=new CornerRadius(16),BorderThickness=new Thickness(2),BorderBrush=HomeBrush("HomeFrameEdgeHoverBrush"),Background=HomeBrush("HomeControlRecess")};
        columns.Children.Add(Region(Panel(UiLanguage.Text("效果"),categories)));var right=Region(Panel(UiLanguage.Text("联动"),parameterHost,resultTitle));Grid.SetColumn(right,1);columns.Children.Add(right);
        var footer=new Grid {ColumnSpacing=12};footer.ColumnDefinitions.Add(new() {Width=new GridLength(1,GridUnitType.Star)});footer.ColumnDefinitions.Add(new() {Width=GridLength.Auto});
        var summary=UtilityText(UiLanguage.Text("已选 0 项"));summary.TextWrapping=TextWrapping.Wrap;summary.VerticalAlignment=VerticalAlignment.Center;summary.MaxLines=2;footer.Children.Add(summary);
        var clear=WidgetButton(UiLanguage.Text("清空选择"),()=> {foreach(var row in rows)row.IsSelected=false;});Grid.SetColumn(clear,1);footer.Children.Add(clear);Grid.SetRow(footer,2);body.Children.Add(footer);
        var dialog=new ContentDialog {XamlRoot=Root.XamlRoot,Title=UiLanguage.Text("添加效果联动"),Content=body,PrimaryButtonText=UiLanguage.Text("添加联动"),CloseButtonText=UiLanguage.Text("取消"),DefaultButton=ContentDialogButton.None,IsPrimaryButtonEnabled=false,Style=HomeStyle("LibraryDialog")};
        AutomationProperties.SetAutomationId(dialog,"AudioRulePicker");dialog.Resources["ContentDialogMaxWidth"]=960d;
        AudioRulePickerRow[] visible=[];EffectKind? selectedEffect=null;bool refreshing=false,closed=false;
        void RefreshSummary() {
            int count=rows.Count(r=>r.PendingSelection),shown=visible.Count(r=>r.PendingSelection),added=rows.Count(r=>r.AlreadyAdded),remaining=Math.Max(0,128-added);
            dialog.PrimaryButtonText=count==0?UiLanguage.Text("添加联动"):UiLanguage.Format($"添加 {count} 项联动");dialog.IsPrimaryButtonEnabled=count>0&&count<=remaining;clear.IsEnabled=count>0;
            summary.Text=count>remaining?UiLanguage.Format($"可添加 {remaining} 项 · 已选 {count} 项"):count==0?UiLanguage.Format($"已选 0 项 · 已添加 {added} 项"):UiLanguage.Format($"已选 {count} 项")+(shown<count?UiLanguage.Format($" · 隐藏已选 {count-shown} 项"):"")+UiLanguage.Format($" · 已添加 {added} 项");
        }
        void RefreshParameters(AudioRulePickerRow[] matches) {
            visible=matches.Where(r=>selectedEffect==null||r.Parameter.Effect==selectedEffect).ToArray();parameters.ItemsSource=visible;
            resultTitle.Text=(selectedEffect is { } kind?rows.First(r=>r.Parameter.Effect==kind).Parameter.DisplayEffectName:UiLanguage.Text("全部联动"))+$" · {visible.Length}";
            empty.Visibility=visible.Length==0?Visibility.Visible:Visibility.Collapsed;RefreshSummary();
        }
        AudioRulePickerRow[] Matches() {
            var tokens=search.Text.Trim().Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);
            return rows.Where(r=>tokens.All(token=>(r.Parameter.DisplayEffectName+" "+r.Name+" "+r.Parameter.Key).Contains(token,StringComparison.OrdinalIgnoreCase))).ToArray();
        }
        void RefreshFilter(bool resetCategory) {
            if(closed)return;
            if(resetCategory)selectedEffect=null;
            var matches=Matches();
            var groups=new[]{new AudioRulePickerGroup(null,UiLanguage.Text("全部效果"),matches.Count(r=>!r.AlreadyAdded),matches.Length)}.Concat(matches.GroupBy(r=>r.Parameter.Effect).Select(group=>new AudioRulePickerGroup(group.Key,group.First().Parameter.DisplayEffectName,group.Count(r=>!r.AlreadyAdded),group.Count()))).ToArray();
            refreshing=true;
            try {categories.ItemsSource=groups;categories.SelectedItem=groups.FirstOrDefault(g=>g.Effect==selectedEffect)??groups[0];selectedEffect=((AudioRulePickerGroup)categories.SelectedItem).Effect;}
            finally {refreshing=false;}
            RefreshParameters(matches);
        }
        foreach(var row in rows)row.SelectionChanged=()=> {RefreshSummary();OperationTrace.Record(operation,"audio_rule_picker_selection","changed",$"selected={rows.Count(r=>r.PendingSelection)}",sampledEvent:true);};
        categories.SelectionChanged+=(_,_)=> {if(refreshing||closed||categories.SelectedItem is not AudioRulePickerGroup group)return;selectedEffect=group.Effect;RefreshParameters(Matches());};
        var debounce=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(180)};
        debounce.Tick+=(_,_)=> {debounce.Stop();RefreshFilter(true);};
        search.TextChanged+=(_,_)=> {debounce.Stop();if(closed)return;if(string.IsNullOrWhiteSpace(search.Text))RefreshFilter(true);else debounce.Start();};
        search.KeyDown+=(_,e)=> {if(e.Key==Windows.System.VirtualKey.Enter) {e.Handled=true;debounce.Stop();RefreshFilter(true);}};
        dialog.PrimaryButtonClick+=(_,args)=> {
            var current=effectSession.ExtendedDraft.AudioRules.Select(r=>r.Parameter).ToHashSet(StringComparer.Ordinal);
            foreach(var row in rows)row.RefreshAdded(current.Contains(row.Parameter.Key));
            RefreshFilter(false);if(!dialog.IsPrimaryButtonEnabled)args.Cancel=true;
        };
        void Resize() {
            // Bound both list viewports. Never measure the entire catalog in an outer scroll panel.
            body.Width=Math.Max(300,Math.Min(850,Root.ActualWidth-140));body.Height=Math.Max(220,Math.Min(550,Root.ActualHeight-260));
            columns.ColumnDefinitions[0].Width=new GridLength(body.Width<560?150:210);
            dialog.Resources["ContentDialogMaxHeight"]=Math.Max(340,Root.ActualHeight-40);
        }
        SizeChangedEventHandler resized=(_,_)=>Resize();
        Resize();RefreshFilter(false);Root.SizeChanged+=resized;
        dialog.Opened+=(_,_)=>search.Focus(FocusState.Programmatic);
        dialog.Closing+=(_,_)=>debounce.Stop();
        try {
            return await dialog.ShowAsync()==ContentDialogResult.Primary?rows.Where(r=>r.PendingSelection).Select(r=>r.Parameter).ToArray():[];
        } finally {
            closed=true;debounce.Stop();Root.SizeChanged-=resized;
            foreach(var row in rows)row.SelectionChanged=null;
            parameters.ItemsSource=categories.ItemsSource=null;dialog.Content=null;body.Children.Clear();
        }
    }
}
