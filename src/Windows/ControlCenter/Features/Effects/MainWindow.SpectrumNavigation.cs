using LumaWall.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private string? spectrumEditorWidgetId;
    private int spectrumEditorSelection=-1; // -2 shared widget settings, -1 main, 0..7 children

    private void CommitSpectrumInput()
    {
        if(FocusManager.GetFocusedElement(Root.XamlRoot) is TextBox text)DoubleClickEdit.Commit(text);
        ScrubNumberInput? scrub=null;EffectValueInput? value=null;NumberBox? number=null;
        for(var node=FocusManager.GetFocusedElement(Root.XamlRoot) as DependencyObject;node!=null&&node!=Root;node=VisualTreeHelper.GetParent(node)) {
            if(node is ScrubNumberInput s)scrub=s;
            if(node is EffectValueInput v)value=v;
            if(node is NumberBox n)number=n;
        }
        if(scrub!=null)scrub.CommitEdit();else if(value!=null)value.CommitEdit();else if(number!=null)NumericCommit.Commit(number);
        widgets?.FlushPendingSave();
    }

    private Action BuildSpectrumSettings(WidgetInstance widget,StackPanel common)
    {
        string id=widget.Id;int editorEpoch=widgetEditorEpoch,viewEpoch=0;
        if(spectrumEditorWidgetId!=id) {spectrumEditorWidgetId=id;spectrumEditorSelection=-1;}
        int selected=spectrumEditorSelection,listedChildren=-1;bool changingLayers=false;
        Action? syncLayer=null;
        bool IsCurrent()=>widgetEditorEpoch==editorEpoch&&selectedWidgetId==id;
        SpectrumOptions Current()=>widgets?.State.Widgets.FirstOrDefault(w=>w.Id==id)?.Spectrum??widget.Spectrum;
        string Name(int key)=>key==-2?UiLanguage.Text("组件整体"):key==-1?UiLanguage.Text("主频谱"):UiLanguage.Format($"子频谱 {key+1}");

        var frame=new StackPanel {Spacing=16};frame.Children.Add(UtilityText(UiLanguage.Text("背景与边距"),"UtilityHeading"));
        var background=WidgetToggle(UiLanguage.Text("显示背景框"),widget.Spectrum.Background);frame.Children.Add(WidgetToggleRow(UiLanguage.Text("显示背景框"),background));
        var padding=new Slider {Style=HomeStyle("HomeSlider"),Minimum=0,Maximum=100,StepFrequency=1,Value=widget.Spectrum.Padding};
        AutomationProperties.SetName(padding,UiLanguage.Text("整个频谱组件的内边距"));frame.Children.Add(NumericSliderRow(UiLanguage.Text("整体内边距"),padding,"DIP",0));
        common.Children.Insert(Math.Max(0,common.Children.Count-1),UtilityCard(frame));
        background.Toggled+=(_,_)=> {if(IsCurrent()&&!syncingWidgetEditor)LiveWidget(id,w=>w with {Spectrum=w.Spectrum with {Background=background.IsOn}});};
        padding.ValueChanged+=(_,_)=> {if(IsCurrent()&&!syncingWidgetEditor)LiveWidget(id,w=>w with {Spectrum=w.Spectrum with {Padding=padding.Value}});};

        var navigation=new StackPanel {Spacing=14};var navigationHeader=new Grid {ColumnSpacing=16};navigationHeader.ColumnDefinitions.Add(new());navigationHeader.ColumnDefinitions.Add(new() {Width=GridLength.Auto});
        var countText=UtilityText(UiLanguage.Text("频谱图层"),"UtilityHeading");countText.VerticalAlignment=VerticalAlignment.Center;navigationHeader.Children.Add(countText);
        var add=WidgetButton(UiLanguage.Text("添加子频谱"),()=>{},"Add");Grid.SetColumn(add,1);navigationHeader.Children.Add(add);navigation.Children.Add(navigationHeader);
        var choices=new Grid {ColumnSpacing=10,RowSpacing=10};
        void LayoutChoices()=>FlowCards(choices,choices.ActualWidth>=900?4:choices.ActualWidth>=600?3:choices.ActualWidth>=360?2:1);
        choices.SizeChanged+=(_,_)=>LayoutChoices();navigation.Children.Add(choices);
        WidgetEditorPanel.Children.Add(UtilityCard(navigation));
        var tabs=new Dictionary<int,(Button Button,TextBlock Detail)>();

        var scope=new StackPanel {Spacing=12};var scopeTitle=UtilityText("","UtilityHeading");scope.Children.Add(scopeTitle);
        var scopeHint=UtilityText("");scope.Children.Add(scopeHint);
        var actions=new Grid {ColumnSpacing=10,RowSpacing=10};
        var up=WidgetButton(UiLanguage.Text("上移一层"),()=>{});var down=WidgetButton(UiLanguage.Text("下移一层"),()=>{});
        var remove=WidgetButton(UiLanguage.Text("删除这一层"),()=>{},"Trash");
        actions.Children.Add(up);actions.Children.Add(down);actions.Children.Add(remove);UtilityAdaptiveGrid(actions,3,480);scope.Children.Add(actions);
        WidgetEditorPanel.Children.Add(UtilityCard(scope));var body=new StackPanel {Spacing=16};WidgetEditorPanel.Children.Add(body);
        add.Click+=(_,_)=> {
            var root=Current();if(root.SubSpectra.Length>=8)return;
            ChangeLayers(a=>a.Append(new SpectrumOptions {Width=root.Width,Height=root.Height,Shape=SpectrumShape.Wave,Color="#FFFFFF",Opacity=.65,Background=false}).ToArray(),root.SubSpectra.Length);
        };
        up.Click+=(_,_)=>Move(-1);down.Click+=(_,_)=>Move(1);
        remove.Click+=(_,_)=> {int index=selected;if(index>=0)ChangeLayers(a=>a.Where((_,i)=>i!=index).ToArray(),index>0?index-1:-1);};

        void RefreshLabels()
        {
            var root=Current();countText.Text=UiLanguage.Format($"频谱图层 · 主频谱 + {root.SubSpectra.Length} 个子频谱");add.IsEnabled=root.SubSpectra.Length<8;
            foreach(var (key,tab) in tabs) {
                bool active=key==selected;ChoiceSelection.SetIsSelected(tab.Button,active);
                AutomationProperties.SetHelpText(tab.Button,active?UiLanguage.Text("正在编辑"):UiLanguage.Text("点击切换编辑对象"));
                if(key==-2)tab.Detail.Text=UiLanguage.Text("位置 · 锁定 · 整体外观");
                else {var layer=key<0?root:key<root.SubSpectra.Length?root.SubSpectra[key]:null;if(layer!=null)tab.Detail.Text=$"{SpectrumShapeNames[(int)layer.Shape]} · {(layer.Enabled?UiLanguage.Text("已显示"):UiLanguage.Text("已隐藏"))}";}
            }
            scopeTitle.Text=UiLanguage.Text("正在编辑 · ")+Name(selected);
            scopeHint.Visibility=Visibility.Collapsed;
            actions.Visibility=selected>=0?Visibility.Visible:Visibility.Collapsed;up.IsEnabled=selected>0;down.IsEnabled=selected>=0&&selected<root.SubSpectra.Length-1;
        }
        void BuildChoices()
        {
            tabs.Clear();choices.Children.Clear();listedChildren=Current().SubSpectra.Length;
            for(int key=-2;key<listedChildren;key++) {
                int target=key;var button=WidgetButton(Name(key),()=>Select(target,true));button.MinHeight=76;button.Padding=new Thickness(12,10,12,10);
                var labels=new StackPanel {Spacing=5,VerticalAlignment=VerticalAlignment.Center};var name=UtilityText(Name(key),"UtilityLabel");name.HorizontalAlignment=HorizontalAlignment.Center;labels.Children.Add(name);
                var detail=UtilityText("");detail.Visibility=Visibility.Collapsed;button.Content=labels;
                foreach(var label in new[]{name,detail})label.SetBinding(TextBlock.ForegroundProperty,new Microsoft.UI.Xaml.Data.Binding {Source=button,Path=new PropertyPath("Foreground")});
                tabs.Add(key,(button,detail));choices.Children.Add(button);
            }
            LayoutChoices();RefreshLabels();
        }
        void Select(int key,bool resetScroll)
        {
            if(!IsCurrent())return;
            CommitSpectrumInput();CloseWidgetDrawer(false);
            if(resetScroll&&selected!=key)RememberNavigation();
            if(activeWidgetDisclosure==id)CaptureWidgetDisclosures();
            selected=key>=-2&&key<Current().SubSpectra.Length?key:-1;spectrumEditorSelection=selected;
            int generation=++viewEpoch;syncLayer=null;body.Children.Clear();
            if(selected==-2)body.Children.Add(common);
            else {
                int target=selected;var root=Current();var layer=target<0?root:root.SubSpectra[target];
                syncLayer=BuildSpectrumLayer(body,id,layer,target<0?null:target,()=>IsCurrent()&&!changingLayers&&viewEpoch==generation&&selected==target);
            }
            DecorateParameterSections(body,UiLanguage.Text("频谱图层 ")+selected);
            if(selected!=-2)foreach(var card in EffectsLogicalChildren(body).OfType<ContentControl>())SectionChrome.SetScope(card,UiLanguage.Text("频谱图层 ")+selected);
            if(activeWidgetDisclosure==id)RestoreWidgetDisclosures(id);
            else foreach(var card in EffectsLogicalChildren(body).OfType<ContentControl>().Where(c=>c.Style==HomeStyle("ParameterSectionCard")))SetDisclosureOpen(card,false);
            RefreshLabels();if(resetScroll)WidgetsScroll.ChangeView(null,0,null,true);
        }
        void ChangeLayers(Func<SpectrumOptions[],SpectrumOptions[]> change,int target)
        {
            if(!IsCurrent()||changingLayers)return;
            CommitSpectrumInput();CloseWidgetDrawer(false);changingLayers=true;viewEpoch++;bool changed;
            try {changed=LiveWidget(id,w=>w with {Spectrum=w.Spectrum with {SubSpectra=change(w.Spectrum.SubSpectra)}});}finally {changingLayers=false;}
            if(!IsCurrent())return;
            BuildChoices();Select(changed?target:selected,true);
        }
        void Move(int offset)
        {
            int index=selected,target=index+offset;if(index<0||target<0||target>=Current().SubSpectra.Length)return;
            ChangeLayers(a=> {var copy=a.ToArray();(copy[index],copy[target])=(copy[target],copy[index]);return copy;},target);
        }
        BuildChoices();Select(selected,false);
        DecorateParameterSections(WidgetEditorPanel,UiLanguage.Text("音频频谱设置"));
        return ()=> {
            if(!IsCurrent()||changingLayers)return;
            if(listedChildren!=Current().SubSpectra.Length) {BuildChoices();Select(selected,false);}
            bool previous=syncingWidgetEditor;syncingWidgetEditor=true;
            try {background.IsOn=Current().Background;padding.Value=Current().Padding;syncLayer?.Invoke();RefreshLabels();}finally {syncingWidgetEditor=previous;}
        };
    }
}

