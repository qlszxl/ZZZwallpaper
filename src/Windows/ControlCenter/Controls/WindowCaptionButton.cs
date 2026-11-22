using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LumaWall.ControlCenter;

// Pass only a state name to the template. Every template instance owns its
// drawing data; no Geometry is attached to both the button and a Path.
public sealed class WindowCaptionButton : Button
{
    public static readonly DependencyProperty GlyphKindProperty = DependencyProperty.Register(
        nameof(GlyphKind), typeof(string), typeof(WindowCaptionButton),
        new PropertyMetadata("Minimize", (sender, _) => ((WindowCaptionButton)sender).UpdateGlyphState()));

    public string GlyphKind
    {
        get => (string)GetValue(GlyphKindProperty);
        set => SetValue(GlyphKindProperty, value);
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        UpdateGlyphState();
    }

    private void UpdateGlyphState() => VisualStateManager.GoToState(this, GlyphKind, false);
}
