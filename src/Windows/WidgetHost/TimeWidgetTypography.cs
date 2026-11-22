using System.IO;
using LumaWall.Contracts;
using W=System.Windows;
using M=System.Windows.Media;

namespace LumaWall.WidgetHost;

// Read the packaged face directly. Family-name resolution must never substitute a
// system font for the large display numbers or the Chinese heavy headings.
internal sealed class TimeWidgetTypography
{
    private readonly M.GlyphTypeface digits,digitsMedium,heavy,body;
    private readonly Dictionary<(string,bool,bool,double,double,int),(M.Geometry Shape,W.Rect Bounds)> outlines=new();
    internal TimeWidgetTypography(string assets) {
        M.GlyphTypeface Font(string name)=>new(new Uri(Path.Combine(assets,"Fonts",name),UriKind.Absolute));
        digits=Font("SairaCondensed-Black.ttf");digitsMedium=Font("SairaCondensed-Medium.ttf");heavy=Font("SourceHanSansSC-Heavy.otf");body=Font("SourceHanSansSC-Medium.otf");
    }
    private (M.Geometry Shape,W.Rect Bounds) Outline(string text,bool number,bool bold,bool italic,TimeWidgetAppearance detail) {
        double slant=italic?Math.Tan((number?detail.DigitSlant:detail.TextSlant)*Math.PI/180):0,spacing=number?detail.DigitSpacing:2;
        var key=(text,number,bold,slant,spacing,detail.DigitFont);if(outlines.TryGetValue(key,out var cached))return cached;
        var group=new M.GeometryGroup();double x=0;
        foreach(var rune in text.EnumerateRunes()) {
            var font=number&&rune.Value<128&&detail.DigitFont==0?(bold?digits:digitsMedium):bold?heavy:body;
            if(!font.CharacterToGlyphMap.TryGetValue(rune.Value,out var id))id=0;
            var shape=font.GetGlyphOutline(id,100,100).Clone();
            shape.Transform=new M.TranslateTransform(x,0);group.Children.Add(shape);
            x+=font.AdvanceWidths[id]*100+spacing;
        }
        group.Transform=new M.MatrixTransform(1,0,-slant,1,0,0);group.Freeze();
        var bounds=group.Bounds;
        // Use tabular advance boxes so ticking from 11 to 12 cannot resize all digits.
        if(number&&x>spacing) {var face=detail.DigitFont==0?(bold?digits:digitsMedium):(bold?heavy:body);var zero=face.GetGlyphOutline(face.CharacterToGlyphMap['0'],100,100).Bounds;bounds=group.Transform.TransformBounds(new W.Rect(0,zero.Y,x-spacing,zero.Height));}
        var result=(Shape:(M.Geometry)group,Bounds:bounds);
        if(outlines.Count>=160)outlines.Clear();outlines[key]=result;return result;
    }
    internal void Draw(M.DrawingContext dc,string text,W.Rect slot,double height,M.Brush ink,bool number=false,bool bold=true,int alignment=1,bool fillWidth=false,bool shadow=false,bool italic=true,TimeWidgetAppearance? details=null) {
        if(string.IsNullOrEmpty(text)||slot.Width<=0||slot.Height<=0)return;
        details??=new();var (geometry,bounds)=Outline(text,number,bold,italic,details);if(bounds.IsEmpty||bounds.Height<=0||bounds.Width<=0)return;
        double sy=Math.Min(height,slot.Height)/bounds.Height;
        double sx=fillWidth?slot.Width/bounds.Width:Math.Min(sy,slot.Width/bounds.Width);
        if(!fillWidth)sy=Math.Min(sy,sx);
        double x=slot.X+(alignment==0?0:alignment==2?slot.Width-bounds.Width*sx:(slot.Width-bounds.Width*sx)/2);
        double y=slot.Y+(slot.Height-bounds.Height*sy)/2;
        var matrix=new M.Matrix(sx,0,0,sy,x-bounds.X*sx,y-bounds.Y*sy);
        if(shadow) {
            if(details.NumberShadow) {var offset=matrix;offset.Translate(0,details.ShadowDistance);dc.PushTransform(new M.MatrixTransform(offset));dc.DrawGeometry(new M.SolidColorBrush(M.Color.FromArgb((byte)Math.Round(details.ShadowOpacity*255),0,0,0)),null,geometry);dc.Pop();}
            if(details.NumberGradient&&ink is M.SolidColorBrush solid) {var c=solid.Color;ink=new M.LinearGradientBrush(c,M.Color.FromArgb(c.A,(byte)(c.R*.91),(byte)(c.G*.91),(byte)(c.B*.91)),90);}
        }
        dc.PushTransform(new M.MatrixTransform(matrix));dc.DrawGeometry(ink,null,geometry);dc.Pop();
    }
}
