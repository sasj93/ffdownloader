using System.Windows;
using FFDownloader.Core.Links;

namespace FFDownloader.App.Views;

public partial class LinkInputWindow : Window
{
    public LinkInputWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => LinksBox.Focus();
        LinksBox.TextChanged += (_, _) => UpdateDetectedCount();
    }

    public string LinkText { get; private set; } = string.Empty;

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        LinkText = LinksBox.Text;
        DialogResult = true;
    }

    private void PasteClipboard_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Clipboard.ContainsText())
            {
                LinksBox.Text = Clipboard.GetText();
                LinksBox.CaretIndex = LinksBox.Text.Length;
                LinksBox.Focus();
            }
        }
        catch
        {
            MessageBox.Show(this, "Nao foi possivel ler o clipboard.", "Adicionar links", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void UpdateDetectedCount()
    {
        var links = DownloadLinkParser.ParseMany(LinksBox.Text);
        DetectedCountText.Text = $"{links.Count} link(s) detectado(s)";
    }
}
