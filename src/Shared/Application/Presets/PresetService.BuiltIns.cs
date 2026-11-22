using LumaWall.Diagnostics;

namespace LumaWall.Application.Presets;

public sealed partial class PresetService
{
    // User saves and ZIP imports get fresh IDs, even when they share a sample's name.
    private static readonly HashSet<string> RetiredBuiltInIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "b8100000000000000000000000000001", "b8100000000000000000000000000002",
        "b8100000000000000000000000000003", "b8100000000000000000000000000004"
    };

    private void RemoveRetiredBuiltIns()
    {
        foreach (string id in RetiredBuiltInIds)
        {
            try
            {
                string directory = PresetDirectory(id);
                if (Directory.Exists(directory))
                {
                    CheckPresetFiles(directory);
                    Directory.Delete(directory, true);
                }
                string marker = Path.Combine(root, "builtin-preset-" + id + ".installed");
                if (File.Exists(marker)) File.Delete(marker);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                AppLog.Warning("Presets", "Could not remove a retired built-in preset", error);
            }
        }
    }
}
