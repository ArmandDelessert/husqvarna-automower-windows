using System.Xml.Linq;

namespace HusqaCockpit.Presentation.Tests.Support;

/// <summary>The app's real texts, read from the .resw files copied next to the tests.</summary>
internal sealed class ReswStrings : IStrings
{
    private readonly Dictionary<string, string> _strings;
    private readonly Dictionary<string, string> _errorCodes;

    private ReswStrings(string language)
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Strings", language);
        _strings = Load(Path.Combine(folder, "Resources.resw"));
        _errorCodes = Load(Path.Combine(folder, "ErrorCodes.resw"));
    }

    public static IStrings English { get; } = new ReswStrings("en-US");

    public static IStrings French { get; } = new ReswStrings("fr-FR");

    /// <summary>The texts of any language of the app, by its tag ("de-DE").</summary>
    public static IStrings For(string language) => new ReswStrings(language);

    /// <summary>The folders of Strings: one per language of the app.</summary>
    public static IReadOnlyList<string> Languages { get; } =
        [.. Directory.GetDirectories(Path.Combine(AppContext.BaseDirectory, "Strings")).Select(d => Path.GetFileName(d)!).Order(StringComparer.Ordinal)];

    /// <summary>The texts of a language as the .resw file has them, to compare one language with another.</summary>
    public static IReadOnlyDictionary<string, string> Resources(string language, string file = "Resources") =>
        Load(Path.Combine(AppContext.BaseDirectory, "Strings", language, file + ".resw"));

    public string Text(string key) => _strings.GetValueOrDefault(key, key);

    public string ErrorCode(int code) =>
        _errorCodes.TryGetValue($"E{code}", out var text) ? text : ((IStrings)this).Format("ErrorCode_Unknown", code);

    private static Dictionary<string, string> Load(string path) =>
        XDocument.Load(path).Root!.Elements("data").ToDictionary(d => (string)d.Attribute("name")!, d => (string)d.Element("value")!);
}
