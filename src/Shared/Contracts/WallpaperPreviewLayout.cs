namespace LumaWall.Contracts;

public readonly record struct PreviewRect(double X,double Y,double Width,double Height);
public readonly record struct WallpaperPreviewLayout(PreviewRect Screen,PreviewRect Content)
{
    public static WallpaperPreviewLayout Calculate(double width,double height,double sourceAspect,double screenAspect,bool contain,WallpaperPosition position)
    {
        position.Validate();
        if(!double.IsFinite(width)||!double.IsFinite(height)||width<=0||height<=0||!double.IsFinite(sourceAspect)||sourceAspect<=0||!double.IsFinite(screenAspect)||screenAspect<=0)return default;
        double screenWidth=Math.Min(width*.9,height*.9*screenAspect),screenHeight=screenWidth/screenAspect;
        var screen=new PreviewRect((width-screenWidth)/2,(height-screenHeight)/2,screenWidth,screenHeight);
        double scale=contain?Math.Min(screenWidth/sourceAspect,screenHeight):Math.Max(screenWidth/sourceAspect,screenHeight);
        double imageWidth=sourceAspect*scale*position.Scale/100,imageHeight=scale*position.Scale/100;
        return new(screen,new(screen.X+(screenWidth-imageWidth)/2+Math.Abs(screenWidth-imageWidth)/2*position.X/100,
            screen.Y+(screenHeight-imageHeight)/2+Math.Abs(screenHeight-imageHeight)/2*position.Y/100,imageWidth,imageHeight));
    }
}
