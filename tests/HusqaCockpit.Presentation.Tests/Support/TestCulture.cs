using System.Globalization;

namespace HusqaCockpit.Presentation.Tests.Support;

/// <summary>
/// English and French with fixed time and number formats: the culture data of ICU changes between versions
/// (e.g. the space before AM/PM, or the French thousands separator), the expected texts should not.
/// </summary>
internal static class TestCulture
{
    public static CultureInfo English { get; } = Create("en-US", groupSeparator: ",");

    public static CultureInfo French { get; } = Create("fr-FR", groupSeparator: " ");

    /// <summary>Makes <paramref name="culture"/> the current culture until the result is disposed.</summary>
    public static IDisposable Use(CultureInfo culture) => new Scope(culture);

    private static CultureInfo Create(string name, string groupSeparator)
    {
        var culture = (CultureInfo)CultureInfo.GetCultureInfo(name).Clone();
        culture.DateTimeFormat.ShortTimePattern = "HH:mm";
        culture.NumberFormat.NumberGroupSeparator = groupSeparator;
        return CultureInfo.ReadOnly(culture);
    }

    private sealed class Scope : IDisposable
    {
        private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
        private readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;

        public Scope(CultureInfo culture)
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = _culture;
            CultureInfo.CurrentUICulture = _uiCulture;
        }
    }
}
