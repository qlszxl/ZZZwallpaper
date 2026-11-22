using LumaWall.Contracts;
using LumaWall.WidgetSupport;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using PixelRect=System.Drawing.Rectangle;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private FrameworkElement WidgetPositionAlignment(string id,bool isGroup,Action<bool,double> move,out Action refresh)
    {
        var picker=new ComboBox {Style=HomeStyle("UtilityComboBox"),HorizontalAlignment=HorizontalAlignment.Stretch};
        string label=UiLanguage.Text("对齐参照物");
        AutomationProperties.SetName(picker,label);
        var panel=new StackPanel {Spacing=12};
        panel.Children.Add(WidgetField(label,picker));
        PixelRect area=default,body=default,reference=default;
        string referenceId="";
        bool updating=false;
        (string Id,string Name)[] options=[];

        // Both bodies are measured in desktop pixels, including the work-area origin.
        double Offset(bool horizontal,double fraction)=>horizontal
            ? reference.Left-area.Left+(reference.Width-body.Width)*fraction
            : reference.Top-area.Top+(reference.Height-body.Height)*fraction;
        bool IsAligned(bool horizontal,double fraction)=>Math.Abs(Offset(horizontal,fraction)-
            (horizontal?body.Left-area.Left:body.Top-area.Top))<=.5;
        FrameworkElement? buttons=null;
        buttons=WidgetAlignmentMenu(Apply,IsAligned);
        panel.Children.Add(buttons);

        void Refresh()
        {
            if(updating||widgets==null)return;
            var state=ExpansionCatalog.AvailableWidgets(widgets.State);
            var group=isGroup?state.LayoutGroups.FirstOrDefault(g=>g.Id==id):null;
            var source=isGroup?null:state.Widgets.FirstOrDefault(w=>w.Id==id);
            if(source==null&&group==null) {panel.Visibility=Visibility.Collapsed;return;}
            panel.Visibility=Visibility.Visible;
            var screen=WidgetPlacement.Resolve(group?.Screen??source?.Screen);
            if(group!=null) {
                area=screen.WorkingArea;
                var bounds=WidgetPlacement.MeasureGroup(state,group);
                body=new(bounds.Left,bounds.Top,bounds.Width,bounds.Height);
            } else {
                area=WidgetPlacement.Area(source!,screen);
                body=WidgetPlacement.BodyBounds(source!,screen);
            }
            var choices=new List<(string Id,string Name)> {("",UiLanguage.Text("显示器"))};
            WidgetInstance? target=null;
            foreach(var w in state.Widgets) {
                if(!w.Enabled||(!isGroup&&w.Id==id)||(isGroup&&w.LayoutGroupId==id))continue;
                var parent=w.LayoutGroupId==null?null:state.LayoutGroups.FirstOrDefault(g=>g.Id==w.LayoutGroupId);
                if(WidgetPlacement.Resolve(parent==null?w.Screen:parent.Screen).DeviceName!=screen.DeviceName)continue;
                string name=WidgetDisplayName(w,state);
                choices.Add((w.Id,parent==null?name:parent.Name+" · "+name));
                if(w.Id==referenceId)target=w;
            }
            if(target==null)referenceId="";
            reference=target==null?area:WidgetPlacement.BodyBounds(WidgetPlacement.Effective(target,state),screen);
            if(!choices.SequenceEqual(options)) {
                updating=true;
                try {
                    options=choices.ToArray();
                    var repeated=options.Skip(1).GroupBy(item=>item.Name).Where(g=>g.Count()>1).ToDictionary(g=>g.Key,_=>0);
                    picker.Items.Clear();
                    foreach(var option in options) {
                        string name=option.Name;
                        if(option.Id.Length>0&&repeated.ContainsKey(name))name+=" · "+(++repeated[option.Name]);
                        picker.Items.Add(new ComboBoxItem {Content=name,Tag=option.Id});
                    }
                    picker.SelectedIndex=Array.FindIndex(options,o=>o.Id==referenceId);
                } finally {updating=false;}
            }
            if(buttons?.Tag is Action highlight)highlight();
        }
        void Apply(bool horizontal,double fraction)
        {
            string selected=referenceId;
            Refresh();
            if(panel.Visibility!=Visibility.Visible||selected!=referenceId)return;
            int travel=horizontal?area.Width-body.Width:area.Height-body.Height;
            move(horizontal,travel<=0?0:Math.Clamp(Offset(horizontal,fraction)/travel,0,1));
        }
        picker.SelectionChanged+=(_,_)=> {
            if(updating||picker.SelectedItem is not ComboBoxItem {Tag:string selected})return;
            referenceId=selected;
            Refresh();
        };
        picker.DropDownOpened+=(_,_)=>Refresh();
        refresh=Refresh;
        return panel;
    }
}
