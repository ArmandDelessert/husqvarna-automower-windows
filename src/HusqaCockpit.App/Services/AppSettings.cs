using System.Text.Json;

namespace HusqaCockpit.App.Services;

/// <summary>User preferences, stored as JSON in %LOCALAPPDATA%\HusqA Cockpit\settings.json.</summary>
public sealed class AppSettings
{
    /// <summary>"" follows Windows; otherwise a language tag such as "fr-FR" or "en-US".</summary>
    public string Language { get; set; } = "";

    public bool NotifyErrors { get; set; } = true;
    public bool NotifyRecoveries { get; set; } = true;
    public bool NotifyStopped { get; set; } = true;
    public bool NotifyConnectivity { get; set; } = true;

    /// <summary>Closing the window keeps the app running in the notification area.</summary>
    public bool CloseToTray { get; set; } = true;

    /// <summary>Set once the user has been told the app keeps running after the window is closed.</summary>
    public bool TrayHintShown { get; set; }

    /// <summary>REST refresh interval used when real-time events are unavailable.</summary>
    public int PollingIntervalMinutes { get; set; } = 10;

    /// <summary>Month ("yyyy-MM") the request counter refers to.</summary>
    public string QuotaMonth { get; set; } = "";

    /// <summary>API requests sent from this computer during <see cref="QuotaMonth"/>.</summary>
    public int QuotaRequests { get; set; }

    public WindowBounds? Window { get; set; }
}

public sealed record WindowBounds(int X, int Y, int Width, int Height, bool Maximized);

public static class AppSettingsStore
{
    private static readonly JsonSerializerOptions s_options = new() { WriteIndented = true };
    private static readonly Lock s_lock = new();

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

    public static void Save(AppSettings settings)
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
    public static string IconPngPath { get; } = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.png");
}
