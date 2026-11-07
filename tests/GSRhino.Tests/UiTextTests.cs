using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using GSRhino.Core;

namespace GSRhino.Tests;

[Collection("USD runtime process isolation")]
public sealed class UiTextTests
{
    [Theory]
    [InlineData(1033, "zh-CN", "en")]
    [InlineData(1031, "zh-TW", "en")]
    [InlineData(2052, "en-US", "zh")]
    [InlineData(1028, "en-US", "zh")]
    [InlineData(3076, "en-US", "zh")]
    [InlineData(null, "zh-Hant", "zh")]
    [InlineData(0, "zh-Hans", "zh")]
    [InlineData(127, "zh-CN", "zh")]
    [InlineData(-1, "zh-HK", "zh")]
    [InlineData(null, "fr-FR", "en")]
    [InlineData(null, null, "en")]
    public void HostLanguageTakesPrecedenceWithEnglishFallback(int? host, string? system, string expected)
        => Assert.Equal(expected, UiText.ResolveLanguage(host, system));

    [Fact]
    public async Task BackgroundMessagesUseHostChoiceWithoutChangingCultureOrUserData()
    {
        CultureInfo culture = CultureInfo.CurrentCulture, uiCulture = CultureInfo.CurrentUICulture;
        try
        {
            UiText.Initialize(1033, "zh-CN");
            Assert.Equal("Choose file…", UiText.Get("Choose file…"));
            UiText.Initialize(2052, "en-US");
            Assert.Equal("选择文件…", await Task.Run(() => UiText.Get("Choose file…")));
            const string userData = "My 中文 model {0} / Ready";
            Assert.Equal(userData, UiText.Get(userData));
            Assert.Equal("Name: " + userData, UiText.Format("Name: {0}", userData));
            Assert.Same(culture, CultureInfo.CurrentCulture); Assert.Same(uiCulture, CultureInfo.CurrentUICulture);
        }
        finally { UiText.Initialize(1033, "en-US"); }
    }

    [Fact]
    public void EveryTranslationKeepsFormatArgumentsAndEnglishKeys()
    {
        using var stream = typeof(UiText).Assembly.GetManifestResourceStream("GSRhino.Core.UiText.zh.json")!;
        var pairs = JsonSerializer.Deserialize<Dictionary<string,string>>(stream)!;
        Assert.True(pairs.Count >= 200);
        foreach (var (english, chinese) in pairs)
        {
            Assert.DoesNotMatch("[\\u4e00-\\u9fff]", english);
            Assert.False(string.IsNullOrWhiteSpace(chinese));
            string[] Args(string text) => Regex.Matches(text, @"\{\d+(?:,[^{}:]+)?(?::[^{}]+)?\}").Select(m => m.Value).OrderBy(x => x).ToArray();
            Assert.Equal(Args(english), Args(chinese));
            Assert.Equal(english, UiText.Translate(english, "en"));
            Assert.Equal(chinese, UiText.Translate(english, "zh"));
        }
    }
}
