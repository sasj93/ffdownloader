using System.Text.Json;
using FFDownloader.Core.Settings;
using Directory = System.IO.Directory;
using File = System.IO.File;
using Path = System.IO.Path;

namespace FFDownloader.App.Services;

public sealed class AppSettingsStore
{
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public string DataFolder { get; } = Path.Combine(AppContext.BaseDirectory, "data");

    public string SettingsPath => Path.Combine(DataFolder, "settings.json");

    public DownloadSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return DownloadSettings.CreateDefault();
            }

            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<DownloadSettings>(json, _jsonOptions) ?? DownloadSettings.CreateDefault();
            settings.Validate();
            return settings;
        }
        catch
        {
            return DownloadSettings.CreateDefault();
        }
    }

    public void Save(DownloadSettings settings)
    {
        settings.Validate();
        Directory.CreateDirectory(DataFolder);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, _jsonOptions));
    }
}
