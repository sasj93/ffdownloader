using System.Text.Json;
using System.Windows;
using FFDownloader.Core.Hosts;
using FFDownloader.Core.Links;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace FFDownloader.App.Services;

public sealed class AutomaticFuckingFastResolver : IHostResolver
{
    private static readonly TimeSpan BrowserTimeout = TimeSpan.FromSeconds(25);
    private const string DownloadButtonXPath = "/html/body/div[2]/div/div[1]/button | /html/body/div[2]/div/div[1]/a";
    private readonly FuckingFastResolver _httpResolver = new();

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
            AppLogger.Info($"HTTP resolver did not expose /dl/ for {link.SourceUrl}: {ex.Message}. Trying hidden WebView2 resolver.");
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
            await browser.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(WindowOpenHookScript);

            var completion = new TaskCompletionSource<ResolvedDownload>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = timeout.Token.Register(() => completion.TrySetCanceled(timeout.Token));

            browser.CoreWebView2.WebMessageReceived += (_, args) =>
            {
                if (TryResolveObservedUrl(args.TryGetWebMessageAsString(), link) is { } resolved)
                {
                    completion.TrySetResult(resolved);
                }
            };

            browser.CoreWebView2.NewWindowRequested += (_, args) =>
            {
                args.Handled = true;
                if (TryResolveObservedUrl(args.Uri, link) is { } resolved)
                {
                    completion.TrySetResult(resolved);
                }
            };

            browser.CoreWebView2.NavigationStarting += (_, args) =>
            {
                if (TryResolveObservedUrl(args.Uri, link) is { } resolved)
                {
                    args.Cancel = true;
                    completion.TrySetResult(resolved);
                    return;
                }

                if (ShouldCancelExternalNavigation(args.Uri))
                {
                    args.Cancel = true;
                }
            };

            browser.CoreWebView2.DownloadStarting += (_, args) =>
            {
                args.Cancel = true;
                if (TryResolveObservedUrl(args.DownloadOperation.Uri, link) is { } resolved)
                {
                    completion.TrySetResult(resolved);
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
        var htmxAttempts = 0;
        var clickAttempts = 0;

        while (!completion.Task.IsCompleted && !cancellationToken.IsCancellationRequested)
        {
            if (await TryResolveCurrentDocumentAsync(webView, link) is { } resolved)
            {
                completion.TrySetResult(resolved);
                return;
            }

            if (htmxAttempts < 3 && await TryPostHtmxDownloadAsync(webView))
            {
                htmxAttempts++;
                await Task.Delay(1_000, cancellationToken);
                continue;
            }

            if (clickAttempts < 3 && await TryClickDownloadButtonAsync(webView))
            {
                clickAttempts++;
                await Task.Delay(1_000, cancellationToken);
                continue;
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
            if (FuckingFastResolver.TryResolveFromHtml(html, link) is { } resolvedFromHtml)
            {
                return resolvedFromHtml;
            }

            var observedUrlJson = await webView.ExecuteScriptAsync(ExtractDownloadUrlScript);
            var observedUrl = JsonSerializer.Deserialize<string?>(observedUrlJson);
            return TryResolveObservedUrl(observedUrl, link);
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Hidden WebView2 resolver failed to inspect document");
            return null;
        }
    }

    private static async Task<bool> TryClickDownloadButtonAsync(CoreWebView2 webView)
    {
        try
        {
            var clickedJson = await webView.ExecuteScriptAsync(ClickDownloadButtonScript);
            return JsonSerializer.Deserialize<bool>(clickedJson);
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Hidden WebView2 resolver failed to click download button");
            return false;
        }
    }

    private static async Task<bool> TryPostHtmxDownloadAsync(CoreWebView2 webView)
    {
        try
        {
            var postedJson = await webView.ExecuteScriptAsync(HtmxPostDownloadScript);
            return JsonSerializer.Deserialize<bool>(postedJson);
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Hidden WebView2 resolver failed to POST htmx download endpoint");
            return false;
        }
    }

    private static ResolvedDownload? TryResolveObservedUrl(string? url, LinkCandidate link)
    {
        return FuckingFastResolver.TryResolveObservedDownloadUrl(url, link);
    }

    private static bool ShouldCancelExternalNavigation(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.Scheme.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            && !uri.Host.EndsWith("fuckingfast.co", StringComparison.OrdinalIgnoreCase);
    }

    private static string WindowOpenHookScript => """
        (() => {
          if (window.__ffdownloaderWindowOpenHooked) {
            return;
          }

          window.__ffdownloaderWindowOpenHooked = true;
          const originalOpen = window.open;
          window.open = function(url) {
            try {
              window.chrome?.webview?.postMessage(String(url || ''));
            } catch {
            }

            return null;
          };
        })();
        """;

    private static string ExtractDownloadUrlScript => """
        (() => {
          const values = [];
          const push = (value) => {
            if (typeof value === 'string' && value.length > 0) {
              values.push(value);
            }
          };

          const xpathNode = document.evaluate('__DOWNLOAD_BUTTON_XPATH__', document, null, XPathResult.FIRST_ORDERED_NODE_TYPE, null).singleNodeValue;
          const nodes = [
            xpathNode,
            ...document.querySelectorAll('button.link-button, .link-button, button[onclick], a[onclick], script')
          ].filter(Boolean);

          for (const node of nodes) {
            if (node.getAttribute) {
              push(node.getAttribute('onclick'));
              push(node.getAttribute('href'));
            }

            push(node.textContent);
          }

          const text = values.join('\n');
          const openMatch = text.match(/window\s*\.\s*open\s*\(\s*['"]([^'"]+)['"]/i);
          if (openMatch) {
            return openMatch[1];
          }

          const dlMatch = text.match(/(?:https?:)?\/\/[^'"<>\s]+\/dl\/[^'"<>\s]+|\/dl\/[^'"<>\s]+/i);
          return dlMatch ? dlMatch[0] : null;
        })();
        """.Replace("__DOWNLOAD_BUTTON_XPATH__", DownloadButtonXPath);

    private static string HtmxPostDownloadScript => """
        (() => {
          if (window.__ffdownloaderHtmxPosted) {
            return false;
          }

          const trigger = document.querySelector('a.link-button[hx-post], button.link-button[hx-post], [hx-post]');
          if (!trigger) {
            return false;
          }

          window.__ffdownloaderHtmxPosted = true;
          fetch(trigger.getAttribute('hx-post'), { method: 'POST', headers: { 'HX-Request': 'true' } })
            .then((response) => {
              const url = response.headers.get('HX-Redirect') || response.headers.get('Location');
              if (url) {
                window.chrome?.webview?.postMessage(String(url));
              } else {
                window.__ffdownloaderHtmxPosted = false;
              }
            })
            .catch(() => {
              window.__ffdownloaderHtmxPosted = false;
            });

          return true;
        })();
        """;

    private static string ClickDownloadButtonScript => """
        (() => {
          const xpathNode = document.evaluate('__DOWNLOAD_BUTTON_XPATH__', document, null, XPathResult.FIRST_ORDERED_NODE_TYPE, null).singleNodeValue;
          const button =
            xpathNode ||
            document.querySelector('button.link-button.text-5xl.gay-button, a.link-button.text-5xl.gay-button') ||
            document.querySelector('button.link-button, a.link-button, [hx-post]') ||
            Array.from(document.querySelectorAll('button, a')).find((node) => /^\s*download\s*$/i.test(node.textContent || ''));

          if (!button) {
            return false;
          }

          button.scrollIntoView({ block: 'center', inline: 'center' });
          button.click();
          return true;
        })();
        """.Replace("__DOWNLOAD_BUTTON_XPATH__", DownloadButtonXPath);
}
