using System.Text.Json;
using System.Windows;
using FFDownloader.Core.Hosts;
using FFDownloader.Core.Links;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace FFDownloader.App.Services;

public sealed class AutomaticMediaFireResolver : IHostResolver
{
    private static readonly TimeSpan BrowserTimeout = TimeSpan.FromSeconds(25);
    private readonly MediaFireResolver _httpResolver = new();

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
            AppLogger.Info($"HTTP resolver did not resolve mediafire.com link for {link.SourceUrl}: {ex.Message}. Trying hidden WebView2 resolver.");
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

            browser.CoreWebView2.DownloadStarting += (_, args) =>
            {
                args.Cancel = true;
                var uri = args.DownloadOperation.Uri;
                if (!string.IsNullOrWhiteSpace(uri))
                {
                    completion.TrySetResult(new ResolvedDownload(uri, link.FileName, null));
                }
            };

            browser.CoreWebView2.NavigationCompleted += async (_, _) =>
            {
                if (await TryResolveCurrentDocumentAsync(browser.CoreWebView2, link) is { } resolved)
                {
                    completion.TrySetResult(resolved);
                }
            };

            browser.Source = new Uri(link.SourceUrl);

            var polling = PollDocumentAsync(browser.CoreWebView2, link, completion, timeout.Token);
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

    private static async Task PollDocumentAsync(CoreWebView2 webView, LinkCandidate link, TaskCompletionSource<ResolvedDownload> completion, CancellationToken cancellationToken)
    {
        while (!completion.Task.IsCompleted && !cancellationToken.IsCancellationRequested)
        {
            if (await TryResolveCurrentDocumentAsync(webView, link) is { } resolved)
            {
                completion.TrySetResult(resolved);
                return;
            }

            await Task.Delay(500, cancellationToken);
        }
    }

    private static async Task<ResolvedDownload?> TryResolveCurrentDocumentAsync(CoreWebView2 webView, LinkCandidate link)
    {
        try
        {
            var htmlJson = await webView.ExecuteScriptAsync("document.documentElement ? document.documentElement.outerHTML : ''");
            var html = JsonSerializer.Deserialize<string>(htmlJson) ?? string.Empty;
            return MediaFireResolver.TryResolveFromHtml(html, link);
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Hidden WebView2 resolver failed to inspect MediaFire document");
            return null;
        }
    }
}
