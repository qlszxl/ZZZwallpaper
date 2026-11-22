namespace LumaWall.Contracts;

// Pure layout in physical pixels. Each window keeps its natural size; long rows
// and columns wrap to the available desktop instead of stretching the controls.
public static class WidgetGroupLayout
{
    public sealed record Item(string Id,int Width,int Height);
    public sealed record Slot(string Id,int X,int Y,int Width,int Height);
    public sealed record Result(int Width,int Height,Slot[] Slots);
    public static Result Arrange(WidgetLayoutGroup group,Item[] items,int availableWidth,int availableHeight,int gap)
    {
        if(items.Length==0)return new(0,0,[]);
        availableWidth=Math.Max(1,availableWidth);availableHeight=Math.Max(1,availableHeight);
        int Align(int room,WidgetAlignment alignment)=>alignment switch {WidgetAlignment.Center=>room/2,WidgetAlignment.End=>room,_=>0};
        Result Grid(int spacing,int requestedColumns) {
            int cw=items.Max(i=>i.Width),ch=items.Max(i=>i.Height);
            int fits=Math.Max(1,(availableWidth+spacing)/(cw+spacing));
            int columns=Math.Min(items.Length,Math.Min(requestedColumns,fits));
            while(columns<Math.Min(items.Length,fits)&&((items.Length+columns-1)/columns)*(ch+spacing)-spacing>availableHeight)columns++;
            int rows=(items.Length+columns-1)/columns;
            return new(columns*(cw+spacing)-spacing,rows*(ch+spacing)-spacing,items.Select((i,n)=>new Slot(i.Id,n%columns*(cw+spacing)+Align(cw-i.Width,group.HorizontalAlignment),n/columns*(ch+spacing)+Align(ch-i.Height,group.VerticalAlignment),i.Width,i.Height)).ToArray());
        }
        Result Flow(int spacing) {
            if(group.Flow==WidgetGroupFlow.Grid)return Grid(spacing,group.Columns);
            bool horizontal=group.Flow==WidgetGroupFlow.Horizontal;
            int limit=horizontal?availableWidth:availableHeight;
            var lines=new List<List<Item>>();var line=new List<Item>();int used=0;
            foreach(var item in items) {
                int size=horizontal?item.Width:item.Height;
                if(line.Count>0&&used+spacing+size>limit) {lines.Add(line);line=[];used=0;}
                used+=(line.Count>0?spacing:0)+size;line.Add(item);
            }
            if(line.Count>0)lines.Add(line);
            int Along(List<Item> l)=>l.Sum(i=>horizontal?i.Width:i.Height)+spacing*(l.Count-1);
            int Across(List<Item> l)=>l.Max(i=>horizontal?i.Height:i.Width);
            int span=lines.Max(Along),cross=lines.Sum(Across)+spacing*(lines.Count-1),offset=0;
            var slots=new List<Slot>();
            foreach(var l in lines) {
                int cursor=Align(span-Along(l),horizontal?group.HorizontalAlignment:group.VerticalAlignment),breadth=Across(l);
                foreach(var i in l) {
                    int secondary=offset+Align(breadth-(horizontal?i.Height:i.Width),horizontal?group.VerticalAlignment:group.HorizontalAlignment);
                    slots.Add(new(i.Id,horizontal?cursor:secondary,horizontal?secondary:cursor,i.Width,i.Height));cursor+=(horizontal?i.Width:i.Height)+spacing;
                }
                offset+=breadth+spacing;
            }
            return new(horizontal?span:cross,horizontal?cross:span,slots.ToArray());
        }
        // First retain the chosen direction, then tighten spacing if needed.
        Result result=Flow(gap);
        for(int spacing=gap-1;(result.Width>availableWidth||result.Height>availableHeight)&&spacing>=0;spacing--)result=Flow(spacing);
        if(result.Width>availableWidth||result.Height>availableHeight)result=Grid(0,items.Length);
        return result;
    }
}
