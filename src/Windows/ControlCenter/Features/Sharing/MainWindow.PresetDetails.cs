using System.Globalization;
using LumaWall.Contracts;
using LumaWall.Contracts.Presets;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private sealed record PresetEffectTarget(string Name,VideoEffects Effects)
    {
        public override string ToString()=>Name;
    }

    private static string PresetNumber(double value,string unit="")=>value.ToString("0.##",CultureInfo.CurrentCulture)+unit;
    private static string PresetPercent(double value)=>PresetNumber(value*100,"%");
    private static string PresetScreen(string screen)=>UiLanguage.Text("显示器 ")+screen.Replace(@"\\.\DISPLAY","");
    private static string PresetFill(FillMode fill)=>fill==FillMode.Cover?UiLanguage.Text("铺满"):UiLanguage.Text("适应");
    private static bool ValidPresetName(string name)=>name.Trim().Length is >=1 and <=80&&!name.Trim().Any(char.IsControl);

    private void PresetDetailField(StackPanel panel,string label,string value)
    {
        var row=new Grid {ColumnSpacing=12};
        row.ColumnDefinitions.Add(new() {Width=new GridLength(104)});
        row.ColumnDefinitions.Add(new());
        var caption=UtilityText(label,"UtilityLabel");caption.TextWrapping=TextWrapping.Wrap;
        var text=UtilityText(string.IsNullOrEmpty(value)?"—":value);
        text.TextWrapping=TextWrapping.Wrap;text.IsTextSelectionEnabled=true;text.Opacity=1;
        AutomationProperties.SetName(text,label+"："+text.Text);
        row.Children.Add(caption);Grid.SetColumn(text,1);row.Children.Add(text);panel.Children.Add(row);
    }

    private Expander PresetDetailSection(string title,Func<UIElement> build,bool expanded=false)
    {
        var heading=UtilityText(title,"UtilityHeading");
        heading.TextTrimming=TextTrimming.CharacterEllipsis;heading.TextWrapping=TextWrapping.NoWrap;
        var section=new Expander {Style=HomeStyle("PresetDetailsExpander"),Header=heading};
        AutomationProperties.SetName(section,title);
        section.Expanding+=(_,_)=>section.Content??=build();
        section.Expanding+=(_,_)=>section.DispatcherQueue.TryEnqueue(()=>KeepPresetSectionVisible(section));
        if(expanded) {section.Content=build();section.IsExpanded=true;}
        return section;
    }

    private static void KeepPresetSectionVisible(Expander section)
    {
        if(!section.IsExpanded||!section.IsLoaded)return;
        for(var parent=VisualTreeHelper.GetParent(section);parent!=null;parent=VisualTreeHelper.GetParent(parent)) {
            if(parent is not ScrollViewer scroll)continue;
            section.UpdateLayout();
            double top=section.TransformToVisual(scroll).TransformPoint(new()).Y;
            // Reveal the heading and a short beginning, without jumping to the
            // end of a large component list or scrolling an already-visible row.
            double preview=Math.Min(160,section.ActualHeight);
            if(top<0||top+preview>scroll.ViewportHeight)
                scroll.ChangeView(null,Math.Clamp(scroll.VerticalOffset+top,0,scroll.ScrollableHeight),null,true);
            return;
        }
    }

    private Task ShowPresetDetails(PresetSummary entry)=>RunPresetOperation(UiLanguage.Text("正在读取预设…"),async token=> {
        var document=await Task.Run(()=>modules.Presets.Inspect(entry.Id,token),token);
        if(libraryShutdown)return;
        SetPresetStatus("");
        var body=PresetPanel();body.Spacing=12;body.Padding=new Thickness(0,0,8,0);
        var name=new TextBox {Style=HomeStyle("UtilityTextBox"),MaxLength=80,Text=document.Name};
        DoubleClickEdit.SetEnabled(name,false);
        AutomationProperties.SetName(name,UiLanguage.Text("预设名称"));presetDetailsName=name;
        body.Children.Add(WidgetField(UiLanguage.Text("名称"),name));
        var error=UtilityText("");error.Visibility=Visibility.Collapsed;body.Children.Add(error);
        bool opened=false;
        void Category(string title,bool included,Func<UIElement> content) {
            bool expand=included&&!opened;if(included)opened=true;
            body.Children.Add(PresetDetailSection(title,()=>included?content():UtilityText(UiLanguage.Text("未包含")),expand));
        }
        Category(UiLanguage.Text("正在播放"),document.Selection.Playing,()=>PresetPlayingDetails(document));
        Category(UiLanguage.Text("素材库"),document.Selection.Library,()=>PresetLibraryDetails(document));
        Category(UiLanguage.Text("画面效果"),document.Selection.Effects,()=>PresetEffectsDetails(document));
        Category(UiLanguage.Text("组件"),document.Selection.Widgets,()=>PresetWidgetsDetails(document));
        var scroll=new ScrollViewer {Style=HomeStyle("HomeScrollViewer"),Content=body,
            VerticalScrollBarVisibility=ScrollBarVisibility.Visible,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,
            HorizontalScrollMode=ScrollMode.Disabled,IsVerticalScrollChainingEnabled=false};
        // Size the viewport, not its content. A reserved scrollbar gutter and a
        // stable viewport keep both dialog edges still as nested sections open.
        void SizeViewport() {
            scroll.Width=Math.Max(240,Math.Min(600,Root.ActualWidth-140));
            scroll.Height=Math.Max(180,Math.Min(540,Root.ActualHeight-260));
        }
        SizeChangedEventHandler resized=(_,_)=>SizeViewport();
        SizeViewport();Root.SizeChanged+=resized;
        var dialog=new ContentDialog {Title=UiLanguage.Text("预设详情"),Content=scroll,XamlRoot=Root.XamlRoot,
            PrimaryButtonText=UiLanguage.Text("保存名称"),CloseButtonText=UiLanguage.Text("关闭"),DefaultButton=ContentDialogButton.Close,
            IsPrimaryButtonEnabled=false,Style=HomeStyle("LibraryDialog")};
        dialog.Resources["ContentDialogMaxWidth"]=700d;
        void ValidateName() {
            bool valid=ValidPresetName(name.Text);
            error.Text=valid?"":UiLanguage.Text("名称须为 1–80 个字符，不能包含控制字符。");
            error.Visibility=valid?Visibility.Collapsed:Visibility.Visible;
            dialog.IsPrimaryButtonEnabled=valid&&name.Text.Trim()!=document.Name;
        }
        name.TextChanged+=(_,_)=>ValidateName();
        dialog.PrimaryButtonClick+=(_,args)=> {ValidateName();if(!dialog.IsPrimaryButtonEnabled)args.Cancel=true;};
        presetDetailsDialog=dialog;
        ContentDialogResult result;
        try {result=await dialog.ShowAsync();}
        finally {Root.SizeChanged-=resized;presetDetailsDialog=null;presetDetailsName=null;scroll.Content=null;dialog.Content=null;}
        if(result!=ContentDialogResult.Primary||libraryShutdown)return;
        SetPresetStatus(UiLanguage.Text("正在保存名称…"));
        string renamed=name.Text.Trim();
        await Task.Run(()=>modules.Presets.RenameAsync(entry.Id,renamed,token),token);
        if(!libraryShutdown)SetPresetStatus("");
    },cancellable:false);

    private UIElement PresetPlayingDetails(PresetDocument document)
    {
        var panel=PresetPanel();panel.Spacing=10;
        var names=document.Assets.ToDictionary(asset=>asset.Id,asset=>asset.Name);
        string AssetName(string id)=>names.GetValueOrDefault(id,"—");
        foreach(string screen in document.Screens) {
            panel.Children.Add(UtilityText(PresetScreen(screen),"UtilityHeading"));
            var binding=document.Bindings.FirstOrDefault(item=>item.ScreenId==screen);
            PresetDetailField(panel,UiLanguage.Text("壁纸"),binding==null?UiLanguage.Text("无"):AssetName(binding.AssetId));
            if(binding!=null) {
                PresetDetailField(panel,UiLanguage.Text("填充"),PresetFill(binding.Fill));
                PresetDetailField(panel,UiLanguage.Text("播放状态"),binding.UserPaused?UiLanguage.Text("已暂停"):UiLanguage.Text("播放中"));
            }
            if(document.SoundEnabled.TryGetValue(screen,out bool sound))PresetDetailField(panel,UiLanguage.Text("声音"),sound?UiLanguage.Text("开启"):UiLanguage.Text("关闭"));
            if(document.Volumes.TryGetValue(screen,out int volume))PresetDetailField(panel,UiLanguage.Text("音量"),volume+"%");
            if(document.Playback.TryGetValue(screen,out var playback)) {
                PresetDetailField(panel,UiLanguage.Text("播放模式"),playback.Mode switch {
                    PlaybackMode.SingleLoop=>UiLanguage.Text("单个循环"),PlaybackMode.SingleOnce=>UiLanguage.Text("单次播放"),
                    PlaybackMode.Sequential=>UiLanguage.Text("顺序播放"),PlaybackMode.Shuffle=>UiLanguage.Text("随机播放"),_=>UiLanguage.Text("自动")});
                PresetDetailField(panel,UiLanguage.Text("切换条件"),playback.Trigger==PlaybackTrigger.Interval?PresetNumber(playback.IntervalSeconds,UiLanguage.Text(" 秒")):UiLanguage.Text("视频结束"));
                PresetDetailField(panel,UiLanguage.Text("切换效果"),playback.Transition==PlaybackTransition.CrossFade?UiLanguage.Text("淡入淡出"):UiLanguage.Text("直接切换"));
                PresetDetailField(panel,UiLanguage.Text("当前队列"),string.Join("、",playback.AssetIds.Select(AssetName)));
                foreach(var playlist in playback.CustomPlaylists??[])
                    panel.Children.Add(PresetDetailSection(playlist.Name,()=>PresetDetailList(playlist.AssetIds.Select(AssetName))));
            }
        }
        if(document.Screens.Length==0)panel.Children.Add(UtilityText(UiLanguage.Text("无")));
        return panel;
    }

    private ListView PresetDetailList(IEnumerable<string> lines)=>new() {
        ItemsSource=lines.ToArray(),SelectionMode=ListViewSelectionMode.None,IsItemClickEnabled=false,
        ItemContainerStyle=HomeStyle("LibraryQueueItem"),MaxHeight=260,MinHeight=40,
        HorizontalAlignment=HorizontalAlignment.Stretch
    };

    private UIElement PresetLibraryDetails(PresetDocument document)
    {
        var panel=PresetPanel();
        PresetDetailField(panel,UiLanguage.Text("素材数量"),document.Assets.Length.ToString(CultureInfo.CurrentCulture));
        if(document.Assets.Length>0)panel.Children.Add(PresetDetailList(document.Assets.Select(asset=>
            $"{asset.Name}  ·  {(asset.IsImage?UiLanguage.Text("图片"):UiLanguage.Text("视频"))}  ·  {asset.Width} × {asset.Height}  ·  {LibraryPresentation.FileSize(asset.Bytes)}")));
        return panel;
    }

    private UIElement PresetEffectsDetails(PresetDocument document)
    {
        var panel=PresetPanel();var targets=new List<PresetEffectTarget>();
        targets.AddRange(document.ScreenProfiles.Select(pair=>new PresetEffectTarget(PresetScreen(pair.Key),pair.Value.Effects)));
        targets.AddRange(document.AssetEffects.Select(pair=>new PresetEffectTarget(
            UiLanguage.Text("素材 · ")+(document.Assets.FirstOrDefault(asset=>asset.Id==pair.Key)?.Name??pair.Key),pair.Value.OwnEffects())));
        if(targets.Count==0) {panel.Children.Add(UtilityText(UiLanguage.Text("无")));return panel;}
        var picker=new ComboBox {Style=HomeStyle("UtilityComboBox"),ItemsSource=targets,HorizontalAlignment=HorizontalAlignment.Stretch};
        AutomationProperties.SetName(picker,UiLanguage.Text("效果作用对象"));panel.Children.Add(WidgetField(UiLanguage.Text("作用对象"),picker));
        var details=PresetPanel();panel.Children.Add(details);
        picker.SelectionChanged+=(_,_)=> {
            details.Children.Clear();
            if(picker.SelectedItem is not PresetEffectTarget target)return;
            var collection=target.Effects.GetCollection();
            var kinds=collection.OrderedKinds().Concat(new[]{EffectKind.HideTaskbar,EffectKind.AudioReactive,EffectKind.MaskLibrary})
                .Distinct().Where(kind=>(collection.Added&kind)!=0).ToArray();
            PresetDetailField(details,UiLanguage.Text("全局强度"),PresetPercent(target.Effects.Intensity));
            if(kinds.Length==0)details.Children.Add(UtilityText(UiLanguage.Text("无已添加效果")));
            foreach(var kind in kinds) {
                string label=effectDefinitions.FirstOrDefault(definition=>definition.Kind==kind)?.Name??kind.ToString();
                details.Children.Add(PresetDetailSection(label,()=>PresetEffectValues(target.Effects,kind),kinds.Length==1));
            }
        };
        picker.SelectedIndex=0;return panel;
    }

    private static bool PresetEffectEnabled(VideoEffects effects,EffectKind kind)
    {
        if(kind==EffectKind.Color)return effects.GetCollection().ColorEnabled;
        if(kind==EffectKind.HideTaskbar)return effects.GetCollection().HideTaskbarEnabled;
        if(kind==EffectKind.MaskLibrary)return effects.Masks.Length>0;
        var value=typeof(VideoEffects).GetProperty(kind.ToString())?.GetValue(effects);
        return value is bool flag?flag:value?.GetType().GetProperty("Enabled")?.GetValue(value) is true;
    }

    private UIElement PresetEffectValues(VideoEffects effects,EffectKind kind)
    {
        var panel=PresetPanel();panel.Spacing=8;
        PresetDetailField(panel,UiLanguage.Text("状态"),PresetEffectEnabled(effects,kind)?UiLanguage.Text("开启"):UiLanguage.Text("关闭"));
        if(kind!=EffectKind.MaskLibrary)PresetDetailField(panel,UiLanguage.Text("效果强度"),PresetPercent(effects.GetCollection().Strength(kind)));
        foreach(var parameter in AudioParameters.All.Where(parameter=>parameter.Effect==kind&&(kind!=EffectKind.Particles||effects.Particles.IsRain)))
            PresetDetailField(panel,parameter.DisplayName,PresetNumber(parameter.Read(effects)*parameter.DisplayScale,parameter.DisplayUnit));
        if(kind==EffectKind.Particles) {
            var particle=effects.Particles;
            PresetDetailField(panel,UiLanguage.Text("粒子数量"),particle.Count.ToString());
            PresetDetailField(panel,UiLanguage.Text("大小"),PresetNumber(particle.Size," px"));
            PresetDetailField(panel,UiLanguage.Text("速度"),PresetNumber(particle.Speed));
            PresetDetailField(panel,UiLanguage.Text("方向"),PresetNumber(particle.Direction,"°"));
            PresetDetailField(panel,UiLanguage.Text("透明度"),PresetPercent(particle.Opacity));
            PresetDetailField(panel,UiLanguage.Text("子粒子层"),particle.SubLayer.Enabled?UiLanguage.Text("开启"):UiLanguage.Text("关闭"));
            if(!string.IsNullOrEmpty(particle.ImageName))PresetDetailField(panel,UiLanguage.Text("图片"),particle.ImageName);
        }
        if(kind==EffectKind.AudioReactive) {
            PresetDetailField(panel,UiLanguage.Text("灵敏度"),PresetNumber(effects.AudioSensitivity));
            PresetDetailField(panel,UiLanguage.Text("平滑"),PresetNumber(effects.AudioSmoothing));
            foreach(var rule in effects.AudioRules) {
                var parameter=AudioParameters.Find(rule.Parameter);
                PresetDetailField(panel,parameter.DisplayEffectName+" · "+parameter.DisplayName,
                    (rule.Enabled?UiLanguage.Text("开启"):UiLanguage.Text("关闭"))+" · "+PresetNumber(rule.Weak*parameter.DisplayScale,parameter.DisplayUnit)+" → "+
                    PresetNumber(rule.Strong*parameter.DisplayScale,parameter.DisplayUnit));
            }
        }
        if(kind==EffectKind.MaskLibrary)foreach(var mask in effects.Masks)panel.Children.Add(UtilityText(mask.Name));
        return panel;
    }

    private UIElement PresetWidgetsDetails(PresetDocument document)
    {
        var panel=PresetPanel();
        PresetDetailField(panel,UiLanguage.Text("隐私内容"),document.Selection.IncludeWidgetPrivateData?UiLanguage.Text("包含"):UiLanguage.Text("未包含"));
        var state=document.Widgets??new();
        PresetDetailField(panel,UiLanguage.Text("组件数量"),state.Widgets.Length.ToString());
        foreach(var group in state.LayoutGroups) {
            var fields=PresetPanel();
            PresetDetailField(fields,UiLanguage.Text("显示器"),PresetScreen(group.Screen));
            PresetDetailField(fields,UiLanguage.Text("位置"),$"{PresetPercent(group.X)} / {PresetPercent(group.Y)}");
            PresetDetailField(fields,UiLanguage.Text("排列"),group.Flow switch {WidgetGroupFlow.Horizontal=>UiLanguage.Text("横向"),WidgetGroupFlow.Vertical=>UiLanguage.Text("纵向"),_=>UiLanguage.Text("网格")});
            PresetDetailField(fields,UiLanguage.Text("组件组锁定"),group.Locked?UiLanguage.Text("是"):UiLanguage.Text("否"));
            panel.Children.Add(PresetDetailSection(group.Name,()=>fields));
        }
        foreach(var widget in state.Widgets.OrderBy(widget=>widget.LayoutOrder)) {
            panel.Children.Add(PresetDetailSection(WidgetDisplayName(widget,state),()=> {
                var fields=PresetPanel();fields.Spacing=8;
                PresetDetailField(fields,UiLanguage.Text("类型"),WidgetTitle(widget));
                PresetDetailField(fields,UiLanguage.Text("显示器"),PresetScreen(widget.Screen));
                PresetDetailField(fields,UiLanguage.Text("显示"),widget.Enabled?UiLanguage.Text("开启"):UiLanguage.Text("关闭"));
                PresetDetailField(fields,UiLanguage.Text("位置"),$"{PresetPercent(widget.X)} / {PresetPercent(widget.Y)}");
                PresetDetailField(fields,UiLanguage.Text("尺寸"),$"{PresetPercent(widget.Width)} × {PresetPercent(widget.Height)}");
                PresetDetailField(fields,UiLanguage.Text("透明度"),PresetPercent(widget.Opacity));
                PresetDetailField(fields,UiLanguage.Text("层级"),widget.Layer.ToString());
                PresetDetailField(fields,UiLanguage.Text("组件锁定"),state.IsEditingLocked(widget)?UiLanguage.Text("是"):UiLanguage.Text("否"));
                PresetDetailField(fields,UiLanguage.Text("交互锁定"),state.IsInteractionLocked(widget)?UiLanguage.Text("是"):UiLanguage.Text("否"));
                if(widget.LayoutGroupId is { } groupId)
                    PresetDetailField(fields,UiLanguage.Text("组件组"),state.LayoutGroups.FirstOrDefault(group=>group.Id==groupId)?.Name??"—");
                if(widget.Kind==WidgetKind.Text)PresetDetailField(fields,UiLanguage.Text("文本"),widget.Visual.Text);
                if(widget.Kind==WidgetKind.Performance)PresetDetailField(fields,UiLanguage.Text("监测项目"),string.Join("、",PerformanceFields.Selected(widget.Metrics).Select(PerformanceFields.Label)));
                if(widget.Kind==WidgetKind.Shortcuts&&state.Shortcuts.FirstOrDefault(shortcut=>shortcut.Id==widget.ShortcutId) is { } shortcut) {
                    PresetDetailField(fields,UiLanguage.Text("目标"),shortcut.Url);
                    PresetDetailField(fields,UiLanguage.Text("类型"),shortcut.Kind switch {ShortcutKind.Website=>UiLanguage.Text("网页"),ShortcutKind.Folder=>UiLanguage.Text("文件夹"),_=>UiLanguage.Text("命令")});
                }
                PresetWidgetOptions(fields,widget);
                return fields;
            }));
        }
        return panel;
    }

    private void PresetWidgetOptions(StackPanel fields,WidgetInstance widget)
    {
        if(widget.Kind is WidgetKind.Text or WidgetKind.Sticker or WidgetKind.Mask||TimeWidgets.IsTime(widget.Kind)) {
            var visual=widget.Visual;
            PresetDetailField(fields,UiLanguage.Text("像素尺寸"),$"{visual.Width} × {visual.Height} px");
            PresetDetailField(fields,UiLanguage.Text("鼠标穿透"),visual.ClickThrough?UiLanguage.Text("开启"):UiLanguage.Text("关闭"));
            PresetDetailField(fields,UiLanguage.Text("背景"),visual.Background?visual.FillColor+" · "+PresetPercent(visual.FillOpacity):UiLanguage.Text("关闭"));
            if(widget.Kind==WidgetKind.Text||TimeWidgets.IsTime(widget.Kind)) {
                PresetDetailField(fields,UiLanguage.Text("字号"),PresetNumber(visual.FontSize," px"));
                PresetDetailField(fields,UiLanguage.Text("文字颜色"),visual.TextColor);
                PresetDetailField(fields,UiLanguage.Text("加粗"),visual.Bold?UiLanguage.Text("是"):UiLanguage.Text("否"));
            }
            if(widget.Kind==WidgetKind.Sticker)PresetDetailField(fields,UiLanguage.Text("图片"),string.IsNullOrEmpty(visual.Image)?UiLanguage.Text("无"):UiLanguage.Text("已包含"));
        }
        switch(widget.Kind) {
            case WidgetKind.Clock:
                PresetDetailField(fields,UiLanguage.Text("时间格式"),widget.Clock.Use24Hour?UiLanguage.Text("24 小时制"):UiLanguage.Text("12 小时制"));
                PresetDetailField(fields,UiLanguage.Text("显示秒"),widget.Clock.ShowSeconds?UiLanguage.Text("是"):UiLanguage.Text("否"));
                PresetDetailField(fields,UiLanguage.Text("显示日期"),widget.Clock.ShowDate?UiLanguage.Text("是"):UiLanguage.Text("否"));
                PresetDetailField(fields,UiLanguage.Text("显示星期"),widget.Clock.ShowWeekday?UiLanguage.Text("是"):UiLanguage.Text("否"));
                break;
            case WidgetKind.Countdown:
                PresetDetailField(fields,UiLanguage.Text("标题"),widget.Countdown.Title);
                PresetDetailField(fields,UiLanguage.Text("目标时间"),widget.Countdown.Target.ToString("yyyy-MM-dd HH:mm:ss zzz",CultureInfo.CurrentCulture));
                PresetDetailField(fields,UiLanguage.Text("结束后正计时"),widget.Countdown.CountUpAfterEnd?UiLanguage.Text("是"):UiLanguage.Text("否"));
                break;
            case WidgetKind.FocusTimer:
                PresetDetailField(fields,UiLanguage.Text("专注时长"),widget.Focus.FocusMinutes+UiLanguage.Text(" 分钟"));
                PresetDetailField(fields,UiLanguage.Text("短休息"),widget.Focus.BreakMinutes+UiLanguage.Text(" 分钟"));
                PresetDetailField(fields,UiLanguage.Text("长休息"),widget.Focus.LongBreakMinutes+UiLanguage.Text(" 分钟"));
                PresetDetailField(fields,UiLanguage.Text("长休息间隔"),widget.Focus.SessionsPerLongBreak+UiLanguage.Text(" 次"));
                PresetDetailField(fields,UiLanguage.Text("自动开始"),widget.Focus.AutoStart?UiLanguage.Text("是"):UiLanguage.Text("否"));
                break;
            case WidgetKind.Search:
                PresetDetailField(fields,UiLanguage.Text("搜索引擎"),SearchWidgetOptions.Label(widget.Search.Engine));
                PresetDetailField(fields,UiLanguage.Text("像素尺寸"),$"{widget.Search.Width} × {widget.Search.Height} px");
                if(widget.Search.Engine==SearchEngine.Custom)PresetDetailField(fields,UiLanguage.Text("搜索地址"),widget.Search.CustomUrl);
                break;
            case WidgetKind.Spectrum:
                PresetDetailField(fields,UiLanguage.Text("像素尺寸"),$"{widget.Spectrum.Width} × {widget.Spectrum.Height} px");
                PresetDetailField(fields,UiLanguage.Text("频段数量"),widget.Spectrum.Bands.ToString());
                PresetDetailField(fields,UiLanguage.Text("灵敏度"),PresetNumber(widget.Spectrum.Sensitivity));
                PresetDetailField(fields,UiLanguage.Text("平滑时间"),widget.Spectrum.SmoothingMs+" ms");
                PresetDetailField(fields,UiLanguage.Text("颜色"),widget.Spectrum.Color);
                PresetDetailField(fields,UiLanguage.Text("子频谱"),widget.Spectrum.SubSpectra.Length.ToString());
                break;
        }
    }
}
