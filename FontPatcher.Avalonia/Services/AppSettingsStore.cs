using System.Text.Json;

namespace FontPatcher.Avalonia.Services;

public sealed class AppSettingsStore : IAppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsPath;

    public AppSettingsStore()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string settingsDirectory = Path.Combine(localAppData, "FontPatcher");
        _settingsPath = Path.Combine(settingsDirectory, "avalonia.settings.json");
    }

    public AppSettingsSnapshot Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return AppSettingsSnapshot.Empty;
            }

            string json = File.ReadAllText(_settingsPath);
            AppSettingsSnapshot? parsed = JsonSerializer.Deserialize<AppSettingsSnapshot>(json, JsonOptions);
            return parsed ?? AppSettingsSnapshot.Empty;
        }
        catch
        {
            return AppSettingsSnapshot.Empty;
        }
    }

    public void Save(AppSettingsSnapshot snapshot)
    {
        try
        {
            string? directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string json = JsonSerializer.Serialize(snapshot, JsonOptions);
            File.WriteAllText(_settingsPath, json);
        }
        catch
        {
            // Ignore persistence errors to keep UI responsive.
        }
    }
}

public interface IAppSettingsStore
{
    AppSettingsSnapshot Load();

    void Save(AppSettingsSnapshot snapshot);
}

public sealed record AppSettingsSnapshot(
    string UnityPath,
    string UnityHubPath,
    string UnityVersion,
    string UnityInstallRoot,
    string TargetGame)
{
    public static AppSettingsSnapshot Empty { get; } = new(
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty);
}
