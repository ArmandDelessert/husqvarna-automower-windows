namespace HusqaCockpit.Presentation;

/// <summary>Writes the settings after a change.</summary>
public interface ISettingsStore
{
    void Save(AppSettings settings);
}
