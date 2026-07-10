using System.Net;
using FFDownloader.Core.Settings;
using MonoTorrent;
using MonoTorrent.Client;

namespace FFDownloader.Core.Torrents;

public sealed class TorrentEngineService : IAsyncDisposable
{
    private readonly ClientEngine _engine;

    public TorrentEngineService(TorrentClientSettings settings, string cacheDirectory)
    {
        _engine = new ClientEngine(BuildEngineSettings(settings, cacheDirectory));
    }

    public IList<TorrentManager> Managers => _engine.Torrents;

    public async Task<TorrentManager> AddMagnetAsync(string magnetUri, string saveDirectory, TorrentClientSettings settings)
    {
        Directory.CreateDirectory(saveDirectory);
        var magnetLink = MagnetLink.Parse(magnetUri);
        return await _engine.AddAsync(magnetLink, saveDirectory, BuildTorrentSettings(settings));
    }

    public async Task<TorrentManager> AddTorrentFileAsync(string torrentFilePath, string saveDirectory, TorrentClientSettings settings)
    {
        Directory.CreateDirectory(saveDirectory);
        var torrent = await Torrent.LoadAsync(torrentFilePath);
        return await _engine.AddAsync(torrent, saveDirectory, BuildTorrentSettings(settings));
    }

    public async Task RemoveAsync(TorrentManager manager)
    {
        if (manager.State is not (TorrentState.Stopped or TorrentState.Error))
        {
            await manager.StopAsync();
        }

        await _engine.RemoveAsync(manager);
    }

    public async Task UpdateSettingsAsync(TorrentClientSettings settings, string cacheDirectory)
    {
        await _engine.UpdateSettingsAsync(BuildEngineSettings(settings, cacheDirectory));
    }

    /// <summary>
    /// MonoTorrent has no built-in seed ratio/time cap, so callers should invoke this periodically
    /// (e.g. from a UI refresh timer) to stop torrents that are seeding past the configured limit.
    /// </summary>
    public static async Task EnforceSeedLimitsAsync(IEnumerable<TorrentManager> managers, TorrentClientSettings settings)
    {
        if (!settings.StopSeedingAtLimit)
        {
            return;
        }

        foreach (var manager in managers)
        {
            if (manager.State != TorrentState.Seeding)
            {
                continue;
            }

            var ratioExceeded = settings.SeedRatioLimit > 0
                && manager.Torrent is { Size: > 0 } torrent
                && manager.Monitor.DataBytesSent / (double)torrent.Size >= settings.SeedRatioLimit;

            var timeExceeded = settings.SeedTimeLimitMinutes > 0
                && DateTime.UtcNow - manager.StartTime.ToUniversalTime() >= TimeSpan.FromMinutes(settings.SeedTimeLimitMinutes);

            if (ratioExceeded || timeExceeded)
            {
                await manager.StopAsync();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _engine.StopAllAsync();
        _engine.Dispose();
    }

    private static TorrentSettings BuildTorrentSettings(TorrentClientSettings settings)
    {
        return new TorrentSettingsBuilder
        {
            AllowDht = settings.EnableDht,
            AllowPeerExchange = settings.EnablePeerExchange,
            CreateContainingDirectory = true
        }.ToSettings();
    }

    private static EngineSettings BuildEngineSettings(TorrentClientSettings settings, string cacheDirectory)
    {
        var listenEndPoints = new Dictionary<string, IPEndPoint>
        {
            ["ipv4"] = new IPEndPoint(IPAddress.Any, settings.ListenPort),
            ["ipv6"] = new IPEndPoint(IPAddress.IPv6Any, settings.ListenPort)
        };

        return new EngineSettingsBuilder
        {
            CacheDirectory = cacheDirectory,
            AllowPortForwarding = settings.EnablePortForwarding,
            AllowLocalPeerDiscovery = settings.EnableLocalPeerDiscovery,
            AutoSaveLoadDhtCache = true,
            AutoSaveLoadFastResume = true,
            AutoSaveLoadMagnetLinkMetadata = true,
            ListenEndPoints = listenEndPoints,
            DhtEndPoint = new IPEndPoint(IPAddress.Any, settings.ListenPort),
            MaximumDownloadRate = ClampToInt(settings.MaxDownloadSpeedBytesPerSecond),
            MaximumUploadRate = ClampToInt(settings.MaxUploadSpeedBytesPerSecond)
        }.ToSettings();
    }

    private static int ClampToInt(long value) => (int)Math.Clamp(value, 0, int.MaxValue);
}
