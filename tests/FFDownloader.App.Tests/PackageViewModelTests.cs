using FFDownloader.App.ViewModels;
using FFDownloader.Core.Downloads;
using FFDownloader.Core.Links;
using FluentAssertions;

namespace FFDownloader.App.Tests;

public sealed class PackageViewModelTests
{
    [Fact]
    public void IsExpanded_defaults_to_false_and_notifies_when_changed()
    {
        var item = new DownloadItem(new LinkCandidate(
            "https://fuckingfast.co/example#Game.part001.rar",
            "fuckingfast.co",
            "Game.part001.rar",
            "Game",
            1,
            true));
        var package = new DownloadPackageJob("Game", "D:\\downloads", [item]);
        var viewModel = new PackageViewModel(package);
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        viewModel.IsExpanded.Should().BeFalse();

        viewModel.IsExpanded = true;

        viewModel.IsExpanded.Should().BeTrue();
        changed.Should().Contain(nameof(PackageViewModel.IsExpanded));
    }
}
