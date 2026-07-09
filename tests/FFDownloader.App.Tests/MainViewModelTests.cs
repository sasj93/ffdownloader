using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using FFDownloader.App.Services;
using FFDownloader.App.ViewModels;
using FFDownloader.Core.Downloads;
using FFDownloader.Core.Hosts;
using FFDownloader.Core.Links;
using FFDownloader.Core.Settings;
using FluentAssertions;

namespace FFDownloader.App.Tests;

public sealed class MainViewModelTests
{
    [Fact]
    public void Constructor_restores_saved_queue_and_selects_first_package()
    {
        RunOnStaThread(() =>
        {
            var settingsStore = new AppSettingsStore();
            ResetDataFolder(settingsStore.DataFolder);

            var settings = CreateTestSettings();
            settingsStore.Save(settings);

            var item = new DownloadItem(new LinkCandidate(
                "https://fuckingfast.co/example#Game.part001.rar",
                "fuckingfast.co",
                "Game.part001.rar",
                "Game",
                1,
                true))
            {
                Status = DownloadStatus.Failed,
                ErrorMessage = "resolver timeout"
            };

            var package = new DownloadPackageJob("Game", settings.DestinationFolder, [item]);
            new DownloadQueueStore().Save(Path.Combine(settingsStore.DataFolder, "queue.json"), [package]);

            var owner = new Window();
            try
            {
                using var viewModel = new MainViewModel(new WindowDialogService(owner));

                viewModel.SelectedPackage.Should().NotBeNull();
                viewModel.SelectedPackage!.Name.Should().Be("Game");
                viewModel.FailedFiles.Should().Be(1);
            }
            finally
            {
                owner.Close();
            }
        });
    }

    [Fact]
    public async Task ResolveWithRetries_retries_browser_resolver_failures()
    {
        await RunOnStaThreadAsync(async () =>
        {
            var settingsStore = new AppSettingsStore();
            ResetDataFolder(settingsStore.DataFolder);

            var settings = CreateTestSettings();
            settings.RetryCount = 1;
            settingsStore.Save(settings);

            var owner = new Window();
            try
            {
                using var viewModel = new MainViewModel(new WindowDialogService(owner));
                var resolver = new FailsOnceResolver();
                var link = new LinkCandidate(
                    "https://fuckingfast.co/example#Game.part001.rar",
                    "fuckingfast.co",
                    "Game.part001.rar",
                    "Game",
                    1,
                    true);

                var resolved = await InvokeResolveWithRetriesAsync(viewModel, resolver, link);

                resolved.DownloadUrl.Should().Be(FailsOnceResolver.DownloadUrl);
                resolver.Attempts.Should().Be(2);
            }
            finally
            {
                owner.Close();
            }
        });
    }

    private static DownloadSettings CreateTestSettings()
    {
        var settings = DownloadSettings.CreateDefault();
        settings.DestinationFolder = Path.Combine(AppContext.BaseDirectory, "downloads");
        settings.MaxConcurrentDownloads = 1;
        settings.ConnectionsPerFile = 1;
        settings.MonitorClipboard = false;
        return settings;
    }

    private static Task<ResolvedDownload> InvokeResolveWithRetriesAsync(
        MainViewModel viewModel,
        IHostResolver resolver,
        LinkCandidate link)
    {
        var method = typeof(MainViewModel).GetMethod(
            "ResolveWithRetriesAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);

        method.Should().NotBeNull();
        return (Task<ResolvedDownload>)method!.Invoke(viewModel, [resolver, link, CancellationToken.None])!;
    }

    private static void ResetDataFolder(string dataFolder)
    {
        var fullPath = Path.GetFullPath(dataFolder);
        var basePath = Path.GetFullPath(AppContext.BaseDirectory);
        fullPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase).Should().BeTrue();

        if (Directory.Exists(fullPath))
        {
            Directory.Delete(fullPath, recursive: true);
        }
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static async Task RunOnStaThreadAsync(Func<Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread? thread = null;
        thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));

            dispatcher.BeginInvoke(async () =>
            {
                try
                {
                    await action();
                    completion.SetResult();
                }
                catch (Exception ex)
                {
                    completion.SetException(ex);
                }
                finally
                {
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Normal);
                }
            });

            Dispatcher.Run();
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        try
        {
            await completion.Task;
        }
        finally
        {
            thread.Join();
        }
    }

    private sealed class FailsOnceResolver : IHostResolver
    {
        public const string DownloadUrl = "https://dl.fuckingfast.co/dl/token";

        public int Attempts { get; private set; }

        public string Host => "fuckingfast.co";

        public bool CanResolve(LinkCandidate link) => true;

        public Task<ResolvedDownload> ResolveAsync(LinkCandidate link, CancellationToken cancellationToken)
        {
            Attempts++;
            if (Attempts == 1)
            {
                throw new ResolveRequiresBrowserException("Automatic WebView2 resolver timed out for Game.part001.rar: A task was canceled.");
            }

            return Task.FromResult(new ResolvedDownload(DownloadUrl, link.FileName, 123));
        }
    }
}
