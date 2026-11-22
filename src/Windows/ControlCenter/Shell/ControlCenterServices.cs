using LumaWall.Application;
using LumaWall.Storage;
using LumaWall.Application.Presets;

namespace LumaWall.ControlCenter;

/// <summary>Composition root. Every feature and automation adapter shares these service instances.</summary>
public sealed class ControlCenterServices
{
    public LibraryService Library { get; }
    public PlaybackSettingsService Playback { get; }
    public SettingsService Settings { get; }
    public WidgetConfigurationService Widgets { get; }
    public EffectsService Effects { get; }
    public HistoryService History { get; }
    public ConfigurationImportService Sharing { get; }
    public PresetService Presets { get; }

    public ControlCenterServices(LibraryStore store)
    {
        Library = new(store);
        Playback = new(store, store);
        Settings = new(store);
        Widgets = new(store);
        Effects = new(store);
        History = new(store);
        Sharing = new(store);
        Presets = new(store, store.Root);
    }
}
