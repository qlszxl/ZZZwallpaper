using System.Text.Json;
using LumaWall.Contracts;

namespace LumaWall.Automation;

public static class EffectReadModel
{
    public static IReadOnlyDictionary<string,JsonElement> Parameters(VideoEffects effects)
    {
        var values=JsonSerializer.SerializeToElement(effects);
        return EffectPatch.Groups.Values.SelectMany(fields=>fields).Distinct()
            .ToDictionary(name=>name,name=>values.GetProperty(name));
    }
}