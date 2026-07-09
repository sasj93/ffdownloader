using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using FFDownloader.App.Services;
using FFDownloader.Core.Settings;
using FFDownloader.Core.Torrents;
using Directory = System.IO.Directory;
using File = System.IO.File;
using Path = System.IO.Path;

namespace FFDownloader.App.ViewModels;

public sealed class TorrentsViewModel : ObservableObject, IDisposable
{
    private readonly TorrentEngineService _engine;
    private readonly TorrentQueueStore _queueStore;
    private readonly AppSettingsStore _settingsStore;
    private readonly WindowDialogService _dialogs;
    private readonly TorrentClientSettings _settings;
    private readonly DispatcherTimer _refreshTimer;
    private readonly Dictionary<Guid, TorrentJobRecord> _records = [];
    private readonly string _torrentFilesFolder;
    private readonly string _cacheFolder;
    private TorrentItemViewModel? _selectedTorrent;
    private string _statusMessage = "Ready";
    private bool _isDisposed;

    public TorrentsViewModel(WindowDialogService dialogs, AppSettingsStore settingsStore)
    {
        _dialogs = dialogs;
        _settingsStore = settingsStore;
        _settings = settingsStore.LoadTorrentSettings();
        _settings.Validate();
        _torrentFilesFolder = Path.Combine(settingsStore.DataFolder, "torrents");
        _cacheFolder = Path.Combine(settingsStore.DataFolder, "torrent-cache");
        Directory.CreateDirectory(_torrentFilesFolder);
        Directory.CreateDirectory(_cacheFolder);

        _engine = new TorrentEngineService(_settings, _cacheFolder);
        _queueStore = new TorrentQueueStore();
        Torrents = [];

        AddMagnetCommand = new AsyncRelayCommand(AddMagnetFromDialogAsync);
        AddTorrentFileCommand = new AsyncRelayCommand(AddTorrentFileFromDialogAsync);
        StartCommand = new AsyncRelayCommand(StartSelectedAsync, () => SelectedTorrent is not null);
        PauseCommand = new AsyncRelayCommand(PauseSelectedAsync, () => SelectedTorrent is not null);
        RemoveCommand = new AsyncRelayCommand(RemoveSelectedAsync, () => SelectedTorrent is not null);
        OpenFolderCommand = new RelayCommand(OpenSelectedFolder, () => SelectedTorrent is not null);
        SaveSettingsCommand = new RelayCommand(SaveSettings);

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _refreshTimer.Tick += async (_, _) => await OnRefreshTickAsync();
        _refreshTimer.Start();

        _ = RestoreQueueAsync();
    }

    public ObservableCollection<TorrentItemViewModel> Torrents { get; }

    public TorrentItemViewModel? SelectedTorrent
    {
        get => _selectedTorrent;
        set
        {
            if (SetProperty(ref _selectedTorrent, value))
            {
                StartCommand.RaiseCanExecuteChanged();
                PauseCommand.RaiseCanExecuteChanged();
                RemoveCommand.RaiseCanExecuteChanged();
                OpenFolderCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public string DownloadFolder
    {
        get => _settings.DownloadFolder;
        set
        {
            if (!string.Equals(_settings.DownloadFolder, value, StringComparison.Ordinal))
            {
                _settings.DownloadFolder = value;
                OnPropertyChanged();
                PersistSettings();
            }
        }
    }

    public int ListenPort
    {
        get => _settings.ListenPort;
        set
        {
            var clamped = Math.Clamp(value, 1, 65535);
            if (_settings.ListenPort != clamped)
            {
                _settings.ListenPort = clamped;
                OnPropertyChanged();
                _ = ApplySettingsAsync();
            }
        }
    }

    public long MaxDownloadSpeedKilobytesPerSecond
    {
        get => _settings.MaxDownloadSpeedBytesPerSecond / 1024;
        set
        {
            var bytes = Math.Max(0, value) * 1024;
            if (_settings.MaxDownloadSpeedBytesPerSecond != bytes)
            {
                _settings.MaxDownloadSpeedBytesPerSecond = bytes;
                OnPropertyChanged();
                _ = ApplySettingsAsync();
            }
        }
    }

    public long MaxUploadSpeedKilobytesPerSecond
    {
        get => _settings.MaxUploadSpeedBytesPerSecond / 1024;
        set
        {
            var bytes = Math.Max(0, value) * 1024;
            if (_settings.MaxUploadSpeedBytesPerSecond != bytes)
            {
                _settings.MaxUploadSpeedBytesPerSecond = bytes;
                OnPropertyChanged();
                _ = ApplySettingsAsync();
            }
        }
    }

    public bool EnableDht
    {
        get => _settings.EnableDht;
        set
        {
            if (_settings.EnableDht != value)
            {
                _settings.EnableDht = value;
                OnPropertyChanged();
                PersistSettings();
            }
        }
    }

    public bool EnablePeerExchange
    {
        get => _settings.EnablePeerExchange;
        set
        {
            if (_settings.EnablePeerExchange != value)
            {
                _settings.EnablePeerExchange = value;
                OnPropertyChanged();
                PersistSettings();
            }
        }
    }

    public bool EnableLocalPeerDiscovery
    {
        get => _settings.EnableLocalPeerDiscovery;
        set
        {
            if (_settings.EnableLocalPeerDiscovery != value)
            {
                _settings.EnableLocalPeerDiscovery = value;
                OnPropertyChanged();
                _ = ApplySettingsAsync();
            }
        }
    }

    public bool EnablePortForwarding
    {
        get => _settings.EnablePortForwarding;
        set
        {
            if (_settings.EnablePortForwarding != value)
            {
                _settings.EnablePortForwarding = value;
                OnPropertyChanged();
                _ = ApplySettingsAsync();
            }
        }
    }

    public double SeedRatioLimit
    {
        get => _settings.SeedRatioLimit;
        set
        {
            var clamped = Math.Max(0, value);
            if (_settings.SeedRatioLimit != clamped)
            {
                _settings.SeedRatioLimit = clamped;
                OnPropertyChanged();
                PersistSettings();
            }
        }
    }

    public int SeedTimeLimitMinutes
    {
        get => _settings.SeedTimeLimitMinutes;
        set
        {
            var clamped = Math.Max(0, value);
            if (_settings.SeedTimeLimitMinutes != clamped)
            {
                _settings.SeedTimeLimitMinutes = clamped;
                OnPropertyChanged();
                PersistSettings();
            }
        }
    }

    public bool StopSeedingAtLimit
    {
        get => _settings.StopSeedingAtLimit;
        set
        {
            if (_settings.StopSeedingAtLimit != value)
            {
                _settings.StopSeedingAtLimit = value;
                OnPropertyChanged();
                PersistSettings();
            }
        }
    }

    public AsyncRelayCommand AddMagnetCommand { get; }

    public AsyncRelayCommand AddTorrentFileCommand { get; }

    public AsyncRelayCommand StartCommand { get; }

    public AsyncRelayCommand PauseCommand { get; }

    public AsyncRelayCommand RemoveCommand { get; }

    public RelayCommand OpenFolderCommand { get; }

    public RelayCommand SaveSettingsCommand { get; }

    private string QueueStatePath => Path.Combine(_settingsStore.DataFolder, "torrent-queue.json");

    private async Task RestoreQueueAsync()
    {
        var records = _queueStore.Load(QueueStatePath);
        foreach (var record in records)
        {
            try
            {
                var manager = record.IsMagnet
                    ? await _engine.AddMagnetAsync(record.Source, record.SaveDirectory, _settings)
                    : await _engine.AddTorrentFileAsync(record.Source, record.SaveDirectory, _settings);

                _records[record.Id] = record;
                var itemViewModel = new TorrentItemViewModel(record.Id, manager, record.SaveDirectory, record.IsMagnet);
                Torrents.Add(itemViewModel);
                SelectedTorrent ??= itemViewModel;

                if (!record.IsPaused)
                {
                    await manager.StartAsync();
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, $"Failed to restore torrent {record.Source}");
            }
        }
    }

    private async Task AddMagnetFromDialogAsync()
    {
        var text = _dialogs.ShowLinkInput();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var magnets = TorrentSourceParser.ParseMagnetLinks(text);
        if (magnets.Count == 0)
        {
            _dialogs.ShowInfo("No magnet link was found in the pasted text.");
            return;
        }

        foreach (var magnet in magnets)
        {
            await AddTorrentAsync(magnet, isMagnet: true);
        }

        StatusMessage = $"{magnets.Count} magnet link(s) added.";
    }

    private async Task AddTorrentFileFromDialogAsync()
    {
        var path = _dialogs.BrowseTorrentFile();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var id = Guid.NewGuid();
        var storedPath = Path.Combine(_torrentFilesFolder, $"{id}.torrent");

        try
        {
            File.Copy(path, storedPath, overwrite: true);
        }
        catch (Exception ex)
        {
            _dialogs.ShowError($"Could not read the .torrent file: {ex.Message}");
            return;
        }

        await AddTorrentAsync(storedPath, isMagnet: false, id);
        StatusMessage = "Torrent file added.";
    }

    private async Task AddTorrentAsync(string source, bool isMagnet, Guid? existingId = null)
    {
        var id = existingId ?? Guid.NewGuid();
        try
        {
            var manager = isMagnet
                ? await _engine.AddMagnetAsync(source, _settings.DownloadFolder, _settings)
                : await _engine.AddTorrentFileAsync(source, _settings.DownloadFolder, _settings);

            var record = new TorrentJobRecord(id, source, _settings.DownloadFolder, DateTimeOffset.UtcNow, IsPaused: false);
            _records[id] = record;
            var itemViewModel = new TorrentItemViewModel(id, manager, _settings.DownloadFolder, isMagnet);
            Torrents.Add(itemViewModel);
            SelectedTorrent ??= itemViewModel;

            await manager.StartAsync();
            SaveQueueState();
        }
        catch (Exception ex)
        {
            _dialogs.ShowError($"Failed to add torrent: {ex.Message}");
        }
    }

    private async Task StartSelectedAsync()
    {
        if (SelectedTorrent is null)
        {
            return;
        }

        await SelectedTorrent.Manager.StartAsync();
        UpdateRecordPaused(SelectedTorrent.Id, false);
        SaveQueueState();
    }

    private async Task PauseSelectedAsync()
    {
        if (SelectedTorrent is null)
        {
            return;
        }

        await SelectedTorrent.Manager.PauseAsync();
        UpdateRecordPaused(SelectedTorrent.Id, true);
        SaveQueueState();
    }

    private async Task RemoveSelectedAsync()
    {
        if (SelectedTorrent is null)
        {
            return;
        }

        var torrent = SelectedTorrent;
        await _engine.RemoveAsync(torrent.Manager);
        Torrents.Remove(torrent);

        if (_records.Remove(torrent.Id, out var record) && !torrent.IsMagnet && File.Exists(record.Source))
        {
            try
            {
                File.Delete(record.Source);
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "Failed to delete stored .torrent file after removal");
            }
        }

        SelectedTorrent = Torrents.FirstOrDefault();
        SaveQueueState();
    }

    private void OpenSelectedFolder()
    {
        if (SelectedTorrent is null)
        {
            return;
        }

        _dialogs.OpenFolder(SelectedTorrent.SaveDirectory);
    }

    private void UpdateRecordPaused(Guid id, bool isPaused)
    {
        if (_records.TryGetValue(id, out var record))
        {
            _records[id] = record with { IsPaused = isPaused };
        }
    }

    private async Task OnRefreshTickAsync()
    {
        try
        {
            foreach (var torrent in Torrents)
            {
                torrent.Refresh();
            }

            await TorrentEngineService.EnforceSeedLimitsAsync(_engine.Managers, _settings);
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Torrent refresh tick failed");
        }
    }

    private void SaveQueueState()
    {
        try
        {
            _queueStore.Save(QueueStatePath, _records.Values.ToList());
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Failed to save torrent queue state");
        }
    }

    private void PersistSettings()
    {
        try
        {
            _settingsStore.SaveTorrentSettings(_settings);
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Failed to persist torrent settings");
        }
    }

    private async Task ApplySettingsAsync()
    {
        PersistSettings();
        try
        {
            await _engine.UpdateSettingsAsync(_settings, _cacheFolder);
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Failed to apply updated torrent engine settings");
        }
    }

    private void SaveSettings()
    {
        PersistSettings();
        StatusMessage = "Torrent settings saved.";
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _refreshTimer.Stop();
        SaveQueueState();

        // Run off the calling thread: if a WPF DispatcherSynchronizationContext is active (e.g.
        // disposing from MainWindow.Closed), blocking here while the engine's internal awaits try
        // to resume on that same, now-blocked, thread would deadlock.
        Task.Run(() => _engine.DisposeAsync().AsTask()).GetAwaiter().GetResult();
        _isDisposed = true;
    }
}
