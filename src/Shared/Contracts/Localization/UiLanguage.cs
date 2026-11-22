using System.Globalization;
using System.Text.Json;

namespace LumaWall.Contracts;

public static class UiLanguage
{
    public const string SettingKey = "ui.language";
    public const string SystemDefault = "system";
    public const string Chinese = "zh-CN";
    public const string English = "en-US";

    private static readonly Lazy<Dictionary<string, string>> EnglishStrings = new(() =>
    {
        using var stream = typeof(UiLanguage).Assembly.GetManifestResourceStream("LumaWall.Ui.en.json")
            ?? throw new InvalidOperationException("Missing English UI resources.");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    });

    public static CultureInfo SystemLanguage { get; private set; } = CultureInfo.CurrentUICulture;
    public static string Current { get; private set; } = Chinese;

    public static string Preference(string? saved) => saved is Chinese or English ? saved : SystemDefault;

    public static string Resolve(string? saved, CultureInfo windowsLanguage) => Preference(saved) switch
    {
        Chinese => Chinese,
        English => English,
        _ => windowsLanguage.TwoLetterISOLanguageName == "zh" ? Chinese : English
    };

    public static void Initialize(string? saved, CultureInfo windowsLanguage)
    {
        SystemLanguage = windowsLanguage;
        Current = Resolve(saved, windowsLanguage);
        var culture = CultureInfo.GetCultureInfo(Current);
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    // Only call with app-authored strings. Names, paths and values supplied by
    // the user remain arguments to a localized format, never lookup keys.
    public static string Text(string source) => Current == English && EnglishStrings.Value.TryGetValue(source, out var value)
        ? value : source;

    public static string Format(FormattableString message) =>
        string.Format(CultureInfo.CurrentCulture, Text(message.Format), message.GetArguments());
}
