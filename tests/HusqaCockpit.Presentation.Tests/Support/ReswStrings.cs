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

    public string Text(string key) => _strings.GetValueOrDefault(key, key);

    public string ErrorCode(int code) =>
        _errorCodes.TryGetValue($"E{code}", out var text) ? text : ((IStrings)this).Format("ErrorCode_Unknown", code);

    private static Dictionary<string, string> Load(string path) =>
        XDocument.Load(path).Root!.Elements("data").ToDictionary(d => (string)d.Attribute("name")!, d => (string)d.Element("value")!);
}
