namespace LumaWall.Contracts.Application;

public interface ISettingsStore
{
    string? GetSetting(string key);
    IReadOnlyDictionary<string, string> GetSettingsByPrefix(string prefix);
    void SetSetting(string key, string value);
}
