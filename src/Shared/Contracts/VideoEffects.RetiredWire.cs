namespace LumaWall.Contracts;
public sealed partial record VideoEffects {
    [System.Text.Json.Serialization.JsonPropertyName("ScanCards")]
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public System.Text.Json.JsonElement? RetiredWarehouse {get=>null;init {}}
    // Fixed compatibility padding at the original position of the retired effect.
    private static string RetiredScanCardsWire()=>" 0 6 10 0.080000 0 0.500000 0.500000 1.000000 1.000000 1.000000 197379 1.000000 3158064 10810367 2.000000 0.450000 4.000000 6.000000 0.350000 0.400000 1 0 12 4.000000 1.000000 8.000000 0 1 0 1 - 1 1 2 1.000000 1.000000 2.000000 2.400000 2.500000 -1 -1 -1 -1 1 99 -1 -1 1.000000 1.000000 0 1 99 0 0 1 99 0 1.000000";
}
