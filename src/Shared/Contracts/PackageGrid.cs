namespace LumaWall.Contracts;
public sealed record PackageGrid(string Title,int DefaultRows,int MinRows,int MaxRows,int DesignWidth,int MinWidth,int MaxWidth,double RowHeight,double HeaderHeight,double Overflow)
{
    public string? Description {get;init;}
    public void Validate(){
        if(string.IsNullOrWhiteSpace(Title)||MinRows<1||MaxRows>128||DefaultRows<MinRows||DefaultRows>MaxRows||MinWidth<1
            ||MaxWidth>16384||DesignWidth<MinWidth||DesignWidth>MaxWidth||!double.IsFinite(RowHeight)||RowHeight<=0||RowHeight>8192
            ||!double.IsFinite(HeaderHeight)||HeaderHeight<0||HeaderHeight>8192||!double.IsFinite(Overflow)||Overflow<0||Overflow>DesignWidth)
            throw new InvalidDataException("扩展组件尺寸定义无效。");
    }
    public GridWidgetOptions Options(WidgetInstance widget)=>widget.GridOptions??new(DefaultRows,DesignWidth);
    public (double Width,double Height) Size(WidgetInstance widget){
        var options=Options(widget);
        var width=Math.Clamp(options.Width,MinWidth,MaxWidth);
        return(width,width/(double)DesignWidth*(HeaderHeight+RowHeight*Math.Clamp(options.Rows,MinRows,MaxRows)));
    }
}
public sealed record GridWidgetOptions(int Rows,int Width);
