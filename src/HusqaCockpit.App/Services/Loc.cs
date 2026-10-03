using System.Globalization;
using Microsoft.Windows.ApplicationModel.Resources;

namespace HusqaCockpit.App.Services;

/// <summary>Access to the localized strings (Strings\{language}\Resources.resw and ErrorCodes.resw).</summary>
public static class Loc
{
    private static readonly ResourceLoader s_strings = new();
    private static readonly ResourceLoader s_errorCodes = new(ResourceLoader.GetDefaultResourceFilePath(), "ErrorCodes");

    public static string Get(string key)
    {
        var value = s_strings.GetString(key);
        return string.IsNullOrEmpty(value) ? key : value;
    }

    public static string Format(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);

    /// <summary>Human-readable description of an Automower error code.</summary>
    public static string ErrorCode(int code)
    {
        var value = s_errorCodes.GetString($"E{code}");
        return string.IsNullOrEmpty(value) ? Format("ErrorCode_Unknown", code) : value;
    }

    /// <summary>Localized name of an enum value, from the key "{EnumType}_{Value}".</summary>
    public static string Enum<TEnum>(TEnum value) where TEnum : struct, System.Enum => Get($"{typeof(TEnum).Name}_{value}");
}
