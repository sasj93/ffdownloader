using System.Text.Json;
using System.Windows;
using FFDownloader.App.Views;
using FFDownloader.Core.Hosts;
using FFDownloader.Core.Links;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace FFDownloader.App.Services;

/// <summary>
/// Covers the multiup.io mirror hosts that don't have a dedicated resolver (see
/// <see cref="DownloadLinkParser.GenericMirrorHosts"/>). Each of these hosts is a different platform
/// with its own reveal/countdown/challenge quirks, so instead of one parser per host this drives a
/// real WebView2: hidden first (fast, no user interaction) for the common "click a download button"
/// case, escalating to a visible, user-facing window when a captcha is detected or the hidden attempt
/// times out — mirroring how JDownloader prompts the user to solve a captcha inline.
/// </summary>
public sealed class GenericBrowserMirrorResolver : IHostResolver
{
    private static readonly TimeSpan HiddenAttemptTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan InteractiveTimeout = TimeSpan.FromMinutes(4);

    public string Host => "generic-mirror";

    public bool CanResolve(LinkCandidate link)
    {
        return DownloadLinkParser.GenericMirrorHosts.Contains(link.Host, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<ResolvedDownload> ResolveAsync(LinkCandidate link, CancellationToken cancellationToken)
    {
        try
        {
            return await RunOnUiThreadAsync(() => ResolveHiddenOnUiThreadAsync(link, cancellationToken));
        }
        catch (ResolveRequiresBrowserException ex)
        {
            AppLogger.Info($"Hidden resolver could not complete {link.SourceUrl}: {ex.Message}. Opening interactive browser window.");
            return await RunOnUiThreadAsync(() => ResolveInteractiveOnUiThreadAsync(link, cancellationToken));
        }
    }

    private static async Task<ResolvedDownload> RunOnUiThreadAsync(Func<Task<ResolvedDownload>> action)
    {
        var dispatcher = Application.Current.Dispatcher;
        if (dispatcher.CheckAccess())
        {
            return await action();
        }

        var operation = dispatcher.InvokeAsync(action);
        return await await operation.Task;
    }

    private static async Task<ResolvedDownload> ResolveInteractiveOnUiThreadAsync(LinkCandidate link, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(InteractiveTimeout);

        var window = new InteractiveBrowserResolverWindow();
        try
        {
            return await window.ResolveAsync(link, timeout.Token);
        }
        catch (OperationCanceledException ex)
        {
            throw new ResolveRequiresBrowserException($"Interactive resolver was cancelled or timed out for {link.FileName}: {ex.Message}");
        }
    }

    private static async Task<ResolvedDownload> ResolveHiddenOnUiThreadAsync(LinkCandidate link, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(HiddenAttemptTimeout);

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
                if (!completion.Task.IsCompleted && await HasCaptchaChallengeAsync(browser.CoreWebView2))
                {
                    completion.TrySetException(new ResolveRequiresBrowserException("Captcha challenge detected; needs interactive solving."));
                }
            };

            browser.Source = new Uri(link.SourceUrl);

            var polling = PollAndClickAsync(browser.CoreWebView2, completion, timeout.Token);
            var resolved = await completion.Task;
            await polling.ConfigureAwait(true);
            return resolved;
        }
        catch (OperationCanceledException ex)
        {
            throw new ResolveRequiresBrowserException($"Hidden resolver timed out for {link.FileName}: {ex.Message}");
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
            if (clickAttempts < 6 && await TryClickDownloadElementAsync(webView))
            {
                clickAttempts++;
                await Task.Delay(1_500, cancellationToken);
                continue;
            }

            await Task.Delay(500, cancellationToken);
        }
    }

    private static async Task<bool> TryClickDownloadElementAsync(CoreWebView2 webView)
    {
        try
        {
            var clickedJson = await webView.ExecuteScriptAsync(ClickDownloadElementScript);
            return JsonSerializer.Deserialize<bool>(clickedJson);
        }
        catch (Exception ex)
        {
            AppLogger.Error(ex, "Generic mirror resolver failed to click a download element");
            return false;
        }
    }

    private static async Task<bool> HasCaptchaChallengeAsync(CoreWebView2 webView)
    {
        try
        {
            var resultJson = await webView.ExecuteScriptAsync(CaptchaDetectionScript);
            return JsonSerializer.Deserialize<bool>(resultJson);
        }
        catch
        {
            return false;
        }
    }

    private static string CaptchaDetectionScript => """
        (() => {
          return !!document.querySelector('.cf-turnstile, .g-recaptcha, .h-captcha, iframe[src*="recaptcha"], iframe[src*="hcaptcha"], iframe[src*="turnstile"], iframe[src*="captcha"]');
        })();
        """;

    private static string ClickDownloadElementScript => """
        (() => {
          const candidates = Array.from(document.querySelectorAll('a, button'));
          const element = candidates.find((node) => {
            if (node.dataset.ffdownloaderClicked) {
              return false;
            }

            const text = (node.textContent || '').trim();
            if (!/download/i.test(text)) {
              return false;
            }

            const href = (node.getAttribute('href') || '').toLowerCase();
            if (/premium|register|login|pricing|signup|adsterra|popads/i.test(href) || /premium|register|login|pricing|signup/i.test(text)) {
              return false;
            }

            return true;
          });

          if (!element) {
            return false;
          }

          element.dataset.ffdownloaderClicked = '1';
          element.scrollIntoView({ block: 'center', inline: 'center' });
          element.click();
          return true;
        })();
        """;
}
