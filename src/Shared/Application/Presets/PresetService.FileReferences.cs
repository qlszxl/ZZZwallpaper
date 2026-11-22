using LumaWall.Contracts.Presets;

namespace LumaWall.Application.Presets;

public sealed record PresetFileReferences(PresetSummary[] Presets, int UnreadablePresets);

public sealed partial class PresetService
{
    /// <summary>Reads local metadata only; never hashes or opens wallpaper media.</summary>
    public PresetFileReferences FindFileReferences(string path)
    {
        string full = LibrarySourceFiles.Normalize(path);
        var found = new List<PresetSummary>(); int unreadable = 0;
        lock (files.Gate)
        {
            foreach (var summary in List())
            {
                try
                {
                    string directory = PresetDirectory(summary.Id); CheckDirectory(directory);
                    var manifest = PresetPackage.ParseManifest(ReadLocalFile(Path.Combine(directory, "manifest.json"), PresetPackageLimits.MaxManifestBytes));
                    var locations = ReadLocations(directory, manifest);
                    if (manifest.Resources.Where(r => r.Kind == "media").Any(r => LibrarySourceFiles.SamePath(
                        locations?.GetValueOrDefault(r.Path) ?? Path.Combine(resources, Path.GetFileName(r.Path)), full)))
                        found.Add(summary);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException)
                { unreadable++; }
            }
        }
        return new(found.ToArray(), unreadable);
    }
}
