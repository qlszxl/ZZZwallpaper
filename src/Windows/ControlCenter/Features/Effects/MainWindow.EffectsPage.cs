using LumaWall.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private sealed record EffectDefinition(EffectKind Kind,FrameworkElement Panel,ToggleSwitch? Enabled)
    {
        public string Name=>UiLanguage.Text(ExpansionCatalog.Effect(Kind).Title);
        public string Icon=>ExpansionCatalog.Effect(Kind).Icon;
    }
    private EffectDefinition[] effectDefinitions=[];
    private EffectKind? selectedEffectKind,displayedEffectKind;
    private bool effectsPageReady,syncingEffectInstance;
    private string effectsOverviewKey="";
    private sealed record EffectOverviewControls(Button Button,FrameworkElement Icon);
    private readonly Dictionary<EffectKind,EffectOverviewControls> effectOverviewControls=new();
    private ToggleSwitch effectEnabled=null!,effectSolo=null!,taskbarSwitch=null!;
    private ComboBox taskbarStylePicker=null!;
    private ComboBox glitchMode=null!;
    private Button effectExplanationButton=null!;
    private bool effectExplanationOpen;
    private ContentControl effectExplanationCard=null!;
    private ContentControl effectExplanationCaption=null!;
    private Grid effectStrengthRow=null!;
    private Slider effectStrength=null!;
    private FrameworkElement effectEnableRow=null!,effectSoloRow=null!;
    private TextBlock effectSaveStatus=null!;
    private static SolidColorBrush EffectsBrush(byte r,byte g,byte b)=>new(Color.FromArgb(255,r,g,b));

    private void InitializeEffectsPage()
    {
        InitializeDotShapes();
        var taskbarPanel=CreateTaskbarSettings();EffectsDetailSections.Children.Add(taskbarPanel);
        glitchMode=new ComboBox {Style=HomeStyle("UtilityComboBox"),HorizontalAlignment=HorizontalAlignment.Stretch};glitchMode.Items.Add(UiLanguage.Text("基础条带 · 整个画面"));glitchMode.Items.Add(UiLanguage.Text("局部集群 · 单独设置区域"));
        EffectsGlitchEditor.Children.Insert(0,UtilityCard(WidgetField(UiLanguage.Text("故障模式"),glitchMode),UiLanguage.Text("故障模式")));
        glitchMode.SelectionChanged+=(_,_)=> {if(!loadingEffects&&!syncingEffectInstance&&glitchMode.SelectedIndex>=0)GlitchClusterSwitch.IsOn=glitchMode.SelectedIndex==1;};
        effectDefinitions=[
            new(EffectKind.Color,EffectsColorEditor,null),
            new(EffectKind.RgbSplit,EffectsRgbEditor,RgbSwitch),
            new(EffectKind.Glow,EffectsGlowEditor,GlowSwitch),
            new(EffectKind.Vignette,EffectsVignetteEditor,VignetteSwitch),
            new(EffectKind.Noise,EffectsNoiseEditor,NoiseSwitch),
            new(EffectKind.Scanlines,EffectsScanEditor,ScanlineSwitch),
            new(EffectKind.Glitch,EffectsGlitchEditor,GlitchSwitch),
            new(EffectKind.LargeGlitch,EffectsLargeGlitchEditor,LargeGlitchSwitch),
            new(EffectKind.Dots,EffectsDotsEditor,DotSwitch),
            new(EffectKind.DotRipple,EffectsRippleEditor,DotRippleSwitch),
            new(EffectKind.Blur,EffectsBlurEditor,BlurSwitch),
            new(EffectKind.Distortion,EffectsDistortionEditor,DistortionSwitch),
            new(EffectKind.HideTaskbar,taskbarPanel,taskbarSwitch),
            new(EffectKind.AudioReactive,EffectsAudioEditor,AudioReactiveSwitch)
        ];
        // Keep appearance effects together, followed by desktop/audio utilities.
        effectDefinitions=[..effectDefinitions.Where(e=>e.Kind is not (EffectKind.HideTaskbar or EffectKind.AudioReactive)),..CreateExtendedEffects(),..effectDefinitions.Where(e=>e.Kind is EffectKind.HideTaskbar or EffectKind.AudioReactive)];
        var editors=effectDefinitions.Append(CreateMaskLibrary()).ToDictionary(e=>e.Kind);
        effectDefinitions=ExpansionCatalog.Effects.Keys.Select(kind=>editors[kind]).ToArray();
        InitializeAdvancedEffects();
        var summary=new StackPanel {Spacing=16};
        effectEnabled=WidgetToggle(UiLanguage.Text("启用效果"),false);summary.Children.Add(effectEnableRow=WidgetToggleRow(UiLanguage.Text("启用效果"),effectEnabled));
        effectSolo=WidgetToggle(UiLanguage.Text("独显此效果"),false);summary.Children.Add(effectSoloRow=WidgetToggleRow(UiLanguage.Text("独显此效果"),effectSolo));
        effectSolo.Toggled+=(_,_)=> {if(!syncingEffectInstance&&!loadingEffects&&selectedEffectKind is { } solo)SetEffectSolo(solo,effectSolo.IsOn);};
        effectExplanationButton=WidgetButton(UiLanguage.Text("效果预览"),()=>SetEffectExplanation(!effectExplanationOpen));effectExplanationButton.Style=HomeStyle("ParameterSectionToggle");effectExplanationButton.HorizontalContentAlignment=HorizontalAlignment.Stretch;effectExplanationButton.HorizontalAlignment=HorizontalAlignment.Stretch;
        effectExplanationButton.Padding=new Thickness(24);effectExplanationButton.MinHeight=80;
        effectExplanationCaption=new ContentControl {HorizontalContentAlignment=HorizontalAlignment.Stretch,IsTabStop=false};effectExplanationButton.Content=effectExplanationCaption;
        var explanationBody=new StackPanel {Spacing=14,Margin=new Thickness(0,0,0,4)};InitializeEffectImagePreview(explanationBody);
        var explanationCard=effectExplanationCard=UtilityCard(explanationBody,UiLanguage.Text("效果预览"),true);explanationCard.Style=HomeStyle("ParameterSectionCard");
        SectionChrome.SetHeader(explanationCard,effectExplanationButton);SectionChrome.SetHeaderVisibility(explanationCard,Visibility.Visible);
        // The header owns the action; its feedback follows the entire preview frame.
        InteractionFocus.SetFeedbackSurface(effectExplanationButton,explanationCard);
        EffectsExplanationHost.Content=explanationCard;SetEffectExplanation(false);
        var row=effectStrengthRow=new Grid {ColumnSpacing=16,MinHeight=44};
        row.ColumnDefinitions.Add(new() {Width=new GridLength(128)});row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new() {Width=new GridLength(198)});
        var label=UtilityText(UiLanguage.Text("效果强度"),"UtilityLabel");label.VerticalAlignment=VerticalAlignment.Center;row.Children.Add(label);
        effectStrength=new Slider {Style=HomeStyle("HomeSlider"),Minimum=0,Maximum=100,StepFrequency=1,Value=100,MinWidth=96,VerticalAlignment=VerticalAlignment.Center};
        AutomationProperties.SetName(effectStrength,UiLanguage.Text("当前效果强度"));Grid.SetColumn(effectStrength,1);row.Children.Add(effectStrength);
        var value=new EffectValueInput {Target=effectStrength,Unit="%",Precision=0,Width=198};Grid.SetColumn(value,2);row.Children.Add(value);summary.Children.Add(row);
        InitializeMaskReference(summary);
        effectSaveStatus=UtilityText("");effectSaveStatus.Visibility=Visibility.Collapsed;summary.Children.Add(effectSaveStatus);EffectsInstanceControls.Content=summary;
        effectEnabled.Toggled+=(_,_)=> {
            if(syncingEffectInstance||loadingEffects||selectedEffectKind is not { } kind)return;
            var definition=effectDefinitions.First(d=>d.Kind==kind);
            SetEffectEnabled(definition,effectEnabled.IsOn);
        };
        effectStrength.ValueChanged+=(_,_)=> {
            if(syncingEffectInstance||loadingEffects||selectedEffectKind is not { } kind)return;
            effectSession.Collection=effectSession.Collection.WithStrength(kind,effectStrength.Value/100);DraftChanged();
        };
        InitializeEffectOrder();InitializeAudioRules();InitializeParameterSections();effectsPageReady=true;ResetEffectDisclosureSession();InitializeModalLayout();InitializeEffectsLayout();InitializeEffectColorChoices();RefreshEffectCollection();RefreshEffectsContext();
    }
    private void SetEffectExplanation(bool open)
    {
        SectionChrome.SetIsExpanded(effectExplanationCard,open);effectExplanationOpen=open;effectExplanationBody.Visibility=open?Visibility.Visible:Visibility.Collapsed;
        if(open)StartEffectImage();else StopEffectImage();
        var caption=new Grid {ColumnSpacing=16};caption.ColumnDefinitions.Add(new());caption.ColumnDefinitions.Add(new() {Width=GridLength.Auto});
        var label=UtilityText(UiLanguage.Text("效果预览"),"UtilityHeading");label.VerticalAlignment=VerticalAlignment.Center;label.Margin=new Thickness(28,0,0,0);caption.Children.Add(label);
        var previewIcon=UtilityIcon("Image",label,20);previewIcon.HorizontalAlignment=HorizontalAlignment.Left;caption.Children.Add(previewIcon);
        var icon=CreateHomeVectorIcon("ChevronDown",16,effectExplanationButton);icon.RenderTransformOrigin=new Windows.Foundation.Point(.5,.5);icon.RenderTransform=new RotateTransform {Angle=open?180:0};Grid.SetColumn(icon,1);caption.Children.Add(icon);effectExplanationCaption.Content=caption;
        AutomationProperties.SetName(effectExplanationButton,open?UiLanguage.Text("收起效果预览"):UiLanguage.Text("展开效果预览"));
    }
    private bool EffectIsEnabled(EffectDefinition definition)=>definition.Kind==EffectKind.MaskLibrary|| (definition.Enabled?.IsOn??effectSession.Collection.ColorEnabled);
    private void RefreshEffectCollection()
    {
        if(!effectsPageReady||loadingEffects)return;
        if(selectedEffectKind is { } old&&(effectSession.Collection.Added&old)==0)selectedEffectKind=null;
        bool detail=selectedEffectKind!=null;
        EffectsOverview.Visibility=detail?Visibility.Collapsed:Visibility.Visible;
        EffectsDetailPanel.Visibility=detail?Visibility.Visible:Visibility.Collapsed;
        EffectsDetailTitle.Visibility=detail?Visibility.Visible:Visibility.Collapsed;
        EffectsCountText.Visibility=!detail&&SelectedScreen!=null?Visibility.Visible:Visibility.Collapsed;
        bool disclosureChanged=displayedEffectKind!=selectedEffectKind;
        if(disclosureChanged)CaptureEffectDisclosures();
        if(disclosureChanged||(!detail&&EffectsBackActions.Children.Count>0)) {
            displayedEffectKind=selectedEffectKind;EffectsBackActions.Children.Clear();
            if(detail) {EffectsBackActions.Children.Add(WidgetButton(UiLanguage.Text("返回效果"),EffectsBack,"Back"));EffectsBackActions.Children.Add(EffectsRemoveButton);}
        }
        // Both glitch families expose the same saved satellite color settings.
        var colorHost=selectedEffectKind==EffectKind.LargeGlitch?EffectsLargeGlitchEditor:EffectsGlitchEditor;
        if(EffectsGlitchColorEditor.Parent is Panel previousHost&&previousHost!=colorHost) {
            previousHost.Children.Remove(EffectsGlitchColorEditor);colorHost.Children.Add(EffectsGlitchColorEditor);
        }
        foreach(var definition in effectDefinitions)definition.Panel.Visibility=definition.Kind==selectedEffectKind?Visibility.Visible:Visibility.Collapsed;
        if(disclosureChanged)RestoreEffectDisclosures();
        RefreshLightFlareLocator();RefreshMaskReference();RefreshEffectScope();
        if(detail) {
            var definition=effectDefinitions.First(d=>d.Kind==selectedEffectKind);
            EffectsDetailTitle.Text=definition.Name;EffectsDetailDescription.Visibility=Visibility.Collapsed;
            effectStrength.IsEnabled=definition.Kind is not (EffectKind.HideTaskbar or EffectKind.MaskLibrary);
            effectStrengthRow.Visibility=definition.Kind is EffectKind.HideTaskbar or EffectKind.MaskLibrary?Visibility.Collapsed:Visibility.Visible;
            effectEnabled.IsEnabled=definition.Kind!=EffectKind.MaskLibrary;
            effectSolo.IsEnabled=definition.Kind!=EffectKind.MaskLibrary&&EffectPreviewAssetId!=null;
            effectEnableRow.Visibility=effectSoloRow.Visibility=definition.Kind==EffectKind.MaskLibrary?Visibility.Collapsed:Visibility.Visible;
            syncingEffectInstance=true;
            try {effectSolo.IsOn=wallpapers?.SoloKind==definition.Kind;effectEnabled.IsOn=EffectIsEnabled(definition);effectStrength.Value=effectSession.Collection.Strength(definition.Kind)*100;}
            finally {syncingEffectInstance=false;}
            RefreshEffectParameterAccess(definition);
            AutomationProperties.SetName(effectStrength,definition.Name+UiLanguage.Text("强度"));
        } else RefreshEffectOverview();
        syncingEffectInstance=true;
        try {glitchMode.SelectedIndex=GlitchClusterSwitch.IsOn?1:0;} finally {syncingEffectInstance=false;}
        GlitchBasicSection.Visibility=GlitchClusterSwitch.IsOn?Visibility.Collapsed:Visibility.Visible;
        GlitchClusterSection.Visibility=GlitchClusterSwitch.IsOn?Visibility.Visible:Visibility.Collapsed;
        LayoutEffectsPage();
    }
    private void SetEffectEnabled(EffectDefinition definition,bool enabled)
    {
        if(loadingEffects||SelectedScreen==null||definition.Kind==EffectKind.MaskLibrary||EffectIsEnabled(definition)==enabled)return;
        // Reuse the parameter controls so their defaulting, save and target-scope
        // handlers remain the only path that changes the underlying effect.
        if(definition.Enabled is { } toggle)toggle.IsOn=enabled;
        else {effectSession.Collection=effectSession.Collection with {ColorEnabled=enabled};DraftChanged();}
        RefreshEffectDraftUi();
    }
    private void SetEffectSolo(EffectKind kind,bool solo)
    {
        if(loadingEffects||kind==EffectKind.MaskLibrary)return;
        if(SelectedScreen is { } screen&&EffectPreviewAssetId is { } asset) {
            FlushEffectChanges();wallpapers?.SetEffectSolo(screen.DeviceName,asset,solo?kind:null);
        }
        RefreshEffectCollection();
    }
    private FrameworkElement EffectOverviewCard(EffectDefinition definition)
    {
        var menu=CreateCardMenu();
        var remove=CardMenuAction(UiLanguage.Text("移除效果"),()=>RemoveEffect(definition.Kind));
        MenuFlyoutItem? enabled=null,solo=null;
        if(definition.Kind!=EffectKind.MaskLibrary) {
            enabled=CardMenuAction(UiLanguage.Text("启用效果"),()=>SetEffectEnabled(definition,!EffectIsEnabled(definition)));
            solo=CardMenuAction(UiLanguage.Text("独显此效果"),()=>SetEffectSolo(definition.Kind,wallpapers?.SoloKind!=definition.Kind));
            menu.Items.Add(enabled);menu.Items.Add(solo);menu.Items.Add(new MenuFlyoutSeparator());
        }
        menu.Items.Add(remove);
        void RefreshMenu()
        {
            bool editable=!loadingEffects&&SelectedScreen!=null&&(effectSession.Collection.Added&definition.Kind)!=0;
            if(enabled!=null) {enabled.Text=EffectIsEnabled(definition)?UiLanguage.Text("停用效果"):UiLanguage.Text("启用效果");enabled.IsEnabled=editable;}
            if(solo!=null) {solo.Text=wallpapers?.SoloKind==definition.Kind?UiLanguage.Text("退出独显"):UiLanguage.Text("独显此效果");solo.IsEnabled=editable&&EffectPreviewAssetId!=null;}
            remove.IsEnabled=editable;
        }
        menu.Opening+=(_,_)=>RefreshMenu();RefreshMenu();
        if(EffectOrder.Supports(definition.Kind))return EffectLayerCard(definition,menu);
        var button=EffectChoiceButton(definition,()=>OpenEffect(definition.Kind),112,overview:true);
        button.ContextFlyout=menu;button.Padding=new Thickness(18,0,18,0);var card=new Grid();card.Children.Add(button);AddCardHelp(card,definition.Name);return card;
    }
    private void SyncEffectOverviewControls()
    {
        if(!effectsPageReady)return;
        foreach(var definition in effectDefinitions) {
            if(!effectOverviewControls.TryGetValue(definition.Kind,out var controls))continue;
            bool enabled=EffectIsEnabled(definition),solo=wallpapers?.SoloKind==definition.Kind;
            SetOverviewIconActive(controls.Icon,definition.Kind!=EffectKind.MaskLibrary&&enabled);
            string status=definition.Kind==EffectKind.MaskLibrary?UiLanguage.Format($"{effectSession.ExtendedDraft.Masks.Length} 个遮罩"):(enabled?UiLanguage.Text("已启用"):UiLanguage.Text("已暂停"))+(definition.Kind==EffectKind.HideTaskbar?UiLanguage.Text(" · 当前显示器"):UiLanguage.Format($" · 强度 {effectSession.Collection.Strength(definition.Kind)*100:0}%"));
            if(definition.Kind!=EffectKind.MaskLibrary)status+=((draftOverrides&definition.Kind)!=0?UiLanguage.Text(" · 素材专属"):UiLanguage.Text(" · 显示器默认"))+(solo?UiLanguage.Text(" · 独显中"):"");
            AutomationProperties.SetItemStatus(controls.Button,status);
        }
        SyncEffectLayerControls();
    }
    private Button EffectChoiceButton(EffectDefinition definition,Action action,double height,bool overview=false,bool reorder=false)
    {
        Button button;
        if(reorder) {
            button=new EffectReorderButton {Style=HomeStyle("UtilityButton"),HorizontalAlignment=HorizontalAlignment.Stretch,Activate=action};
            UtilityButtonContent(button,definition.Name,null,false);
        } else button=WidgetButton(definition.Name,action);
        button.Height=height;button.Padding=new Thickness(18,0,18,0);button.HorizontalContentAlignment=HorizontalAlignment.Stretch;
        var row=new Grid {ColumnSpacing=16};row.ColumnDefinitions.Add(new() {Width=new GridLength(32)});row.ColumnDefinitions.Add(new());
        var icon=CreateEffectMetalIcon(definition.Kind,32,overview&&definition.Kind!=EffectKind.MaskLibrary&&EffectIsEnabled(definition));row.Children.Add(icon);
        if(overview)effectOverviewControls[definition.Kind]=new(button,icon);
        var labels=new StackPanel {Spacing=7,VerticalAlignment=VerticalAlignment.Center};
        var title=UtilityText(definition.Name,"UtilityHeading");title.TextWrapping=TextWrapping.NoWrap;title.TextTrimming=TextTrimming.CharacterEllipsis;labels.Children.Add(title);
        Grid.SetColumn(labels,1);row.Children.Add(labels);button.Content=row;return button;
    }
    private void OpenEffect(EffectKind kind)
    {
        ClearEffectLayerDrag();
        CommitSpectrumInput();if(selectedEffectKind!=kind)RememberNavigation();
        FlushEffectChanges();if(wallpapers?.SoloKind!=kind)wallpapers?.ClearEffectSolo();CloseEffectsAdd(false);selectedEffectKind=kind;
        if(kind==EffectKind.AudioReactive)RefreshAudioRules();
        RefreshEffectCollection();EffectsParameterScroll.ChangeView(null,0,null,true);AnimatePageEntry("display");
    }
    private void EffectsBack()
    {
        CommitSpectrumInput();RememberNavigation();
        FlushEffectChanges();ResetEffectsNavigation(keepSolo:true);EffectsAddButton.Focus(FocusState.Programmatic);AnimatePageEntry("display");
    }
    private void ResetEffectsNavigation(bool keepSolo=false)
    {
        if(!effectsPageReady)return;
        if(!keepSolo)wallpapers?.ClearEffectSolo();ClearEffectLayerDrag();CloseEffectsAdd(false);selectedEffectKind=null;effectsOverviewKey="";RefreshEffectCollection();EffectsParameterScroll.ChangeView(null,0,null,true);
    }
    private void EffectsAdd_Click(object sender,RoutedEventArgs e)
    {
        if(SelectedScreen==null)return;
        CloseEffectsTarget(false);FlushEffectChanges();EffectsAddList.Children.Clear();EffectsAddAuxiliaryList.Children.Clear();
        foreach(var kind in EffectOrder.Default.Concat(EffectLayerPresentation.AuxiliaryKinds(effectTargetAssetId!=null))) {
            var definition=effectDefinitions.First(definition=>definition.Kind==kind);
            bool added=(effectSession.Collection.Added&definition.Kind)!=0;
            var button=EffectChoiceButton(definition,()=>AddEffect(definition.Kind),112);button.Height=double.NaN;button.MinHeight=112;button.IsEnabled=!added;
            if(added) {var labels=(StackPanel)((Grid)button.Content).Children[1];labels.Children.Add(UtilityText(UiLanguage.Text("已添加")));}
            button.Padding=new Thickness(18,0,18,0);var card=new Grid();card.Children.Add(button);AddCardHelp(card,definition.Name);
            (EffectOrder.Supports(kind)?EffectsAddList:EffectsAddAuxiliaryList).Children.Add(card);
        }
        foreach(var item in FeaturePacks.Effects){
            bool added=effectSession.ExtendedDraft.PackageEffects.Any(p=>p.Id==item.Key);
            var choice=WidgetButton(item.Entry.Title+(added?UiLanguage.Text(" · 已添加"):""),()=>{
                var items=effectSession.ExtendedDraft.PackageEffects;
                if(items.Length>=16){ShowError(UiLanguage.Text("同一画面最多添加 16 个扩展效果。"));return;}
                CloseEffectsAdd(true);SavePackageEffects([..items,new(item.Key)]);
            });
            choice.IsEnabled=!added;choice.MinHeight=112;EffectsAddList.Children.Add(choice);
        }
        LayoutModalChoices(EffectsAddList);LayoutModalChoices(EffectsAddAuxiliaryList);EffectsAddScroll.ChangeView(null,0,null,true);
        EffectsAddOverlay.Visibility=Visibility.Visible;EffectsBodyHost.IsEnabled=ScreenPicker.IsEnabled=false;
        EffectsAddCloseButton.Focus(FocusState.Programmatic);
    }
    private void SavePackageEffects(PackageEffectSetting[] effects){
        effectSession.ExtendedDraft=effectSession.ExtendedDraft with {PackageEffects=effects};
        DraftChanged();RefreshEffectOverview();
    }
    private async void EditPackageEffect(PackageEffectSetting setting,string title){
        var scope=effectSession.Key;
        var enabled=new ToggleSwitch {Header=UiLanguage.Text("启用效果"),IsOn=setting.Enabled};
        var strength=new Slider {Header=UiLanguage.Text("强度"),Minimum=0,Maximum=100,Value=setting.Strength*100};
        var panel=new StackPanel {Spacing=12};panel.Children.Add(enabled);panel.Children.Add(strength);
        var dialog=new ContentDialog {Title=title,Content=panel,PrimaryButtonText=UiLanguage.Text("应用"),SecondaryButtonText=UiLanguage.Text("移除"),CloseButtonText=UiLanguage.Text("取消"),XamlRoot=Root.XamlRoot,Style=HomeStyle("LibraryDialog")};
        var choice=await dialog.ShowAsync();if(choice==ContentDialogResult.None||scope!=effectSession.Key)return;
        var items=effectSession.ExtendedDraft.PackageEffects;
        SavePackageEffects(choice==ContentDialogResult.Secondary?items.Where(p=>p.Id!=setting.Id).ToArray()
            :items.Select(p=>p.Id==setting.Id?p with {Enabled=enabled.IsOn,Strength=strength.Value/100}:p).ToArray());
    }
    private void AddEffect(EffectKind kind)
    {
        if(SelectedScreen==null||(effectSession.Collection.Added&kind)!=0)return;
        RememberNavigation();wallpapers?.ClearEffectSolo();
        loadingEffects=true;
        try {
            effectSession.Collection=effectSession.Collection with {Added=effectSession.Collection.Added|kind};
            var definition=effectDefinitions.First(d=>d.Kind==kind);
            if(definition.Enabled!=null)definition.Enabled.IsOn=true;else effectSession.Collection=effectSession.Collection with {ColorEnabled=true};
            if(kind==EffectKind.HideTaskbar)effectSession.Collection=effectSession.Collection with {HideTaskbarEnabled=true};
            if(kind==EffectKind.Noise&&NoiseSlider.Value==0)NoiseSlider.Value=.5;
        } finally {loadingEffects=false;}
        selectedEffectKind=kind;CloseEffectsAdd(false);DraftChanged();EffectsParameterScroll.ChangeView(null,0,null,true);effectEnabled.Focus(FocusState.Programmatic);AnimatePageEntry("display");
    }
    private void EffectsRemove_Click(object sender,RoutedEventArgs e)
    {
        if(selectedEffectKind is { } kind)RemoveEffect(kind);
    }
    private void RemoveEffect(EffectKind kind)
    {
        if((effectSession.Collection.Added&kind)==0)return;
        int position=Array.FindIndex(effectDefinitions.Where(d=>(effectSession.Collection.Added&d.Kind)!=0).ToArray(),d=>d.Kind==kind);
        bool detail=selectedEffectKind==kind;FlushEffectChanges();wallpapers?.ClearEffectSolo();
        loadingEffects=true;
        try {
            var definition=effectDefinitions.First(d=>d.Kind==kind);
            if(definition.Enabled!=null)definition.Enabled.IsOn=false;
            effectSession.Collection=effectSession.Collection with {Added=effectSession.Collection.Added&~kind};
        } finally {loadingEffects=false;}
        if(detail)selectedEffectKind=null;effectsOverviewKey="";DraftChanged();FlushEffectChanges();
        if(detail)EffectsParameterScroll.ChangeView(null,0,null,true);
        DispatcherQueue.TryEnqueue(()=> {var cards=effectDefinitions.Where(d=>effectOverviewControls.ContainsKey(d.Kind)).Select(d=>effectOverviewControls[d.Kind].Button).ToArray();(cards.Length==0?EffectsAddButton:cards[Math.Clamp(position,0,cards.Length-1)]).Focus(FocusState.Programmatic);});
    }
    private void EffectsAddClose_Click(object sender,RoutedEventArgs e)=>CloseEffectsAdd(true);
    private void CloseEffectsAdd(bool restoreFocus)
    {
        if(!effectsPageReady||EffectsAddOverlay.Visibility!=Visibility.Visible)return;
        EffectsAddOverlay.Visibility=Visibility.Collapsed;EffectsBodyHost.IsEnabled=ScreenPicker.IsEnabled=true;
        if(restoreFocus&&DisplayPanel.Visibility==Visibility.Visible)EffectsAddButton.Focus(FocusState.Programmatic);
    }
    private void EffectsPage_KeyDown(object sender,KeyRoutedEventArgs e)
    {
        if(e.Key==VirtualKey.Escape&&EffectsTargetOverlay.Visibility==Visibility.Visible) {CloseEffectsTarget(true);e.Handled=true;}
        else if(e.Key==VirtualKey.Escape&&EffectsAddOverlay.Visibility==Visibility.Visible) {CloseEffectsAdd(true);e.Handled=true;}
        else if(e.Key==VirtualKey.Escape&&QueueOverlay.Visibility==Visibility.Visible) {CloseQueue(true);e.Handled=true;}
    }
    private void SetEffectsSaveStatus(string message)
    {
        if(EffectsDraftStatus.Text==message)return;
        EffectsDraftStatus.Text=message;
        if(effectSaveStatus!=null) {effectSaveStatus.Text=message;effectSaveStatus.Visibility=Visibility.Collapsed;}
    }
    private void RefreshEffectsContext()
    {
        if(!effectsPageReady)return;
        RefreshEffectScope();
        bool editable=SelectedScreen!=null;
        EffectsEmptyState.Visibility=editable?Visibility.Collapsed:Visibility.Visible;
        EffectsAddButton.IsEnabled=EffectsDetailPanel.IsEnabled=editable;
        EffectsCollectionHeader.Visibility=EffectsListPanel.Visibility=editable?Visibility.Visible:Visibility.Collapsed;
        RefreshEffectAuxiliaryVisibility();
        EffectsCountText.Visibility=editable&&selectedEffectKind==null?Visibility.Visible:Visibility.Collapsed;
        SyncEffectOverviewControls();
        EffectsServiceLabel.Text=menu==null?UiLanguage.Text("桌面服务准备中"):UiLanguage.Text("桌面服务运行中");
        EffectsServiceDot.Fill=menu==null?EffectsBrush(120,120,120):EffectsBrush(23,220,75);
    }
    private void EffectsPage_SizeChanged(object sender,SizeChangedEventArgs e)
    {
        if(!effectsPageReady)return;
        LayoutShell(e.NewSize.Width);ResizeHomeBackdrop();bool compact=e.NewSize.Width<1060;
        LayoutCassetteNavigation(e.NewSize.Width);
        PageSubtitle.Visibility=e.NewSize.Width<1360?Visibility.Collapsed:Visibility.Visible;
        EffectsBody.Margin=new Thickness(compact?12:24,16,compact?12:24,22);
        LayoutCenteredModals();LayoutEffectsPage();
    }
}
