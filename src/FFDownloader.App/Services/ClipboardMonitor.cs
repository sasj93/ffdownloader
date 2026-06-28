using System.Windows;
using System.Windows.Threading;
using FFDownloader.Core.Links;

namespace FFDownloader.App.Services;

public sealed class ClipboardMonitor : IDisposable
{
    private readonly DispatcherTimer _timer;
    private string? _lastText;
    private bool _isDisposed;

    public ClipboardMonitor()
    {
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(900)
        };
        _timer.Tick += (_, _) => PollClipboard();
    }

    public event EventHandler<IReadOnlyList<LinkCandidate>>? LinksDetected;

    public bool IsRunning => _timer.IsEnabled;

    public void Start() => _timer.Start();

    public void Stop() => _timer.Stop();

    public void Remember(string text) => _lastText = text;

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _timer.Stop();
        _isDisposed = true;
    }

    private void PollClipboard()
    {
        string? text;
        try
        {
            if (!Clipboard.ContainsText())
            {
                return;
            }

            text = Clipboard.GetText();
        }
        catch
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(text) || string.Equals(text, _lastText, StringComparison.Ordinal))
        {
            return;
        }

        var links = DownloadLinkParser.ParseMany(text);
        if (links.Count == 0)
        {
            _lastText = text;
            return;
        }

        _lastText = text;
        LinksDetected?.Invoke(this, links);
    }
}
