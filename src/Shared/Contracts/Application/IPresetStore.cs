using LumaWall.Contracts.Presets;

namespace LumaWall.Contracts.Application;

/// <summary>Database access stays on the caller's storage thread; package file I/O is independent.</summary>
public interface IPresetStore
{
    string Root { get; }
    PresetDocument CapturePreset(string[] screens, bool includeLibrary);
    void ApplyPreset(PresetDocument document, string? expectedRevision = null);
}
