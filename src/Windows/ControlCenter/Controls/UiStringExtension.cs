using LumaWall.Contracts;
using Microsoft.UI.Xaml.Markup;

namespace LumaWall.ControlCenter;

[MarkupExtensionReturnType(ReturnType = typeof(string))]
public sealed class UiStringExtension : MarkupExtension
{
    public string Source { get; set; } = "";
    protected override object ProvideValue() => UiLanguage.Text(Source);
}
