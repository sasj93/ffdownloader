using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Net.Http;
using FFDownloader.App.Services;
using FFDownloader.Core.Downloads;
using FFDownloader.Core.Extraction;
using FFDownloader.Core.Hosts;
using FFDownloader.Core.Links;
using FFDownloader.Core.Settings;
using Path = System.IO.Path;

namespace FFDownloader.App.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly DownloadQueue _queue = new();
    private readonly ResolverRegistry _resolverRegistry;
    private readonly HttpDownloadService _downloadService;
    private readonly DownloadQueueStore _queueStore;
    private readonly ArchiveExtractor _archiveExtractor;
    private readonly AppSettingsStore _settingsStore;
    private readonly ClipboardMonitor _clipboardMonitor;
    private readonly WindowDialogService _dialogs;
    private readonly DownloadSettings _settings;
    private CancellationTokenSource? _downloadCts;
    private PackageViewModel? _selectedPackage;
    private string _statusMessage = "Pronto";
    private bool _isDownloading;
    private bool _isDisposed;
    private DateTimeOffset _lastQueueSave = DateTimeOffset.MinValue;

    public MainViewModel(WindowDialogService dialogs)
    {
        _dialogs = dialogs;
        _settingsStore = new AppSettingsStore();
        _settings = _settingsStore.Load();
        _settings.Validate();
        _resolverRegistry = new ResolverRegistry([new AutomaticFuckingFastResolver()]);
        _downloadService = new HttpDownloadService();
        _queueStore = new DownloadQueueStore();
        _archiveExtractor = new ArchiveExtractor();
        _clipboardMonitor = new ClipboardMonitor();
        _clipboardMonitor.LinksDetected += ClipboardMonitor_LinksDetected;

        _queue.ReplacePackages(_queueStore.Load(QueueStatePath));
        Packages = new ObservableCollection<PackageViewModel>(_queue.Packages.Select(package => new PackageViewModel(package)));
        SelectedPackage = Packages.FirstOrDefault();
        AddLinksCommand = new RelayCommand(AddLinksFromDialog);
        StartCommand = new AsyncRelayCommand(StartDownloadsAsync, () => !IsDownloading && Packages.Any(package => package.Items.Any(item => item.Status is DownloadStatus.Queued or DownloadStatus.Failed or DownloadStatus.Paused)));
        PauseCommand = new RelayCommand(PauseDownloads, () => IsDownloading);
        RemoveSelectedCommand = new RelayCommand(RemoveSelectedPackage, () => SelectedPackage is not null);
        OpenDestinationCommand = new RelayCommand(() => _dialogs.OpenFolder(DestinationFolder));
        SaveSettingsCommand = new RelayCommand(SaveSettings);
        ClearCompletedCommand = new RelayCommand(ClearCompleted);

        if (MonitorClipboard)
        {
            _clipboardMonitor.Start();
        }
    }

    public ObservableCollection<PackageViewModel> Packages { get; }

    public PackageViewModel? SelectedPackage
    {
        get => _selectedPackage;
        set
        {
            if (SetProperty(ref _selectedPackage, value))
            {
                RemoveSelectedCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public bool IsDownloading
    {
        get => _isDownloading;
        set
        {
            if (SetProperty(ref _isDownloading, value))
            {
                StartCommand.RaiseCanExecuteChanged();
                PauseCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string DestinationFolder
    {
        get => _settings.DestinationFolder;
        set
        {
            if (!string.Equals(_settings.DestinationFolder, value, StringComparison.Ordinal))
            {
                _settings.DestinationFolder = value;
                OnPropertyChanged();
            }
        }
    }

    public int MaxConcurrentDownloads
    {
        get => _settings.MaxConcurrentDownloads;
        set
        {
            if (_settings.MaxConcurrentDownloads != value)
            {
                _settings.MaxConcurrentDownloads = value;
                OnPropertyChanged();
            }
        }
    }

    public int ConnectionsPerFile
    {
        get => _settings.ConnectionsPerFile;
        set
        {
            if (_settings.ConnectionsPerFile != value)
            {
                _settings.ConnectionsPerFile = value;
                OnPropertyChanged();
            }
        }
    }

    public int RetryCount
    {
        get => _settings.RetryCount;
        set
        {
            if (_settings.RetryCount != value)
            {
                _settings.RetryCount = value;
                OnPropertyChanged();
            }
        }
    }

    public long SpeedLimitKilobytesPerSecond
    {
        get => _settings.SpeedLimitBytesPerSecond / 1024;
        set
        {
            var bytes = Math.Max(0, value) * 1024;
            if (_settings.SpeedLimitBytesPerSecond != bytes)
            {
                _settings.SpeedLimitBytesPerSecond = bytes;
                OnPropertyChanged();
            }
        }
    }

    public long MinMultiConnectionSizeMegabytes
    {
        get => _settings.MinMultiConnectionSizeBytes / 1024 / 1024;
        set
        {
            var bytes = Math.Max(0, value) * 1024 * 1024;
            if (_settings.MinMultiConnectionSizeBytes != bytes)
            {
                _settings.MinMultiConnectionSizeBytes = bytes;
                OnPropertyChanged();
            }
        }
    }

    public bool EnableMultiConnectionDownloads
    {
        get => _settings.EnableMultiConnectionDownloads;
        set
        {
            if (_settings.EnableMultiConnectionDownloads != value)
            {
                _settings.EnableMultiConnectionDownloads = value;
                OnPropertyChanged();
            }
        }
    }

    public bool EnableAdaptiveConnectionCount
    {
        get => _settings.EnableAdaptiveConnectionCount;
        set
        {
            if (_settings.EnableAdaptiveConnectionCount != value)
            {
                _settings.EnableAdaptiveConnectionCount = value;
                OnPropertyChanged();
            }
        }
    }

    public bool UseTemporaryDownloadFiles
    {
        get => _settings.UseTemporaryDownloadFiles;
        set
        {
            if (_settings.UseTemporaryDownloadFiles != value)
            {
                _settings.UseTemporaryDownloadFiles = value;
                OnPropertyChanged();
            }
        }
    }

    public bool ValidateRemoteIdentity
    {
        get => _settings.ValidateRemoteIdentity;
        set
        {
            if (_settings.ValidateRemoteIdentity != value)
            {
                _settings.ValidateRemoteIdentity = value;
                OnPropertyChanged();
            }
        }
    }

    public bool RenewExpiredLinks
    {
        get => _settings.RenewExpiredLinks;
        set
        {
            if (_settings.RenewExpiredLinks != value)
            {
                _settings.RenewExpiredLinks = value;
                OnPropertyChanged();
            }
        }
    }

    public bool MonitorClipboard
    {
        get => _settings.MonitorClipboard;
        set
        {
            if (_settings.MonitorClipboard != value)
            {
                _settings.MonitorClipboard = value;
                if (value)
                {
                    _clipboardMonitor.Start();
                }
                else
                {
                    _clipboardMonitor.Stop();
                }
                OnPropertyChanged();
            }
        }
    }

    public bool AutoExtract
    {
        get => _settings.AutoExtract;
        set
        {
            if (_settings.AutoExtract != value)
            {
                _settings.AutoExtract = value;
                OnPropertyChanged();
            }
        }
    }

    public bool AutoStart
    {
        get => _settings.AutoStart;
        set
        {
            if (_settings.AutoStart != value)
            {
                _settings.AutoStart = value;
                OnPropertyChanged();
            }
        }
    }

    public bool CreateSubfolderPerPackage
    {
        get => _settings.CreateSubfolderPerPackage;
        set
        {
            if (_settings.CreateSubfolderPerPackage != value)
            {
                _settings.CreateSubfolderPerPackage = value;
                OnPropertyChanged();
            }
        }
    }

    public RelayCommand AddLinksCommand { get; }

    public AsyncRelayCommand StartCommand { get; }

    public RelayCommand PauseCommand { get; }

    public RelayCommand RemoveSelectedCommand { get; }

    public RelayCommand OpenDestinationCommand { get; }

    public RelayCommand SaveSettingsCommand { get; }

    public RelayCommand ClearCompletedCommand { get; }

    private string QueueStatePath => Path.Combine(_settingsStore.DataFolder, "queue.json");

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _downloadCts?.Cancel();
        SaveQueueState();
        _downloadCts?.Dispose();
        _clipboardMonitor.Dispose();
        _isDisposed = true;
    }

    private void ClipboardMonitor_LinksDetected(object? sender, IReadOnlyList<LinkCandidate> links)
    {
        if (_dialogs.ConfirmAddLinks(links.Count))
        {
            AddLinks(links);
            if (AutoStart)
            {
                StartCommand.Execute(null);
            }
        }
    }

    private void AddLinksFromDialog()
    {
        var text = _dialogs.ShowLinkInput();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        _clipboardMonitor.Remember(text);
        var links = DownloadLinkParser.ParseMany(text);
        if (links.Count == 0)
        {
            _dialogs.ShowInfo("Nenhum link do FuckingFast foi encontrado.");
            return;
        }

        AddLinks(links);
    }

    private void AddLinks(IReadOnlyList<LinkCandidate> links)
    {
        var packages = _queue.AddLinks(links, DestinationFolder);
        foreach (var package in packages)
        {
            package.AutoExtract = AutoExtract;
            var existing = Packages.FirstOrDefault(vm => vm.Model.Id == package.Id);
            if (existing is null)
            {
                existing = new PackageViewModel(package);
                Packages.Add(existing);
            }
            else
            {
                existing.SyncItems();
            }

            SelectedPackage ??= existing;
        }

        RefreshAllPackages();
        StatusMessage = $"{links.Count} link(s) adicionados.";
        SaveQueueState();
        StartCommand.RaiseCanExecuteChanged();
    }

    private async Task StartDownloadsAsync()
    {
        try
        {
            _settings.Validate();
            SaveSettings();
            SaveQueueState();
        }
        catch (Exception ex)
        {
            _dialogs.ShowError(ex.Message);
            return;
        }

        _downloadCts?.Cancel();
        _downloadCts?.Dispose();
        _downloadCts = new CancellationTokenSource();
        var token = _downloadCts.Token;
        IsDownloading = true;
        StatusMessage = "Baixando...";

        try
        {
            using var semaphore = new SemaphoreSlim(MaxConcurrentDownloads);
            var jobs = Packages
                .SelectMany(package => package.Items.Select(item => new { Package = package, Item = item }))
                .Where(entry => entry.Item.Status is DownloadStatus.Queued or DownloadStatus.Failed or DownloadStatus.Paused)
                .Select(entry => RunItemAsync(entry.Package, entry.Item, semaphore, token))
                .ToList();

            await Task.WhenAll(jobs);

            foreach (var package in Packages)
            {
                await ExtractPackageIfNeededAsync(package, token);
            }

            SaveQueueState();
            StatusMessage = "Fila finalizada.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Downloads pausados.";
        }
        finally
        {
            IsDownloading = false;
            RefreshAllPackages();
            SaveQueueState();
        }
    }

    private void PauseDownloads()
    {
        _downloadCts?.Cancel();
        StatusMessage = "Pausando...";
        SaveQueueState();
    }

    private void RemoveSelectedPackage()
    {
        if (SelectedPackage is null)
        {
            return;
        }

        _queue.RemovePackage(SelectedPackage.Id);
        Packages.Remove(SelectedPackage);
        SelectedPackage = Packages.FirstOrDefault();
        SaveQueueState();
        StartCommand.RaiseCanExecuteChanged();
    }

    private void ClearCompleted()
    {
        _queue.ClearCompleted();
        foreach (var package in Packages.Where(package => package.Items.All(item => item.Status is DownloadStatus.Completed or DownloadStatus.Extracted)).ToList())
        {
            Packages.Remove(package);
        }
        SelectedPackage = Packages.FirstOrDefault();
        SaveQueueState();
    }

    private async Task RunItemAsync(PackageViewModel package, DownloadItemViewModel item, SemaphoreSlim semaphore, CancellationToken token)
    {
        await semaphore.WaitAsync(token);
        try
        {
            item.Status = DownloadStatus.Resolving;
            package.RefreshComputed();

            var resolver = _resolverRegistry.FindResolver(item.Model.Link);
            if (resolver is null)
            {
                throw new InvalidOperationException($"Host nao suportado: {item.Host}");
            }

            var destination = GetPackageDestination(package);
            var progress = new Progress<DownloadProgress>(_ =>
            {
                item.RefreshFromModel();
                package.RefreshComputed();
                SaveQueueStateThrottled();
            });

            await DownloadWithRenewalAsync(resolver, item.Model, destination, progress, token);
            item.RefreshFromModel();
            package.RefreshComputed();
            SaveQueueState();
        }
        catch (OperationCanceledException)
        {
            item.Status = DownloadStatus.Paused;
            SaveQueueState();
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, $"Download item failed: {item.FileName}");
            item.Status = DownloadStatus.Failed;
            item.ErrorMessage = ex.Message;
            StatusMessage = $"Erro em {item.FileName}: {ex.Message}";
            SaveQueueState();
        }
        finally
        {
            package.RefreshComputed();
            semaphore.Release();
        }
    }

    private async Task DownloadWithRenewalAsync(
        IHostResolver resolver,
        DownloadItem item,
        string destination,
        IProgress<DownloadProgress> progress,
        CancellationToken token)
    {
        var attempts = Math.Max(1, RetryCount + 1);
        Exception? last = null;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            token.ThrowIfCancellationRequested();

            try
            {
                item.Status = DownloadStatus.Resolving;
                progress.Report(new DownloadProgress(item.Id, item.DownloadedBytes, item.SizeBytes, item.SpeedBytesPerSecond, item.Status));

                var resolved = await ResolveWithRetriesAsync(resolver, item.Link, token);
                await _downloadService.DownloadAsync(item, resolved, destination, _settings, progress, token);
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (attempt < attempts && ShouldRetryDownload(ex))
            {
                last = ex;
                item.Status = DownloadStatus.Resolving;
                item.ErrorMessage = ShouldRenewLink(ex)
                    ? "URL expirada; renovando link..."
                    : "Falha temporaria; tentando novamente...";
                item.ResolvedUrl = ShouldRenewLink(ex) ? null : item.ResolvedUrl;
                progress.Report(new DownloadProgress(item.Id, item.DownloadedBytes, item.SizeBytes, item.SpeedBytesPerSecond, item.Status));
                SaveQueueState();
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(attempt, 5)), token);
            }
            catch (Exception ex)
            {
                last = ex;
                break;
            }
        }

        throw last ?? new InvalidOperationException("Nao foi possivel baixar o arquivo.");
    }

    private bool ShouldRetryDownload(Exception exception)
    {
        return ShouldRenewLink(exception)
            || exception is HttpRequestException
            || exception is IOException;
    }

    private bool ShouldRenewLink(Exception exception)
    {
        if (!RenewExpiredLinks)
        {
            return false;
        }

        if (exception is HttpRequestException { StatusCode: HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized or HttpStatusCode.NotFound or HttpStatusCode.Gone or HttpStatusCode.TooManyRequests })
        {
            return true;
        }

        var message = exception.ToString();
        return message.Contains("403", StringComparison.OrdinalIgnoreCase)
            || message.Contains("404", StringComparison.OrdinalIgnoreCase)
            || message.Contains("410", StringComparison.OrdinalIgnoreCase)
            || message.Contains("expired", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<ResolvedDownload> ResolveWithRetriesAsync(IHostResolver resolver, LinkCandidate link, CancellationToken token)
    {
        var attempts = Math.Max(1, RetryCount + 1);
        Exception? last = null;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                return await resolver.ResolveAsync(link, token);
            }
            catch (ResolveRequiresBrowserException)
            {
                throw;
            }
            catch (Exception ex)
            {
                last = ex;
                if (attempt < attempts)
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Min(attempt, 5)), token);
                }
            }
        }

        throw last ?? new InvalidOperationException("Nao foi possivel resolver o link.");
    }

    private async Task ExtractPackageIfNeededAsync(PackageViewModel package, CancellationToken token)
    {
        if (!package.AutoExtract && !AutoExtract)
        {
            return;
        }

        if (package.Items.Any(item => item.Status != DownloadStatus.Completed))
        {
            return;
        }

        var archiveItem = package.Items
            .OrderBy(item => item.PartNumber ?? int.MaxValue)
            .FirstOrDefault(item => item.LocalPath is not null && ArchiveExtractor.IsSupportedArchive(item.LocalPath));

        if (archiveItem?.LocalPath is null)
        {
            return;
        }

        try
        {
            archiveItem.Status = DownloadStatus.Extracting;
            var extractTo = Path.Combine(GetPackageDestination(package), "_extracted");
            await _archiveExtractor.ExtractAsync(archiveItem.LocalPath, extractTo, package.Password, token);
            foreach (var item in package.Items)
            {
                item.Status = DownloadStatus.Extracted;
            }
            StatusMessage = $"Extraido: {package.Name}";
        }
        catch (Exception ex)
        {
            archiveItem.Status = DownloadStatus.Failed;
            archiveItem.ErrorMessage = $"Extracao falhou: {ex.Message}";
        }
        finally
        {
            package.RefreshComputed();
        }
    }

    private string GetPackageDestination(PackageViewModel package)
    {
        return CreateSubfolderPerPackage
            ? Path.Combine(package.DestinationFolder, SanitizeFolderName(package.Name))
            : package.DestinationFolder;
    }

    private void SaveQueueStateThrottled()
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastQueueSave < TimeSpan.FromSeconds(1))
        {
            return;
        }

        SaveQueueState();
    }

    private void SaveQueueState()
    {
        try
        {
            _queueStore.Save(QueueStatePath, _queue.Packages);
            _lastQueueSave = DateTimeOffset.UtcNow;
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Failed to save download queue state");
        }
    }

    private void SaveSettings()
    {
        try
        {
            _settingsStore.Save(_settings);
            StatusMessage = "Configuracoes salvas.";
        }
        catch (Exception ex)
        {
            _dialogs.ShowError(ex.Message);
        }
    }

    private void RefreshAllPackages()
    {
        foreach (var package in Packages)
        {
            package.RefreshComputed();
        }
        StartCommand.RaiseCanExecuteChanged();
    }

    private static string SanitizeFolderName(string name)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        return string.IsNullOrWhiteSpace(name) ? "package" : name;
    }
}
