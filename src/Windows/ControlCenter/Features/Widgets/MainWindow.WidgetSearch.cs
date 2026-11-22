using LumaWall.Contracts;
using LumaWall.WidgetSupport;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private Action BuildSearchSettings(WidgetInstance widget)
    {
        var enginePanel=new StackPanel {Spacing=14};enginePanel.Children.Add(UtilityText(UiLanguage.Text("搜索引擎"),"UtilityHeading"));
        var engine=new ComboBox {Style=HomeStyle("UtilityComboBox")};
        foreach(var item in Enum.GetValues<SearchEngine>())engine.Items.Add(SearchWidgetOptions.Label(item));
        engine.SelectedIndex=(int)widget.Search.Engine;enginePanel.Children.Add(engine);
        var custom=new TextBox {Style=HomeStyle("UtilityTextBox"),Text=widget.Search.CustomUrl,MaxLength=4096,PlaceholderText="https://example.com/search?q={query}"};
        var customPanel=new StackPanel {Spacing=10};customPanel.Children.Add(WidgetField(UiLanguage.Text("搜索地址"),custom));
        
        var save=WidgetButton(UiLanguage.Text("保存搜索引擎"),()=> {
            if(engine.SelectedIndex<0)return;
            LiveWidget(widget.Id,w=>w with {Search=w.Search with {Engine=(SearchEngine)engine.SelectedIndex,CustomUrl=custom.Text.Trim()}});
        });customPanel.Children.Add(save);enginePanel.Children.Add(customPanel);WidgetEditorPanel.Children.Add(UtilityCard(enginePanel));
        void ShowCustom()=>customPanel.Visibility=engine.SelectedIndex==(int)SearchEngine.Custom?Visibility.Visible:Visibility.Collapsed;
        bool selectingCustom=widget.Search.Engine==SearchEngine.Custom;
        engine.SelectionChanged+=(_,_)=> {
            ShowCustom();if(syncingWidgetEditor||engine.SelectedIndex<0)return;
            selectingCustom=engine.SelectedIndex==(int)SearchEngine.Custom;
            if(!selectingCustom)LiveWidget(widget.Id,w=>w with {Search=w.Search with {Engine=(SearchEngine)engine.SelectedIndex}});
        };
        ShowCustom();
        var panel=new StackPanel {Spacing=16};panel.Children.Add(UtilityText(UiLanguage.Text("搜索框尺寸"),"UtilityHeading"));
        WidgetEditorPanel.Children.Add(UtilityCard(panel));
        var screen=WidgetPlacement.Resolve(widgets!.State.LayoutGroups.FirstOrDefault(g=>g.Id==widget.LayoutGroupId)?.Screen??widget.Screen);
        var max=WidgetPlacement.MaximumSize(widget,screen);
        Slider Add(string title,double min,double max,double value,Func<SearchWidgetOptions,double,SearchWidgetOptions> change)
        {
            var slider=new Slider {Style=HomeStyle("HomeSlider"),Minimum=min,Maximum=Math.Max(min,max),StepFrequency=1,Value=Math.Clamp(value,min,Math.Max(min,max))};
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(slider,UiLanguage.Text("搜索框")+title);
            panel.Children.Add(NumericSliderRow(title,slider,"DIP",0));
            slider.ValueChanged+=(_,_)=>LiveWidget(widget.Id,w=>w with {Search=change(w.Search,slider.Value)});return slider;
        }
        var width=Add(UiLanguage.Text("宽度"),120,max.Width,widget.Search.Width,(s,v)=>s with {Width=(int)v});
        var height=Add(UiLanguage.Text("高度"),44,max.Height,widget.Search.Height,(s,v)=>s with {Height=(int)v});
        var radius=Add(UiLanguage.Text("圆角"),0,Math.Min(width.Value,height.Value)/2,widget.Search.Radius,(s,v)=>s with {Radius=v});
        return ()=> {
            var current=widgets?.State.Widgets.FirstOrDefault(w=>w.Id==widget.Id);if(current==null)return;
            if(!selectingCustom||current.Search.Engine==SearchEngine.Custom)engine.SelectedIndex=(int)current.Search.Engine;
            if(custom.FocusState==FocusState.Unfocused&&!selectingCustom)custom.Text=current.Search.CustomUrl;
            ShowCustom();
            var target=WidgetPlacement.Resolve(widgets!.State.LayoutGroups.FirstOrDefault(g=>g.Id==current.LayoutGroupId)?.Screen??current.Screen);
            var bounds=WidgetPlacement.MaximumSize(current,target);
            width.Maximum=Math.Max(width.Minimum,bounds.Width);height.Maximum=Math.Max(height.Minimum,bounds.Height);
            width.Value=Math.Clamp(current.Search.Width,width.Minimum,width.Maximum);height.Value=Math.Clamp(current.Search.Height,height.Minimum,height.Maximum);
            radius.Maximum=Math.Min(width.Value,height.Value)/2;radius.Value=Math.Min(current.Search.Radius,radius.Maximum);
        };
    }
}
