using System.Text.Json;
using LumaWall.Contracts;
using LumaWall.Contracts.Application;

namespace LumaWall.Application;

public sealed record LoadedWidgetConfiguration(WidgetConfiguration State, string? LegacySnapshot);

/// <summary>Owns configuration validation and legacy backup; the Windows adapter owns host windows.</summary>
public sealed class WidgetConfigurationService(ISettingsStore store)
{
    public LoadedWidgetConfiguration Read()
    {
        string? saved=store.GetSetting("widgets.v1");
        var state=WidgetConfiguration.FromJson(saved);
        string? legacy=null;
        if(!string.IsNullOrWhiteSpace(saved))
        {
            using var json=JsonDocument.Parse(saved);
            if(json.RootElement.TryGetProperty("Version",out var version)&&version.GetInt32()<18)legacy=saved;
        }
        return new(state,legacy);
    }

    public void Save(WidgetConfiguration state,string? legacySnapshot=null)
    {
        string json=state.ToJson();
        if(legacySnapshot!=null)store.SetSetting("widgets.before-0.1.10.2",legacySnapshot);
        store.SetSetting("widgets.v1",json);
    }
}
