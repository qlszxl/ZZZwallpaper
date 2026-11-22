using LumaWall.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private StackPanel audioRuleList=null!;
    private Slider audioAttackSlider=null!;
    private TextBlock audioLinksHeading=null!,audioLinksEmpty=null!;
    private Button audioAddRuleButton=null!;
    private string? audioRuleToExpand;
    private readonly Dictionary<string,(TextBlock Value,ProgressBar Meter)> audioRuleMeters=[];
    private readonly Dictionary<string,(TextBlock Origin,Button Remove)> audioRuleOrigins=[];
    private readonly List<Action> audioResponseRefreshers=[];
    private bool buildingAudioRules;

    private void InitializeAudioRules()
    {
        audioAttackSlider=new Slider {Style=HomeStyle("HomeSlider"),Minimum=5,Maximum=1000,StepFrequency=1,Value=18};
        AudioResponseFields.Children.Insert(2,NumericSliderRow(UiLanguage.Text("响应时间"),audioAttackSlider,UiLanguage.Text("毫秒"),0));
        audioAttackSlider.ValueChanged+=(_,_)=> {if(loadingEffects)return;effectSession.ExtendedDraft=effectSession.ExtendedDraft with {AudioAttack=audioAttackSlider.Value/1000};DraftChanged();};
        var feedback=(UIElement)AudioFeedbackCard.Content;AudioFeedbackCard.Content=null;EffectsAudioEditor.Children.Remove(AudioFeedbackCard);AudioSourceFields.Children.Add(feedback);
        // Keep one shared section per topic. Rules are flat cards, with no
        // intervening effect-group or duplicate response drawers.
        var links=new StackPanel {Spacing=16};audioLinksHeading=UtilityText(UiLanguage.Text("效果联动"),"UtilityHeading");links.Children.Add(audioLinksHeading);
        audioAddRuleButton=WidgetButton(UiLanguage.Text("添加效果联动"),AddAudioRule,"Add");audioAddRuleButton.HorizontalAlignment=HorizontalAlignment.Left;links.Children.Add(audioAddRuleButton);
        audioLinksEmpty=UtilityText(UiLanguage.Text("暂无联动"));links.Children.Add(audioLinksEmpty);
        audioRuleList=new StackPanel {Spacing=16};links.Children.Add(audioRuleList);
        AudioSettingsRoot.Children.Add(UtilityCard(links));
    }

    private void ChangeAudioRule(string key,Func<AudioRule,AudioRule> edit)
    {
        if(loadingEffects||buildingAudioRules)return;
        var current=effectSession.ExtendedDraft.AudioRules.FirstOrDefault(r=>r.Parameter==key);if(current==null)return;
        effectSession.ExtendedDraft=effectSession.ExtendedDraft with {AudioRules=AudioParameters.Upsert(effectSession.ExtendedDraft.AudioRules,edit(current))};
        if(effectTargetAssetId!=null&&audioRuleOrigins.TryGetValue(key,out var ui)) {ui.Origin.Text=UiLanguage.Text("来源 · 素材");ui.Remove.IsEnabled=true;}
        DraftChanged();
    }

    private void RemoveAudioRule(string key)
    {
        FlushEffectChanges();
        if(SelectedScreen is not { } screen)return;
        var baseline=modules.Effects.ScreenProfile(screen.DeviceName)??new WallpaperProfile(FillMode.Cover,VideoEffects.Original);
        if(effectTargetAssetId is { } asset) {
            var own=modules.Effects.AssetProfile(asset);
            // Removing a material override restores the current display rule.
            modules.Effects.Commit(screen.DeviceName,asset,baseline,own with {Effects=own.Effects with {AudioRules=own.Effects.AudioRules.Where(r=>r.Parameter!=key).ToArray()}});
        } else modules.Effects.SaveScreenProfile(screen.DeviceName,baseline with {Effects=baseline.Effects with {AudioRules=baseline.Effects.AudioRules.Where(r=>r.Parameter!=key).ToArray()}});
        wallpapers?.RefreshLiveEffects();LoadEffectDraft(true,preserveView:true);
    }

    private void RefreshAudioRules()
    {
        if(audioRuleList==null)return;
        if(effectsPageReady&&displayedEffectKind==EffectKind.AudioReactive)CaptureEffectSectionStates(EffectKind.AudioReactive);
        buildingAudioRules=true;
        try {
            var rules=effectSession.ExtendedDraft.AudioRules;
            audioAttackSlider.Value=effectSession.ExtendedDraft.AudioAttack*1000;
            audioLinksHeading.Text=rules.Length==0?UiLanguage.Text("效果联动"):UiLanguage.Format($"效果联动 · {rules.Length}");
            audioLinksEmpty.Visibility=rules.Length==0?Visibility.Visible:Visibility.Collapsed;
            audioRuleList.Visibility=rules.Length==0?Visibility.Collapsed:Visibility.Visible;
            audioAddRuleButton.IsEnabled=AudioParameters.Selectable.Count>0;
            audioRuleList.Children.Clear();audioRuleMeters.Clear();audioRuleOrigins.Clear();audioResponseRefreshers.Clear();
            var own=effectTargetAssetId is { } asset?modules.Effects.AssetProfile(asset).Effects.AudioRules:rules;
            foreach(var parameter in AudioParameters.Selectable) {
                var rule=rules.FirstOrDefault(r=>r.Parameter==parameter.Key);if(rule==null)continue;
                audioRuleList.Children.Add(BuildAudioRule(rule,parameter,own.Any(r=>r.Parameter==rule.Parameter)));
            }
            if(effectsPageReady) {
                DecorateParameterSections(EffectsAudioEditor,UiLanguage.Text("音频联动设置"));RestoreEffectSectionStates(EffectKind.AudioReactive);
                if(audioRuleToExpand is { } key) {
                    var card=audioRuleList.Children.OfType<ContentControl>().FirstOrDefault(c=>c.Tag as string=="effect-fold:audio-rule:"+key);
                    if(card!=null)SectionChrome.SetIsExpanded(card,true);
                }
            }
            audioRuleToExpand=null;
        } finally {buildingAudioRules=false;}
    }

    private FrameworkElement BuildAudioRule(AudioRule rule,AudioParameter parameter,bool own)
    {
        string key=rule.Parameter;var panel=new StackPanel {Spacing=16};
        var enabled=WidgetToggle(UiLanguage.Text("启用此联动"),rule.Enabled);enabled.Toggled+=(_,_)=>ChangeAudioRule(key,r=>r with {Enabled=enabled.IsOn});
        panel.Children.Add(WidgetToggleRow(UiLanguage.Text("启用此联动"),enabled));
        if(!parameter.IsEnabled(DraftEffects))panel.Children.Add(UtilityText(UiLanguage.Format($"{parameter.DisplayEffectName}未启用")));
        var fields=new Grid {ColumnSpacing=16,RowSpacing=12};
        ComboBox Choice(string title,string[] names,int index,Action<int> change,Panel? host=null) {
            var picker=new ComboBox {Style=HomeStyle("AudioRuleComboBox"),HorizontalAlignment=HorizontalAlignment.Stretch};
            foreach(string name in names)picker.Items.Add(new ComboBoxItem {Content=name});picker.SelectedIndex=index;
            DiagnosticInput.SetTarget(picker,"AudioRule:"+key+":"+title);
            AutomationProperties.SetName(picker,parameter.DisplayName+title);picker.SelectionChanged+=(_,_)=> {if(picker.SelectedIndex>=0)change(picker.SelectedIndex);};(host??fields).Children.Add(WidgetField(title,picker));return picker;
        }
        Choice(UiLanguage.Text("频段"),[UiLanguage.Text("整体声音"),UiLanguage.Text("低频"),UiLanguage.Text("中频"),UiLanguage.Text("高频")],rule.Band,v=>ChangeAudioRule(key,r=>r with {Band=v}));
        ComboBox? responsePicker=null;
        if(!parameter.Trigger)responsePicker=Choice(UiLanguage.Text("响应方式"),[UiLanguage.Text("连续跟随"),UiLanguage.Text("节拍触发")],rule.Response,v=>ChangeAudioRule(key,r=>r with {Response=v}));
        UtilityAdaptiveGrid(fields,2,560);panel.Children.Add(fields);
        double scale=key=="RgbOffset"?effectRgbScale:parameter.DisplayScale;
        double maximum=key=="RgbOffset"?Math.Min(parameter.Maximum,RgbSlider.Maximum/scale):parameter.Maximum;
        FrameworkElement Number(string title,double value,double min,double max,double factor,string unit,Action<double> change) {
            double step=unit is "%" or "°" || unit == UiLanguage.Text("毫秒") || unit == UiLanguage.Text("次")?1:unit==UiLanguage.Text("秒")?.1:.01;
            var slider=new Slider {Style=HomeStyle("HomeSlider"),Minimum=min*factor,Maximum=Math.Max(max,value)*factor,Value=value*factor,StepFrequency=step};
            AutomationProperties.SetName(slider,parameter.DisplayName+title);slider.ValueChanged+=(_,_)=>change(slider.Value/factor);
            return NumericSliderRow(title,slider,unit,step==1?0:unit==UiLanguage.Text("秒")?1:2);
        }
        if(!parameter.Trigger)panel.Children.Add(Number(UiLanguage.Text("弱音值"),rule.Weak,parameter.Minimum,maximum,scale,parameter.DisplayUnit,v=>ChangeAudioRule(key,r=>r with {Weak=v})));
        if(!parameter.Trigger)panel.Children.Add(Number(UiLanguage.Text("强音值"),rule.Strong,parameter.Minimum,maximum,scale,parameter.DisplayUnit,v=>ChangeAudioRule(key,r=>r with {Strong=v})));

        var thresholdToggle=WidgetToggle(UiLanguage.Text("阈值切换"),rule.ThresholdSwitch);
        var thresholdValue=Number(UiLanguage.Text("切换阈值"),rule.SwitchThreshold,.01,1,100,"%",v=>ChangeAudioRule(key,r=>r with {SwitchThreshold=v}));
        thresholdValue.Visibility=thresholdToggle.IsOn?Visibility.Visible:Visibility.Collapsed;
        if(!parameter.Trigger) {panel.Children.Add(WidgetToggleRow(UiLanguage.Text("阈值切换"),thresholdToggle));panel.Children.Add(thresholdValue);}
        var frequencySection=new StackPanel {Spacing=16};
        var frequencyPicker=Choice(UiLanguage.Text("触发频率"),[UiLanguage.Text("无限制"),UiLanguage.Text("限制次数")],rule.TriggerLimitEnabled?1:0,v=>ChangeAudioRule(key,r=>r with {TriggerLimitEnabled=v==1}),frequencySection);
        var frequencyFields=new StackPanel {Spacing=16,Visibility=rule.TriggerLimitEnabled?Visibility.Visible:Visibility.Collapsed};
        frequencyFields.Children.Add(Number(UiLanguage.Text("时间窗口"),rule.TriggerLimitWindow,.1,60,1,UiLanguage.Text("秒"),v=>ChangeAudioRule(key,r=>r with {TriggerLimitWindow=v})));
        frequencyFields.Children.Add(Number(UiLanguage.Text("最多触发"),rule.TriggerLimitCount,1,100,1,UiLanguage.Text("次"),v=>ChangeAudioRule(key,r=>r with {TriggerLimitCount=(int)Math.Round(v)})));
        frequencySection.Children.Add(frequencyFields);panel.Children.Add(frequencySection);
        void RefreshTriggerFields() {
            thresholdValue.Visibility=thresholdToggle.IsOn?Visibility.Visible:Visibility.Collapsed;
            frequencySection.Visibility=parameter.Trigger||thresholdToggle.IsOn||responsePicker?.SelectedIndex==1?Visibility.Visible:Visibility.Collapsed;
            frequencyFields.Visibility=frequencyPicker.SelectedIndex==1?Visibility.Visible:Visibility.Collapsed;
        }
        thresholdToggle.Toggled+=(_,_)=> {ChangeAudioRule(key,r=>r with {ThresholdSwitch=thresholdToggle.IsOn});RefreshTriggerFields();};
        if(responsePicker!=null)responsePicker.SelectionChanged+=(_,_)=>RefreshTriggerFields();
        frequencyPicker.SelectionChanged+=(_,_)=>RefreshTriggerFields();RefreshTriggerFields();

        bool timedTrigger=parameter.Trigger&&parameter.Effect is EffectKind.Glitch or EffectKind.DotRipple or EffectKind.ZoomTunnel;
        if(timedTrigger)panel.Children.Add(Number(UiLanguage.Text("持续时间"),rule.Release??effectSession.ExtendedDraft.AudioSmoothing,.02,2,1000,UiLanguage.Text("毫秒"),v=>ChangeAudioRule(key,r=>r with {Release=v})));
        bool syncingResponse=false;
        var responseToggle=WidgetToggle(UiLanguage.Text("独立响应"),rule.Sensitivity.HasValue||rule.Threshold.HasValue||(!parameter.Trigger&&(rule.Attack.HasValue||rule.Release.HasValue)));
        panel.Children.Add(WidgetToggleRow(UiLanguage.Text("独立响应"),responseToggle));
        var responseFields=new StackPanel {Spacing=16,Visibility=responseToggle.IsOn?Visibility.Visible:Visibility.Collapsed};
        void ResponseNumber(string title,Func<AudioRule,double?> get,Func<double> fallback,double min,double max,double factor,string unit,Func<AudioRule,double,AudioRule> set) {
            double step=unit==UiLanguage.Text("毫秒")||unit=="%"?1:.05;
            var slider=new Slider {Style=HomeStyle("HomeSlider"),Minimum=min*factor,Maximum=max*factor,StepFrequency=step,Value=(get(rule)??fallback())*factor};
            AutomationProperties.SetName(slider,parameter.DisplayName+UiLanguage.Text("独立")+title);
            slider.ValueChanged+=(_,_)=> {if(!syncingResponse)ChangeAudioRule(key,r=>set(r,slider.Value/factor));};
            responseFields.Children.Add(NumericSliderRow(title,slider,unit,step==1?0:2));
            audioResponseRefreshers.Add(()=> {
                var current=effectSession.ExtendedDraft.AudioRules.FirstOrDefault(r=>r.Parameter==key);if(current==null)return;
                syncingResponse=true;try {slider.Value=(get(current)??fallback())*factor;}finally {syncingResponse=false;}
            });
        }
        ResponseNumber(UiLanguage.Text("灵敏度"),r=>r.Sensitivity,()=>AudioSensitivitySlider.Value,.25,5,1,"×",(r,v)=>r with {Sensitivity=v});
        if(!parameter.Trigger)ResponseNumber(UiLanguage.Text("响应时间"),r=>r.Attack,()=>effectSession.ExtendedDraft.AudioAttack,.005,1,1000,UiLanguage.Text("毫秒"),(r,v)=>r with {Attack=v});
        if(!parameter.Trigger)ResponseNumber(UiLanguage.Text("回落时间"),r=>r.Release,()=>AudioSmoothingSlider.Value/1000,.02,2,1000,UiLanguage.Text("毫秒"),(r,v)=>r with {Release=v});
        ResponseNumber(UiLanguage.Text("节拍阈值"),r=>r.Threshold,()=>AudioBeatThresholdSlider.Value/100,.05,.95,100,"%",(r,v)=>r with {Threshold=v});
        panel.Children.Add(responseFields);
        responseToggle.Toggled+=(_,_)=> {
            if(loadingEffects||buildingAudioRules)return;
            responseFields.Visibility=responseToggle.IsOn?Visibility.Visible:Visibility.Collapsed;
            ChangeAudioRule(key,r=>responseToggle.IsOn?r with {Sensitivity=r.Sensitivity??AudioSensitivitySlider.Value,Attack=parameter.Trigger?null:r.Attack??effectSession.ExtendedDraft.AudioAttack,Release=parameter.Trigger?r.Release:r.Release??AudioSmoothingSlider.Value/1000,Threshold=r.Threshold??AudioBeatThresholdSlider.Value/100}:
                r with {Sensitivity=null,Attack=null,Release=parameter.Trigger?r.Release:null,Threshold=null});
            RefreshAudioResponseDefaults();
        };
        var valueLabel=UtilityText(parameter.Trigger?UiLanguage.Text("等待节拍"):UiLanguage.Text("当前值 · —"));var meter=new ProgressBar {Minimum=0,Maximum=100,Height=6,Foreground=HomeBrush("InteractionAccentBrush")};
        panel.Children.Add(valueLabel);panel.Children.Add(meter);audioRuleMeters[key]=(valueLabel,meter);
        var origin=UtilityText(effectTargetAssetId!=null&&own?UiLanguage.Text("来源 · 素材"):UiLanguage.Text("来源 · 显示器"));
        var remove=WidgetButton(effectTargetAssetId==null?UiLanguage.Text("删除联动"):UiLanguage.Text("移除素材联动"),()=>RemoveAudioRule(key),"Trash");remove.IsEnabled=effectTargetAssetId==null||own;
        var footer=new Grid {ColumnSpacing=16};footer.ColumnDefinitions.Add(new());footer.ColumnDefinitions.Add(new() {Width=GridLength.Auto});
        origin.VerticalAlignment=VerticalAlignment.Center;footer.Children.Add(origin);Grid.SetColumn(remove,1);footer.Children.Add(remove);panel.Children.Add(footer);audioRuleOrigins[key]=(origin,remove);
        var card=UtilityCard(panel,parameter.DisplayEffectName+" · "+parameter.DisplayName);card.Tag="effect-fold:audio-rule:"+key;return card;
    }

    private void RefreshAudioResponseDefaults()
    {
        if(loadingEffects||buildingAudioRules)return;
        foreach(var refresh in audioResponseRefreshers)refresh();
    }

    private void UpdateAudioRuleMeters(NativePlayback? player,bool receiving)
    {
        foreach(var (key,ui) in audioRuleMeters) {
            var p=AudioParameters.Find(key);double scale=key=="RgbOffset"?effectRgbScale:p.DisplayScale;
            if(receiving&&player!.AudioRuleValues.TryGetValue(p.Id,out var current)) {ui.Value.Text=p.Trigger?(current.Signal>.01?UiLanguage.Text("节拍触发"):UiLanguage.Text("等待节拍")):UiLanguage.Format($"当前值 · {current.Value*scale:0.##}{p.DisplayUnit}");ui.Meter.Value=current.Signal*100;}
            else {ui.Value.Text=p.Trigger?UiLanguage.Text("等待节拍"):UiLanguage.Text("当前值 · —");ui.Meter.Value=0;}
        }
    }
}
