using System.Globalization;
using System.Text.Json;

namespace GSRhino.Core;

/// <summary>English source text with optional Chinese UI resources. Never changes process culture or user data.</summary>
public static class UiText
{
    private static int _chinese;
    private static readonly Lazy<IReadOnlyDictionary<string, string>> Chinese = new(() =>
    {
        using var resource = typeof(UiText).Assembly.GetManifestResourceStream("GSRhino.Core.UiText.zh.json")
            ?? throw new InvalidOperationException("Missing embedded Chinese UI resource.");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(resource)!;
    });

    // The host initializes this once on its UI thread, before creating panels or background work.
    // Workers read the same immutable language choice rather than their own thread's UI culture.
    public static void Initialize(int? rhinoLanguageId, string? systemUiLanguage)
        => Volatile.Write(ref _chinese, ResolveLanguage(rhinoLanguageId, systemUiLanguage) == "zh" ? 1 : 0);

    public static string ResolveLanguage(int? rhinoLanguageId, string? systemUiLanguage)
    {
        if (rhinoLanguageId is > 0 and not 127)
        {
            try { return IsChinese(CultureInfo.GetCultureInfo(rhinoLanguageId.Value).Name) ? "zh" : "en"; }
            catch (CultureNotFoundException) { }
        }
        return IsChinese(systemUiLanguage) ? "zh" : "en";
    }

    private static bool IsChinese(string? language) => !string.IsNullOrWhiteSpace(language) &&
        (language.Equals("zh", StringComparison.OrdinalIgnoreCase) || language.StartsWith("zh-", StringComparison.OrdinalIgnoreCase));

    public static string Get(string english) => Translate(english, Volatile.Read(ref _chinese) == 1 ? "zh" : "en");
    public static string Translate(string english, string language) =>
        language == "zh" && Chinese.Value.TryGetValue(english, out string? translated) ? translated : english;
    public static string Format(string english, params object?[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, Get(english), arguments);
}
