using LumaWall.Contracts;
using Shortcut=LumaWall.Contracts.Shortcut;
using W=System.Windows;
using C=System.Windows.Controls;
using I=System.Windows.Input;
using M=System.Windows.Media;

namespace LumaWall.WidgetHost;

// The built-in registry is deliberately static: adding a visual does not require
// changes to window placement, input transport or the host synchronization loop.
internal static class WidgetContentFactory
{
    internal static WidgetContent Create(WidgetContentContext context,SearchFieldState search)=>context.Instance().Kind switch {
        WidgetKind.Search=>new SearchWidgetContent(context,search),
        WidgetKind.Clock or WidgetKind.Countdown or WidgetKind.FocusTimer=>new TimeWidgetContent(context),
        WidgetKind.Text or WidgetKind.Sticker or WidgetKind.Mask=>new VisualWidgetContent(context),
        WidgetKind.Spectrum=>new SpectrumWidgetContent(context),
        WidgetKind.Performance=>new PerformanceWidgetContent(context),
        _=>new ShortcutWidgetContent(context)
    };
}
