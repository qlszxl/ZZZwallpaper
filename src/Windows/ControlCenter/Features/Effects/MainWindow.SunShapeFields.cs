using Microsoft.UI.Xaml;
namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    private readonly List<(FrameworkElement Element,bool Cone)> sunShapeFields=new();
    private FrameworkElement SunShapeField(bool cone,FrameworkElement element)
    {
        sunShapeFields.Add((element,cone));return element;
    }
    private void RefreshSunShapeFields()
    {
        bool cone=effectSession.ExtendedDraft.SunPrism.Shape==5;
        foreach(var field in sunShapeFields)field.Element.Visibility=field.Cone==cone?Visibility.Visible:Visibility.Collapsed;
    }
}
