namespace LumaWall.ControlCenter;
public sealed partial class MainWindow
{
    private void RefreshEffectParameterAccess(EffectDefinition definition,bool refresh=true)
    {
        bool enabled=EffectIsEnabled(definition);
        if(!enabled&&definition.Kind==LumaWall.Contracts.EffectKind.LightFlare)StopLightFlareLocator();
        ParameterInputGate.Apply(definition.Panel,enabled,refresh);
        ParameterInputGate.Apply(effectStrengthRow,enabled,refresh);
        ParameterInputGate.Apply(effectMaskRow,enabled,refresh);
        ParameterInputGate.Apply(effectSoloRow,enabled,refresh);
    }
}
