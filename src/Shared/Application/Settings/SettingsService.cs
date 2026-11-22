using LumaWall.Contracts;
using LumaWall.Contracts.Application;

namespace LumaWall.Application;

public sealed class SettingsService(ISettingsStore store)
{
    public string? Get(string key) => store.GetSetting(key);
    public IReadOnlyDictionary<string, string> GetByPrefix(string prefix) => store.GetSettingsByPrefix(prefix);
    public void Set(string key, string value) => store.SetSetting(key, value);
    public static string PauseKey(PauseReason reason) => "pause." + reason;
    public PauseReason PausePolicy()
    {
        var policy = PauseReason.None;
        foreach (var reason in new[] { PauseReason.SessionLocked, PauseReason.DisplayOff, PauseReason.Suspend })
            if (Get(PauseKey(reason)) != "false") policy |= reason;
        if (Get(PauseKey(PauseReason.HighResourceLoad)) == "true") policy |= PauseReason.HighResourceLoad;
        return policy;
    }
    public int FpsLimit() => int.TryParse(Get("fpsLimit"), out int cap) && cap is >= 1 and <= 240 ? cap : 0;
    public DecoderMode Decoder() => Enum.TryParse<DecoderMode>(Get("decoderMode"), out var mode) && Enum.IsDefined(mode) ? mode : DecoderMode.Auto;
}
