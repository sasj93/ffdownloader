using System.Windows;
using FFDownloader.Core.Hosts;
using FFDownloader.Core.Links;

namespace FFDownloader.App.Views;

public partial class InteractiveBrowserResolverWindow : Window
{
    private readonly TaskCompletionSource<ResolvedDownload> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public InteractiveBrowserResolverWindow()
    {
        InitializeComponent();
    }

    public async Task<ResolvedDownload> ResolveAsync(LinkCandidate link, CancellationToken cancellationToken)
    {
        TitleText.Text = $"Complete the download: {link.FileName}";
        using var registration = cancellationToken.Register(() => _completion.TrySetCanceled(cancellationToken));

        Closed += (_, _) => _completion.TrySetCanceled();

        await Browser.EnsureCoreWebView2Async();
        Browser.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = true;
        Browser.CoreWebView2.Settings.AreDevToolsEnabled = false;

        Browser.CoreWebView2.NewWindowRequested += (_, args) =>
        {
            if (Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri) && !string.Equals(uri.Host, new Uri(link.SourceUrl).Host, StringComparison.OrdinalIgnoreCase))
            {
                args.Handled = true;
            }
        };

        Browser.CoreWebView2.DownloadStarting += (_, args) =>
        {
            args.Cancel = true;
            var uri = args.DownloadOperation.Uri;
            if (!string.IsNullOrWhiteSpace(uri))
            {
                StatusText.Text = "Download detected, finishing up...";
                _completion.TrySetResult(new ResolvedDownload(uri, link.FileName, null));
            }
        };

        Browser.Source = new Uri(link.SourceUrl);

        Show();
        try
        {
            return await _completion.Task;
        }
        finally
        {
            try
            {
                Close();
            }
            catch (InvalidOperationException)
            {
                // Already closing/closed via the user's Cancel click or the window's X button.
            }
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _completion.TrySetCanceled();
    }
}
