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
        Closed += (_, _) => _viewModel.Dispose();
    }
}
