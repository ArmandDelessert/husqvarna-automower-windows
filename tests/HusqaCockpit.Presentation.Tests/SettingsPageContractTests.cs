using System.Xml.Linq;
using HusqaCockpit.Presentation.Tests.Support;

namespace HusqaCockpit.Presentation.Tests;

/// <summary>
/// The Settings page has the same structure and the same texts as the one of DySS Cockpit, the twin application.
/// These tests fix what both agree on, so that the two pages do not drift apart unnoticed:
/// the order and the titles of the sections, and the texts they share.
/// </summary>
public class SettingsPageContractTests
{
    private static readonly XNamespace s_xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly XNamespace s_presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    /// <summary>The x:Uid of the section headers of the page, in the order they appear.</summary>
    private static List<string> SectionHeaderUids()
    {
        var page = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Views", "SettingsPage.xaml"));
        return page.Descendants(s_presentation + "TextBlock")
            .Where(t => (string?)t.Attribute("Style") == "{StaticResource SectionHeaderStyle}")
            .Select(t => (string)t.Attribute(s_xaml + "Uid")!)
            .ToList();
    }

    [Fact]
    public void The_sections_come_in_the_agreed_order()
    {
        // The API key comes first because only this application has one; the map section is DySS's.
        Assert.Equal(
            ["Settings_ApiHeader", "Settings_NotificationsHeader", "Settings_BehaviorHeader", "Settings_LanguageHeader", "Settings_AboutHeader"],
            SectionHeaderUids());
    }

    [Theory]
    [InlineData("en-US", new[] { "Husqvarna API key", "Notifications", "Behavior", "Language", "About" })]
    [InlineData("fr-FR", new[] { "Clé d'API Husqvarna", "Notifications", "Comportement", "Langue", "À propos" })]
    public void The_sections_have_the_agreed_titles(string language, string[] titles)
    {
        var strings = language == "fr-FR" ? ReswStrings.French : ReswStrings.English;

        Assert.Equal(titles, SectionHeaderUids().Select(uid => strings.Text($"{uid}.Text")));
    }

    [Theory]
    [InlineData("TaskNotificationMode_None", "None", "Aucune")]
    [InlineData("TaskNotificationMode_EndOnly", "Mowing end only", "Fin de tonte seulement")]
    [InlineData("TaskNotificationMode_StartAndEnd", "Mowing start and end", "Début et fin de tonte")]
    [InlineData("Settings_TestNotification.Content", "Send a test notification", "Envoyer une notification de test")]
    [InlineData("Settings_LanguageSystem.Content", "Automatic (Windows language)", "Automatique (langue de Windows)")]
    [InlineData("Settings_RestartRequired.Title", "Restart required", "Redémarrage nécessaire")]
    [InlineData("Settings_RestartNow.Content", "Restart now", "Redémarrer maintenant")]
    [InlineData("Settings_OpenLogs.Content", "Open the logs folder", "Ouvrir le dossier des journaux")]
    public void The_texts_shared_with_the_twin_application_are_the_agreed_ones(string key, string english, string french)
    {
        Assert.Equal(english, ReswStrings.English.Text(key));
        Assert.Equal(french, ReswStrings.French.Text(key));
    }

    [Fact]
    public void The_about_section_has_the_version_the_disclaimer_the_repository_and_the_logs()
    {
        var page = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Views", "SettingsPage.xaml"));
        var uids = page.Descendants().Select(e => (string?)e.Attribute(s_xaml + "Uid")).Where(u => u is not null).ToList();

        Assert.Contains("Settings_Disclaimer", uids);
        Assert.Contains("Settings_SourceCode", uids);
        Assert.Contains("Settings_OpenLogs", uids);
        Assert.Contains(page.Descendants(s_presentation + "HyperlinkButton"),
            link => ((string?)link.Attribute("NavigateUri"))?.StartsWith("https://github.com/", StringComparison.Ordinal) == true);
    }
}
