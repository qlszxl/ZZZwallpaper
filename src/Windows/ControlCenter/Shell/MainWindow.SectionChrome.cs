using LumaWall.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private static bool IsSectionHeading(TextBlock text)=>text.Style==HomeStyle("UtilityHeading")||text.Style==HomeStyle("EffectsHeading");
    private static string SectionIcon(string name)=>name switch {
        var s when s.Contains(UiLanguage.Text("位置"))||s.Contains(UiLanguage.Text("尺寸"))||s.Contains(UiLanguage.Text("排列"))||s.Contains(UiLanguage.Text("对齐"))=>"Compare",
        var s when s.Contains(UiLanguage.Text("搜索"))=>"Search",
        var s when s.Contains(UiLanguage.Text("颜色"))||s.Contains(UiLanguage.Text("色彩"))||s.Contains(UiLanguage.Text("外观"))||s.Contains(UiLanguage.Text("背景"))=>"Color",
        var s when s.Contains(UiLanguage.Text("频谱"))||s.Contains(UiLanguage.Text("音频"))||s.Contains(UiLanguage.Text("声音"))||s.Contains(UiLanguage.Text("响应"))=>"Spectrum",
        var s when s.Contains(UiLanguage.Text("时间"))||s.Contains(UiLanguage.Text("时钟"))||s.Contains(UiLanguage.Text("倒计时"))=>"Clock",
        var s when s.Contains(UiLanguage.Text("文字"))||s.Contains(UiLanguage.Text("文本"))||s.Contains(UiLanguage.Text("名称"))=>"Text",
        var s when s.Contains(UiLanguage.Text("预览"))||s.Contains(UiLanguage.Text("贴纸"))||s.Contains(UiLanguage.Text("图片"))=>"Image",
        var s when s.Contains(UiLanguage.Text("组件"))||s.Contains(UiLanguage.Text("图层"))=>"Widgets",
        _=>"Settings"
    };
    private static void DecorateParameterCard(ContentControl card,string fallback)
    {
        if(card.Style==HomeStyle("ParameterSectionCard"))return;
        if(card.Style!=HomeStyle("UtilityCard")&&card.Style!=HomeStyle("EffectsSection"))return;
        string title=SectionChrome.GetTitle(card);
        // Some effect headings have a leading StackPanel for their help text.
        // Follow only that leading branch; never promote a parameter label.
        var first=card.Content as UIElement;
        while(first is StackPanel panel)first=panel.Children.FirstOrDefault();
        TextBlock? heading=first as TextBlock;
        if(heading!=null&&!IsSectionHeading(heading)&&heading.Text!=title)heading=null;
        var inline=first as Grid;
        heading??=inline?.Children.OfType<TextBlock>().FirstOrDefault(IsSectionHeading);
        if(title.Length==0)title=heading?.Text??fallback;
        if(string.IsNullOrWhiteSpace(title))title=fallback;
        SectionChrome.SetTitle(card,title);card.Style=HomeStyle("ParameterSectionCard");
        if(card.Content is StackPanel fields)fields.Spacing=ParameterUi.FieldGap;
        if(SectionChrome.GetHasOwnHeader(card))return;
        if(heading!=null)card.SetBinding(SectionChrome.TitleProperty,new Binding {Source=heading,Path=new PropertyPath("Text")});
        var row=new Grid {ColumnSpacing=8,MinHeight=24};
        row.ColumnDefinitions.Add(new() {Width=GridLength.Auto});row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new() {Width=GridLength.Auto});
        var label=UtilityText(title,"UtilityHeading");label.VerticalAlignment=VerticalAlignment.Center;
        label.TextWrapping=TextWrapping.Wrap;
        if(heading!=null) {
            label.SetBinding(TextBlock.TextProperty,new Binding {Source=heading,Path=new PropertyPath("Text")});heading.Visibility=Visibility.Collapsed;
        }
        row.Children.Add(UtilityIcon(SectionIcon(title),label,20));Grid.SetColumn(label,1);row.Children.Add(label);
        var toggle=new Button {Style=HomeStyle("ParameterSectionToggle"),Content=row,Padding=new Thickness(24),MinHeight=80,
            HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch};
        // Match the preview card: header interaction outlines the whole section.
        InteractionFocus.SetFeedbackSurface(toggle,card);
        AutomationProperties.SetAutomationId(toggle,"ParameterSectionToggle");
        var arrow=CreateHomeVectorIcon("ChevronDown",16,toggle);
        arrow.Name="ParameterSectionChevron";arrow.IsHitTestVisible=false;arrow.RenderTransformOrigin=new Windows.Foundation.Point(.5,.5);
        var rotation=new RotateTransform();arrow.RenderTransform=rotation;Grid.SetColumn(arrow,2);row.Children.Add(arrow);
        void RefreshHeader() {
            bool expanded=SectionChrome.GetIsExpanded(card);rotation.Angle=expanded?180:0;
            AutomationProperties.SetName(toggle,(expanded?UiLanguage.Text("收起"):UiLanguage.Text("展开"))+SectionChrome.GetTitle(card));
            AutomationProperties.SetItemStatus(toggle,expanded?UiLanguage.Text("已展开"):UiLanguage.Text("已收起"));
        }
        card.RegisterPropertyChangedCallback(SectionChrome.IsExpandedProperty,(_,_)=>RefreshHeader());
        card.RegisterPropertyChangedCallback(SectionChrome.TitleProperty,(_,_)=>RefreshHeader());
        toggle.Click+=(_,_)=>SectionChrome.SetIsExpanded(card,!SectionChrome.GetIsExpanded(card));
        RefreshHeader();
        if(inline!=null&&heading!=null&&inline.Parent is StackPanel parent) {
            // Lift only the existing heading row. Its reset/toggle/add controls
            // remain siblings of the disclosure button and keep their handlers.
            // A collapsed placeholder preserves any indexed parameter access.
            int index=parent.Children.IndexOf(inline);parent.Children.RemoveAt(index);
            parent.Children.Insert(index,new Grid {Visibility=Visibility.Collapsed});
            inline.Margin=new Thickness(0,0,24,0);
            toggle.Padding=new Thickness(24,24,0,24);
            Grid.SetColumn(toggle,Grid.GetColumn(heading));Grid.SetRow(toggle,Grid.GetRow(heading));Grid.SetColumnSpan(toggle,Grid.GetColumnSpan(heading));
            inline.Children.Add(toggle);SectionChrome.SetHeader(card,SectionHeaderWithHelp(inline,card,label));
        } else {
            if(heading?.Parent is StackPanel headerGroup&&!ReferenceEquals(headerGroup,card.Content)&&headerGroup.Children.All(c=>c.Visibility==Visibility.Collapsed))headerGroup.Visibility=Visibility.Collapsed;
            SectionChrome.SetHeader(card,SectionHeaderWithHelp(toggle,card,label));
        }
        SectionChrome.SetHeaderVisibility(card,Visibility.Visible);
    }
    private static void DecorateParameterSections(FrameworkElement root,string title)
    {
        SectionChrome.SetScope(root,title);
        foreach(var card in EffectsLogicalChildren(root).OfType<ContentControl>().ToArray())DecorateParameterCard(card,title);
    }
    private static void DecorateLoadedParameterCard(object sender,RoutedEventArgs args)
    {
        var card=(ContentControl)sender;
        for(var parent=VisualTreeHelper.GetParent(card);parent!=null;parent=VisualTreeHelper.GetParent(parent)) {
            string scope=SectionChrome.GetScope(parent);if(scope.Length==0)continue;
            DecorateParameterCard(card,scope);return;
        }
    }
    private void InitializeParameterSections()
    {
        UtilityButtonContent(EffectsRemoveButton,UiLanguage.Text("移除效果"),"Trash");
        foreach(var definition in effectDefinitions)DecorateParameterSections(definition.Panel,definition.Name+UiLanguage.Text("设置"));
        SectionChrome.SetTitle(EffectsInstanceControls,UiLanguage.Text("效果控制"));DecorateParameterCard(EffectsInstanceControls,UiLanguage.Text("效果控制"));
        SectionChrome.SetScope(EffectsDetailPanel,UiLanguage.Text("效果设置"));
    }
}
