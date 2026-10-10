using System.Text.Json;
using HusqaCockpit.Presentation;

namespace HusqaCockpit.App.Services;

/// <summary>Reads and writes the settings as JSON in %LocalAppData%\HusqA Cockpit\settings.json.</summary>
public sealed class AppSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions s_options = new() { WriteIndented = true };
    private static readonly Lock s_lock = new();

    public static AppSettingsStore Default { get; } = new();

    private static string FilePath => Path.Combine(AppPaths.DataFolder, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), s_options) ?? new AppSettings();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Corrupted or unreadable settings: start from defaults.
        }
        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        lock (s_lock)
        {
            Directory.CreateDirectory(AppPaths.DataFolder);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, s_options));
            File.Move(temp, FilePath, overwrite: true);
        }
    }
}

public static class AppPaths
{
    public static string DataFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HusqA Cockpit");

    public static string LogsFolder { get; } = Path.Combine(DataFolder, "Logs");

    public static string InstallFolder { get; } = AppContext.BaseDirectory;

    public static string IconPath { get; } = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
    public static string AlertIconPath { get; } = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIconAlert.ico");
}
