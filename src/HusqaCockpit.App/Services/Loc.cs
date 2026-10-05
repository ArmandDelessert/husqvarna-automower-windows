using HusqaCockpit.Presentation;

namespace HusqaCockpit.App.Services;

/// <summary>Access to the localized strings from the WinUI code; the view models receive <see cref="Strings"/> instead.</summary>
public static class Loc
{
    public static IStrings Strings { get; } = new ResourceStrings();

    public static string Get(string key) => Strings.Text(key);

    public static string Format(string key, params object?[] args) => Strings.Format(key, args);

    /// <summary>Human-readable description of an Automower error code.</summary>
    public static string ErrorCode(int code) => Strings.ErrorCode(code);

    /// <summary>Localized name of an enum value, from the key "{EnumType}_{Value}".</summary>
    public static string Enum<TEnum>(TEnum value) where TEnum : struct, System.Enum => Strings.EnumText(value);
}
