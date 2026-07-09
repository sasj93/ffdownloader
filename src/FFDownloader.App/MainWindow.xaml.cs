using System.Windows;
using FFDownloader.App.Services;
using FFDownloader.App.ViewModels;

namespace FFDownloader.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel(new WindowDialogService(this));
        DataContext = _viewModel;
        Closed += (_, _) =>
        {
            _viewModel.Dispose();

            // The torrent engine (and WebView2) can leave non-background threads running even
            // after being told to stop, which would keep the process alive indefinitely. All
            // state is already saved synchronously by Dispose() above, so force-exit is safe.
            Environment.Exit(0);
        };
    }
}
