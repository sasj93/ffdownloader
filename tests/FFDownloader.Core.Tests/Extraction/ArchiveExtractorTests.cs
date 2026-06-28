using FFDownloader.Core.Extraction;
using FluentAssertions;

namespace FFDownloader.Core.Tests.Extraction;

public sealed class ArchiveExtractorTests
{
    [Theory]
    [InlineData("File.zip", true)]
    [InlineData("File.rar", true)]
    [InlineData("File.part001.rar", true)]
    [InlineData("File.7z", true)]
    [InlineData("File.bin", false)]
    public void IsSupportedArchive_recognizes_common_archive_names(string fileName, bool expected)
    {
        ArchiveExtractor.IsSupportedArchive(fileName).Should().Be(expected);
    }
}
