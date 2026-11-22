using LumaWall.Contracts;
using LumaWall.WidgetHost;
using System.Windows;
using System.Windows.Controls;
namespace FeatureFixture;
public sealed class Meter:IPackageWidgetV1 {
    public FrameworkElement Create(WidgetInstance settings)=>new TextBlock {Text="Meter "+settings.Id};
    public void Dispose(){}
}
