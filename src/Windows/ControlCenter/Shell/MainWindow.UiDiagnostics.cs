using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace LumaWall.ControlCenter;

public sealed partial class MainWindow
{
    internal string DescribeUiFaultContext()
    {
        // Fault reporting must neither move focus nor read user-entered values.
        string context=$"operationId={OperationTrace.Current?.Id??lastUiOperation?.Id??"none"}; page={navigationPage}; effect={selectedEffectKind?.ToString()??"none"}; loadingEffects={loadingEffects}; buildingAudioRules={buildingAudioRules}";
        try {
            if(Root.XamlRoot is { } root&&FocusManager.GetFocusedElement(root) is FrameworkElement focused)
                context+=$"; focusType={focused.GetType().Name}; focusId={focused.Name}; focusLoaded={focused.IsLoaded}";
        } catch(Exception error) {context+=$"; focusReadError=0x{error.HResult:X8}";}
        return context;
    }
}
