using LumaWall.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace LumaWall.ControlCenter;

internal sealed class LayoutGridPicker : StackPanel
{
    private readonly TextBlock summary=new() {Style=StyleOf("UtilityLabel")};
    private readonly Grid matrix=new() {ColumnSpacing=8,RowSpacing=8,MaxWidth=344,HorizontalAlignment=HorizontalAlignment.Left};
    private readonly Dictionary<WidgetGroupFlow,Button> flows=new();
    private WidgetGroupFlow flow;
    private int columns=2,count;
    private string layoutKey="";
    internal event Action<WidgetGroupFlow,int>? Changed;
    private static Style StyleOf(string name)=>(Style)Microsoft.UI.Xaml.Application.Current.Resources[name];
    internal LayoutGridPicker()
    {
        Spacing=16;var actions=new Grid {ColumnSpacing=8};
        foreach(var value in Enum.GetValues<WidgetGroupFlow>()) {
            actions.ColumnDefinitions.Add(new());
            var button=new Button {Style=StyleOf("UtilityButton"),Content=value switch {WidgetGroupFlow.Horizontal=>UiLanguage.Text("横向"),WidgetGroupFlow.Vertical=>UiLanguage.Text("纵向"),_=>UiLanguage.Text("网格")},HorizontalAlignment=HorizontalAlignment.Stretch};
            AutomationProperties.SetName(button,UiLanguage.Text("排列方式：")+button.Content);Grid.SetColumn(button,flows.Count);flows.Add(value,button);actions.Children.Add(button);
            button.Click+=(_,_)=>Changed?.Invoke(value,columns);
        }
        Children.Add(actions);Children.Add(summary);Children.Add(matrix);
        for(int i=0;i<8;i++)matrix.ColumnDefinitions.Add(new());
        SizeChanged+=(_,e)=>matrix.Width=Math.Max(1,Math.Min(344,e.NewSize.Width));
    }
    internal void Update(WidgetGroupFlow value,int columnCount,int itemCount)
    {
        string key=$"{value}:{columnCount}:{itemCount}";if(key==layoutKey)return;layoutKey=key;
        flow=value;columns=Math.Clamp(columnCount,1,8);count=Math.Max(0,itemCount);
        foreach(var pair in flows)ChoiceSelection.SetIsSelected(pair.Value,pair.Key==flow);
        int actual=flow==WidgetGroupFlow.Horizontal?Math.Max(1,count):flow==WidgetGroupFlow.Vertical?1:columns;
        summary.Text=count==0?UiLanguage.Text("暂无项目"):UiLanguage.Format($"{actual} 列 × {(count+actual-1)/actual} 行");
        matrix.Visibility=flow==WidgetGroupFlow.Grid?Visibility.Visible:Visibility.Collapsed;
        matrix.Children.Clear();matrix.RowDefinitions.Clear();
        int rows=Math.Max(4,(count+columns-1)/columns);
        for(int r=0;r<rows;r++) {
            matrix.RowDefinitions.Add(new() {Height=new GridLength(30)});
            for(int c=0;c<8;c++) {
                int selected=c+1;bool filled=c<columns&&r*columns+c<count;
                var cell=new Button {Style=StyleOf("LayoutCellButton"),HorizontalAlignment=HorizontalAlignment.Stretch,VerticalAlignment=VerticalAlignment.Stretch};
                ChoiceSelection.SetIsSelected(cell,filled);
                AutomationProperties.SetName(cell,UiLanguage.Format($"{selected} 列，{(count+selected-1)/selected} 行"));
                cell.Click+=(_,_)=>Changed?.Invoke(WidgetGroupFlow.Grid,selected);
                Grid.SetRow(cell,r);Grid.SetColumn(cell,c);matrix.Children.Add(cell);
            }
        }
    }
}

internal sealed class OrderedTileList : StackPanel
{
    internal sealed record Item(string Id,string Name);
    private readonly Panel tiles;
    private readonly bool preview;
    private readonly Func<Item,Control,FrameworkElement>? createContent;
    private readonly Dictionary<Button,Border> badges=new();
    internal const double PreviewCardWidth=248;
    private WidgetGroupFlow previewFlow;
    private int previewColumns=2;
    private readonly Button previous,next;
    private Item[] items=[];
    private string? selected,dragged;
    private string itemKey="";
    internal string? SelectedId=>selected;
    internal event Action<string,string>? Reordered;
    internal event Action<string>? Selected;
    internal event Action<string>? Invoked;
    internal OrderedTileList(bool preview=false,Func<Item,Control,FrameworkElement>? content=null)
    {
        this.preview=preview;createContent=content;Spacing=12;
        tiles=preview?new Grid {ColumnSpacing=ParameterUi.TileGap,RowSpacing=ParameterUi.TileGap,HorizontalAlignment=HorizontalAlignment.Left}:new ChoiceTilePanel {AspectRatio=1.6};
        var actions=new StackPanel {Orientation=Orientation.Horizontal,Spacing=8,HorizontalAlignment=HorizontalAlignment.Right};
        Button ActionButton(string text,string name,int delta) {
            var button=new Button {Content=text,Style=(Style)Microsoft.UI.Xaml.Application.Current.Resources["UtilityButton"],MinWidth=44,Width=44,Height=44,MinHeight=44,Padding=new Thickness(0),CornerRadius=new CornerRadius(22)};
            AutomationProperties.SetName(button,name);button.Click+=(_,_)=>Move(delta);actions.Children.Add(button);return button;
        }
        previous=ActionButton("←",UiLanguage.Text("前移选中项目"),-1);next=ActionButton("→",UiLanguage.Text("后移选中项目"),1);
        if(preview) {
            HorizontalAlignment=HorizontalAlignment.Left;
            var heading=new Grid {ColumnSpacing=12};heading.ColumnDefinitions.Add(new());heading.ColumnDefinitions.Add(new() {Width=GridLength.Auto});
            heading.Children.Add(new TextBlock {Text=UiLanguage.Text("排列预览"),Style=(Style)Microsoft.UI.Xaml.Application.Current.Resources["UtilityLabel"],VerticalAlignment=VerticalAlignment.Center});
            Grid.SetColumn(actions,1);heading.Children.Add(actions);Children.Add(heading);
            Children.Add(new ScrollViewer {Content=tiles,HorizontalScrollMode=ScrollMode.Enabled,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,
                VerticalScrollMode=ScrollMode.Disabled,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,HorizontalContentAlignment=HorizontalAlignment.Left,IsTabStop=false});
        } else {Children.Add(actions);Children.Add(tiles);}
    }
    internal void UpdateArrangement(WidgetGroupFlow flow,int columns)
    {
        if(previewFlow==flow&&previewColumns==columns)return;
        previewFlow=flow;previewColumns=columns;ArrangePreview();
    }
    private void ArrangePreview()
    {
        if(tiles is not Grid grid)return;
        int columns=Math.Max(1,new PerformanceWidgetOptions {Flow=previewFlow,Columns=Math.Clamp(previewColumns,1,8)}.ColumnCount(items.Length));
        int rows=(items.Length+columns-1)/columns;
        grid.ColumnDefinitions.Clear();grid.RowDefinitions.Clear();
        for(int c=0;c<columns;c++)grid.ColumnDefinitions.Add(new() {Width=new GridLength(PreviewCardWidth)});
        for(int r=0;r<rows;r++)grid.RowDefinitions.Add(new() {Height=GridLength.Auto});
        // Limit the entire preview, including its header actions. Extra page
        // width must never turn these cards into long bars.
        MaxWidth=grid.Width=columns*PreviewCardWidth+(columns-1)*grid.ColumnSpacing;
        for(int i=0;i<grid.Children.Count;i++){Grid.SetColumn((FrameworkElement)grid.Children[i],i%columns);Grid.SetRow((FrameworkElement)grid.Children[i],i/columns);}
    }
    internal void Update(IEnumerable<Item> values)
    {
        var nextItems=values.ToArray();string key=string.Join("\u001f",nextItems.Select(x=>x.Id+"\u001e"+x.Name));
        items=nextItems;if(!items.Any(x=>x.Id==selected))selected=items.FirstOrDefault()?.Id;
        if(itemKey==key){RefreshSelection();return;}
        bool restoreFocus=preview&&tiles.Children.OfType<Button>().Any(b=>b.FocusState!=FocusState.Unfocused);
        itemKey=key;tiles.Children.Clear();badges.Clear();
        foreach(var item in items) {
            var text=new TextBlock {Text=item.Name,TextWrapping=TextWrapping.Wrap,TextAlignment=TextAlignment.Center,TextLineBounds=TextLineBounds.Tight,VerticalAlignment=VerticalAlignment.Center};
            var button=new Button {Content=text,Style=(Style)Microsoft.UI.Xaml.Application.Current.Resources[preview?"OrderCardButton":"OrderTileButton"],CanDrag=true,AllowDrop=true};
            if(preview) {
                var content=new Grid {ColumnSpacing=ParameterUi.TileGap};content.ColumnDefinitions.Add(new());content.ColumnDefinitions.Add(new() {Width=GridLength.Auto});
                text.TextAlignment=TextAlignment.Left;content.Children.Add(createContent?.Invoke(item,button)??text);
                var number=new TextBlock {Text=(Array.IndexOf(items,item)+1).ToString("00"),FontFamily=UiFonts.Body,FontSize=12,
                    HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
                var badge=new Border {Width=24,Height=24,CornerRadius=new CornerRadius(5),BorderThickness=new Thickness(1),Child=number,
                    VerticalAlignment=VerticalAlignment.Center,IsHitTestVisible=false};
                Grid.SetColumn(badge,1);content.Children.Add(badge);badges.Add(button,badge);button.Content=content;
                AutomationProperties.SetHelpText(button,UiLanguage.Text("拖动调整顺序，或选中后使用前移、后移按钮。"));
            }
            AutomationProperties.SetName(button,item.Name);button.Tag=item.Id;
            button.Click+=(_,_)=>{selected=item.Id;RefreshSelection();Selected?.Invoke(item.Id);};
            button.DoubleTapped+=(_,e)=>{Invoked?.Invoke(item.Id);e.Handled=true;};
            button.KeyDown+=(_,e)=>{if(e.Key is VirtualKey.Left or VirtualKey.Right){selected=item.Id;Move(e.Key==VirtualKey.Left?-1:1);e.Handled=true;}};
            button.DragStarting+=(_,e)=>{dragged=item.Id;if(preview){selected=item.Id;RefreshSelection();}e.Data.SetText(item.Id);e.Data.RequestedOperation=DataPackageOperation.Move;};
            button.DragOver+=(_,e)=>{if(dragged!=null&&dragged!=item.Id){e.AcceptedOperation=DataPackageOperation.Move;e.Handled=true;}};
            button.Drop+=(_,e)=>{var source=dragged;dragged=null;if(source!=null&&source!=item.Id){selected=source;Reordered?.Invoke(source,item.Id);e.Handled=true;}};
            button.DropCompleted+=(_,_)=>dragged=null;tiles.Children.Add(button);
        }
        ArrangePreview();RefreshSelection();
        if(restoreFocus)tiles.Children.OfType<Button>().FirstOrDefault(b=>(string)b.Tag==selected)?.Focus(FocusState.Programmatic);
    }
    private void Move(int direction)
    {
        int index=Array.FindIndex(items,x=>x.Id==selected),target=index+direction;
        if(index>=0&&target>=0&&target<items.Length)Reordered?.Invoke(items[index].Id,items[target].Id);
    }
    private void RefreshSelection()
    {
        int index=Array.FindIndex(items,x=>x.Id==selected);previous.IsEnabled=index>0;next.IsEnabled=index>=0&&index<items.Length-1;
        foreach(var button in tiles.Children.OfType<Button>()) {
            bool active=(string)button.Tag==selected;
            if(!preview){ChoiceSelection.SetIsSelected(button,active);continue;}
            var badge=badges[button];
            badge.Background=(Brush)Microsoft.UI.Xaml.Application.Current.Resources[active?"InteractionAccentBrush":"HomeControlRecess"];
            badge.BorderBrush=(Brush)Microsoft.UI.Xaml.Application.Current.Resources[active?"InteractionAccentBrush":"HomeFrameEdgeHoverBrush"];
            ((TextBlock)badge.Child).Foreground=(Brush)Microsoft.UI.Xaml.Application.Current.Resources[active?"HomeControlInk":"HomeControlMuted"];
            AutomationProperties.SetItemStatus(button,active?UiLanguage.Text("已选中排序项"):UiLanguage.Text("未选中排序项"));
        }
    }
}
