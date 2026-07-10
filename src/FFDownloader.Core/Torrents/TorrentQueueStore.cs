using System.Text.Json;

namespace FFDownloader.Core.Torrents;

public sealed class TorrentQueueStore
{
    private const int CurrentVersion = 1;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public IReadOnlyList<TorrentJobRecord> Load(string statePath)
    {
        if (!File.Exists(statePath))
        {
            return [];
        }

        try
        {
            var json = File.ReadAllText(statePath);
            var state = JsonSerializer.Deserialize<QueueState>(json, _jsonOptions);
            return state?.Torrents ?? [];
        }
        catch
        {
            return [];
        }
    }

    public void Save(string statePath, IReadOnlyList<TorrentJobRecord> torrents)
    {
        var directory = Path.GetDirectoryName(statePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var state = new QueueState(CurrentVersion, torrents.ToList());
        File.WriteAllText(statePath, JsonSerializer.Serialize(state, _jsonOptions));
    }

    private sealed record QueueState(int Version, List<TorrentJobRecord> Torrents);
}
