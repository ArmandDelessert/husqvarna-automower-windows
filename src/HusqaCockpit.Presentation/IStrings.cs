using System.Globalization;

namespace HusqaCockpit.Presentation;

/// <summary>
/// The localized texts, as the view models see them. The app reads them from its .resw resources
/// (Strings\{language}\Resources.resw and ErrorCodes.resw); the tests provide their own.
/// </summary>
public interface IStrings
{
    /// <summary>The text of <paramref name="key"/>, or the key itself when there is none.</summary>
    string Text(string key);

    /// <summary>Human-readable description of an Automower error code.</summary>
    string ErrorCode(int code);

    /// <summary>The text of <paramref name="key"/> with its placeholders filled in the current culture.</summary>
    string Format(string key, params object?[] args) => string.Format(CultureInfo.CurrentCulture, Text(key), args);

    /// <summary>Localized name of an enum value, from the key "{EnumType}_{Value}".</summary>
    string EnumText<TEnum>(TEnum value) where TEnum : struct, Enum => Text($"{typeof(TEnum).Name}_{value}");
}
