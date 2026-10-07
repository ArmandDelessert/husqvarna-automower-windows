using System.Text.Json;

namespace HusqaCockpit.Presentation.Tests;

public class AppSettingsTests
{
    // The options of AppSettingsStore.
    private static readonly JsonSerializerOptions s_options = new() { WriteIndented = true };

    [Fact]
    public void The_task_choice_is_written_as_text()
    {
        var json = JsonSerializer.Serialize(new AppSettings { TaskNotifications = TaskNotificationMode.StartAndEnd }, s_options);

        Assert.Contains("\"TaskNotifications\": \"StartAndEnd\"", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(TaskNotificationMode.None)]
    [InlineData(TaskNotificationMode.EndOnly)]
    [InlineData(TaskNotificationMode.StartAndEnd)]
    public void The_task_choice_survives_a_save_and_a_load(TaskNotificationMode mode)
    {
        var json = JsonSerializer.Serialize(new AppSettings { TaskNotifications = mode }, s_options);

        Assert.Equal(mode, JsonSerializer.Deserialize<AppSettings>(json, s_options)!.TaskNotifications);
    }

    [Fact]
    public void A_settings_file_from_an_older_version_gets_the_default_task_choice()
    {
        var older = JsonSerializer.Deserialize<AppSettings>("{ \"Language\": \"fr-FR\", \"NotifyErrors\": false }", s_options)!;

        Assert.Equal(TaskNotificationMode.None, older.TaskNotifications);
        Assert.Equal("fr-FR", older.Language);
        Assert.False(older.NotifyErrors);
    }
}
