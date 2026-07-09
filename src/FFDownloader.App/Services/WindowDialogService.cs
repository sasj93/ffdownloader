using System.Diagnostics;
using System.Windows;
using FFDownloader.App.Views;
using FFDownloader.Core.Downloads;
using Directory = System.IO.Directory;

namespace FFDownloader.App.Services;

public sealed class WindowDialogService
{
    private readonly Window _owner;

    public WindowDialogService(Window owner)
    {
        _owner = owner;
    }

    public string? ShowLinkInput()
    {
        var dialog = new LinkInputWindow { Owner = _owner };
        return dialog.ShowDialog() == true ? dialog.LinkText : null;
    }

    public bool ConfirmAddLinks(int linkCount)
    {
        return MessageBox.Show(
            _owner,
            $"Encontramos {linkCount} link(s) do FuckingFast no clipboard. Adicionar ao FFDOWNLOADER?",
            "Adicionar links",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes;
    }

    public void ShowError(string message)
    {
        MessageBox.Show(_owner, message, "FFDOWNLOADER", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    public void ShowInfo(string message)
    {
        MessageBox.Show(_owner, message, "FFDOWNLOADER", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public string? BrowseFolder(string initialPath)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Selecionar pasta"
        };

        if (!string.IsNullOrWhiteSpace(initialPath) && Directory.Exists(initialPath))
        {
            dialog.InitialDirectory = initialPath;
        }

        return dialog.ShowDialog(_owner) == true ? dialog.FolderName : null;
    }

    public void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }
}
