using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HusqaCockpit.App.Views;

/// <summary>Functions used from x:Bind expressions.</summary>
public static class BindingHelpers
{
    public static Visibility VisibleWhenFalse(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    public static Visibility VisibleWhenNotEmpty(string? value) => string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;

    public static InfoBarSeverity FeedbackSeverity(bool failed) => failed ? InfoBarSeverity.Error : InfoBarSeverity.Success;

    public static bool Not(bool value) => !value;
}
