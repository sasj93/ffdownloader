using SharpCompress.Common;
using SharpCompress.Readers;

namespace FFDownloader.Core.Extraction;

public sealed class ArchiveExtractor
{
    private static readonly string[] SupportedExtensions =
    [
        ".zip",
        ".rar",
        ".7z",
        ".tar",
        ".gz",
        ".bz2",
        ".xz"
    ];

    public static bool IsSupportedArchive(string fileName)
    {
        if (fileName.EndsWith(".part001.rar", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return SupportedExtensions.Any(fileName.EndsWith);
    }

    public async Task<ExtractionResult> ExtractAsync(
        string archivePath,
        string destinationFolder,
        string? password,
        CancellationToken cancellationToken)
    {
        if (!IsSupportedArchive(Path.GetFileName(archivePath)))
        {
            throw new NotSupportedException("Archive type is not supported.");
        }

        Directory.CreateDirectory(destinationFolder);
        var writtenFiles = new List<string>();

        var options = new ReaderOptions
        {
            Password = string.IsNullOrWhiteSpace(password) ? null : password,
            LeaveStreamOpen = false
        };

        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var reader = ReaderFactory.OpenReader(archivePath, options);
            while (reader.MoveToNextEntry())
            {
                var entry = reader.Entry;
                if (entry.IsDirectory)
                {
                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();
                reader.WriteEntryToDirectory(destinationFolder, new ExtractionOptions
                {
                    ExtractFullPath = true,
                    Overwrite = true
                });

                if (!string.IsNullOrWhiteSpace(entry.Key))
                {
                    writtenFiles.Add(Path.Combine(destinationFolder, entry.Key));
                }
            }
        }, cancellationToken);

        return new ExtractionResult(destinationFolder, writtenFiles);
    }
}
