using System.Text.Json;
using System.Windows;
using FFDownloader.Core.Hosts;
using FFDownloader.Core.Links;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace FFDownloader.App.Services;

public sealed class AutomaticDatanodesResolver : IHostResolver
{
    private static readonly TimeSpan BrowserTimeout = TimeSpan.FromSeconds(40);
    private readonly DatanodesResolver _httpResolver = new();

    public string Host => _httpResolver.Host;

    public bool CanResolve(LinkCandidate link) => _httpResolver.CanResolve(link);

    public async Task<ResolvedDownload> ResolveAsync(LinkCandidate link, CancellationToken cancellationToken)
    {
        try
        {
            return await _httpResolver.ResolveAsync(link, cancellationToken);
        }
        catch (ResolveRequiresBrowserException ex)
        {
            AppLogger.Info($"HTTP resolver did not resolve datanodes.to link for {link.SourceUrl}: {ex.Message}. Trying hidden WebView2 resolver.");
            return await ResolveOnUiThreadAsync(link, cancellationToken);
        }
    }

    private static async Task<ResolvedDownload> ResolveOnUiThreadAsync(LinkCandidate link, CancellationToken cancellationToken)
    {
        var dispatcher = Application.Current.Dispatcher;
        if (dispatcher.CheckAccess())
        {
            return await ResolveWithHiddenBrowserAsync(link, cancellationToken);
        }

        var operation = dispatcher.InvokeAsync(() => ResolveWithHiddenBrowserAsync(link, cancellationToken));
        return await await operation.Task;
    }

    private static async Task<ResolvedDownload> ResolveWithHiddenBrowserAsync(LinkCandidate link, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(BrowserTimeout);

        var browser = new WebView2();
        var window = new Window
        {
            Width = 900,
            Height = 700,
            Left = -32000,
            Top = -32000,
            Opacity = 0,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            Content = browser
        };

        try
        {
            window.Show();
            await browser.EnsureCoreWebView2Async();
            browser.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = false;
            browser.CoreWebView2.Settings.AreDevToolsEnabled = false;

            var completion = new TaskCompletionSource<ResolvedDownload>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = timeout.Token.Register(() => completion.TrySetCanceled(timeout.Token));

            browser.CoreWebView2.NewWindowRequested += (_, args) =>
            {
                args.Handled = true;
            };

            browser.CoreWebView2.NavigationStarting += (_, args) =>
            {
                if (ShouldCancelKnownAdNavigation(args.Uri))
                {
                    args.Cancel = true;
                    return;
                }

                if (LooksLikeFinalDownloadUrl(args.Uri))
                {
                    args.Cancel = true;
                    completion.TrySetResult(new ResolvedDownload(args.Uri, link.FileName, null));
                }
            };

            browser.CoreWebView2.DownloadStarting += (_, args) =>
            {
                args.Cancel = true;
                var uri = args.DownloadOperation.Uri;
                if (!string.IsNullOrWhiteSpace(uri))
                {
                    completion.TrySetResult(new ResolvedDownload(uri, link.FileName, null));
                }
            };

            browser.Source = new Uri(link.SourceUrl);

            var polling = PollAndClickAsync(browser.CoreWebView2, completion, timeout.Token);
            var resolvedDownload = await completion.Task;
            await polling.ConfigureAwait(true);
            return resolvedDownload;
        }
        catch (OperationCanceledException ex)
        {
            throw new ResolveRequiresBrowserException($"Automatic WebView2 resolver timed out for {link.FileName}: {ex.Message}");
        }
        finally
        {
            window.Close();
        }
    }

    private static async Task PollAndClickAsync(CoreWebView2 webView, TaskCompletionSource<ResolvedDownload> completion, CancellationToken cancellationToken)
    {
        var clickAttempts = 0;

        while (!completion.Task.IsCompleted && !cancellationToken.IsCancellationRequested)
        {
            if (clickAttempts < 5 && await TryClickContinueButtonAsync(webView))
            {
                clickAttempts++;
                await Task.Delay(1_000, cancellationToken);
                continue;
            }

            await Task.Delay(500, cancellationToken);
        }
    }

    private static async Task<bool> TryClickContinueButtonAsync(CoreWebView2 webView)
    {
        try
        {
            var clickedJson = await webView.ExecuteScriptAsync(ClickContinueButtonScript);
            return JsonSerializer.Deserialize<bool>(clickedJson);
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Hidden WebView2 resolver failed to click datanodes continue button");
            return false;
        }
    }

    private static bool ShouldCancelKnownAdNavigation(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        var lower = url.ToLowerInvariant();
        return lower.Contains("adsterra")
            || lower.Contains("popads")
            || lower.Contains("doubleclick")
            || lower.Contains("googlesyndication")
            || lower.Contains("/ads");
    }

    private static bool LooksLikeFinalDownloadUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Host.EndsWith("datanodes.to", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return uri.AbsolutePath.Contains("/download/", StringComparison.OrdinalIgnoreCase);
    }

    private static string ClickContinueButtonScript => """
        (() => {
          const button = document.getElementById('method_free') || document.querySelector('#downloadForm button[type="submit"]');
          if (!button) {
            return false;
          }

          const reveal = document.getElementById('downloadReveal');
          if (reveal && getComputedStyle(reveal).maxHeight === '0px') {
            return false;
          }

          button.click();
          return true;
        })();
        """;
}
