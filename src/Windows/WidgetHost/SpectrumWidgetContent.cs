using LumaWall.Contracts;
using Shortcut=LumaWall.Contracts.Shortcut;
using W=System.Windows;
using C=System.Windows.Controls;
using I=System.Windows.Input;
using M=System.Windows.Media;

namespace LumaWall.WidgetHost;

internal sealed class SpectrumWidgetContent : WidgetContent
{
    private readonly SpectrumView spectrum;
    internal SpectrumWidgetContent(WidgetContentContext context):base(context)
    {
            spectrum=new SpectrumView {Margin=new W.Thickness(14,12,14,12)};spectrum.Configure(instance.Spectrum);
            var visual=instance.Spectrum.Background?WidgetVisuals.Frame(spectrum,out focusEdge):new C.Border {Background=M.Brushes.Transparent,Child=spectrum};
            if(!instance.Spectrum.Background)focusEdge=null;
            var button=new C.Button {Content=visual,Style=WidgetVisuals.PlainButton(),HorizontalContentAlignment=W.HorizontalAlignment.Stretch,VerticalContentAlignment=W.VerticalAlignment.Stretch};
            DragHandle(button,()=>Send(new() {Type="edit-widget",Id=instance.Id}),opensSettings:true);Element=button;
            W.Automation.AutomationProperties.SetName(button,UiLanguage.Text("系统声音音频频谱"));
            WidgetVisuals.Action(menu,UiLanguage.Text("频谱设置…"),"settings",()=>Send(new() {Type="edit-widget",Id=instance.Id}),()=>!editingLocked);
    }
    internal override void UpdateSpectrum(SpectrumSample sample) {
        spectrum.Margin=new W.Thickness(instance.Spectrum.Padding);spectrum.Configure(instance.Spectrum);spectrum.Update(sample);
    }
}
