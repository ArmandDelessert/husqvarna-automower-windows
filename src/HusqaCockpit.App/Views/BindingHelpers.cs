using HusqaCockpit.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace HusqaCockpit.App.Views;

/// <summary>Functions used from x:Bind expressions.</summary>
public static class BindingHelpers
{
    public static Visibility VisibleWhenFalse(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    public static Visibility VisibleWhenNotEmpty(string? value) => string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;

    public static InfoBarSeverity FeedbackSeverity(bool failed) => failed ? InfoBarSeverity.Error : InfoBarSeverity.Success;

    /// <summary>The detail line of a status, except for a mower out of reach: it only repeats when it was last heard of.</summary>
    public static string DetailWhenConnected(bool connected, string detail) => connected ? detail : "";

    public static Visibility VisibleWhenConnectedAndNotEmpty(bool connected, string detail) => VisibleWhenNotEmpty(DetailWhenConnected(connected, detail));

    public static bool Not(bool value) => !value;

    /// <summary>The InfoBar look of a severity chosen by a view model.</summary>
    public static InfoBarSeverity InfoBarSeverityFor(StatusSeverity severity) => severity switch
    {
        StatusSeverity.Ok => InfoBarSeverity.Success,
        StatusSeverity.Warning => InfoBarSeverity.Warning,
        StatusSeverity.Error => InfoBarSeverity.Error,
        _ => InfoBarSeverity.Informational,
    };

    /// <summary>The color of the status dot of a mower.</summary>
    public static Brush SeverityBrush(StatusSeverity severity)
    {
        var key = severity switch
        {
            StatusSeverity.Ok => "SystemFillColorSuccessBrush",
            StatusSeverity.Info => "AccentFillColorDefaultBrush",
            StatusSeverity.Warning => "SystemFillColorCautionBrush",
            StatusSeverity.Error => "SystemFillColorCriticalBrush",
            _ => "SystemFillColorNeutralBrush",
        };
        return (Brush)Application.Current.Resources[key];
    }
}
