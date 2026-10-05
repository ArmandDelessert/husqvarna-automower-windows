using HusqaCockpit.Presentation;
using Microsoft.Windows.ApplicationModel.Resources;

namespace HusqaCockpit.App.Services;

/// <summary>The localized strings of the app: Strings\{language}\Resources.resw and ErrorCodes.resw.</summary>
public sealed class ResourceStrings : IStrings
{
    private readonly ResourceLoader _strings = new();
    private readonly ResourceLoader _errorCodes = new(ResourceLoader.GetDefaultResourceFilePath(), "ErrorCodes");

    public string Text(string key)
    {
        var value = _strings.GetString(key);
        return string.IsNullOrEmpty(value) ? key : value;
    }

    public string ErrorCode(int code)
    {
        var value = _errorCodes.GetString($"E{code}");
        return string.IsNullOrEmpty(value) ? ((IStrings)this).Format("ErrorCode_Unknown", code) : value;
    }
}
