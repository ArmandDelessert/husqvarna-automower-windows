using System.Globalization;
using System.Text.RegularExpressions;
using HusqaCockpit.Presentation.Tests.Support;

namespace HusqaCockpit.Presentation.Tests;

/// <summary>
/// The texts of every language agree with the English ones where a mistake would show on screen: the same placeholders, and
/// nothing that the English does not have. A text a language lacks is not a failure: Windows then shows the English one.
/// </summary>
public static partial class TranslationsTests
{
    public static TheoryData<string> Languages => [.. ReswStrings.Languages.Where(l => l != "en-US")];

    [GeneratedRegex(@"\{(\d+)(?::[^}]*)?\}")]
    private static partial Regex Placeholder();

    private static List<string> PlaceholdersOf(string text) => [.. Placeholder().Matches(text).Select(m => m.Value).Order(StringComparer.Ordinal)];

    [Theory]
    [MemberData(nameof(Languages))]
    public static void A_language_keeps_the_placeholders_of_the_english_texts(string language)
    {
        foreach (var file in new[] { "Resources", "ErrorCodes" })
        {
            var english = ReswStrings.Resources("en-US", file);
            foreach (var (key, text) in ReswStrings.Resources(language, file))
            {
                Assert.True(english.TryGetValue(key, out var source), $"{language} {file}: '{key}' does not exist in English");
                Assert.False(string.IsNullOrWhiteSpace(text), $"{language} {file}: '{key}' is empty");
                Assert.True(PlaceholdersOf(source!).SequenceEqual(PlaceholdersOf(text)), $"{language} {file}: '{key}' does not keep the placeholders of '{source}'");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public static void A_language_writes_the_date_of_a_mower_event_in_its_own_way(string language)
    {
        var culture = CultureInfo.GetCultureInfo(language);
        var pattern = ReswStrings.Resources(language)["When_DatePattern"];

        var text = new DateTime(2026, 10, 7).ToString(pattern, culture);

        Assert.Contains("7", text, StringComparison.Ordinal);
        Assert.NotEqual(pattern, text);
    }
}
