using System.Windows;
using FFDownloader.App.Services;

namespace FFDownloader.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                AppLogger.Error(exception, "Unhandled AppDomain exception");
            }
        };

        DispatcherUnhandledException += (_, args) =>
        {
            AppLogger.Error(args.Exception, "Unhandled WPF dispatcher exception");
            MessageBox.Show(
                $"O FFDOWNLOADER encontrou um erro e registrou os detalhes em:{Environment.NewLine}{AppLogger.LogPath}{Environment.NewLine}{Environment.NewLine}{args.Exception.Message}",
                "FFDOWNLOADER",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppLogger.Error(args.Exception, "Unobserved task exception");
            args.SetObserved();
        };

        AppLogger.Info("Application starting");
        base.OnStartup(e);
    }
}
