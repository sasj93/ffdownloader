using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FFDownloader.Core.Downloads;
using FFDownloader.Core.Hosts;
using FFDownloader.Core.Links;
using FFDownloader.Core.Settings;
using FluentAssertions;

namespace FFDownloader.Core.Tests.Downloads;

public sealed class HttpDownloadServiceTests
{
    [Fact]
    public async Task DownloadAsync_writes_response_to_destination_and_reports_completion()
    {
        using var temp = new TempDirectory();
        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes)
        });
        var service = new HttpDownloadService(new HttpClient(handler));
        var link = new LinkCandidate("https://fuckingfast.co/file#File.bin", "fuckingfast.co", "File.bin", "File.bin", null, false);
        var item = new DownloadItem(link);
        var settings = DownloadSettings.CreateDefault();
        settings.DestinationFolder = temp.Path;

        var result = await service.DownloadAsync(
            item,
            new ResolvedDownload("https://cdn.example.test/File.bin", "File.bin", bytes.Length),
            temp.Path,
            settings,
            null,
            CancellationToken.None);

        File.ReadAllBytes(result.LocalPath).Should().Equal(bytes);
        item.Status.Should().Be(DownloadStatus.Completed);
        item.DownloadedBytes.Should().Be(bytes.Length);
    }

    [Fact]
    public async Task DownloadAsync_resumes_existing_partial_file_with_range_request()
    {
        using var temp = new TempDirectory();
        var localPath = System.IO.Path.Combine(temp.Path, "File.bin.ffdownload");
        await File.WriteAllBytesAsync(localPath, [1, 2, 3]);
        var handler = new StubHttpMessageHandler(request =>
        {
            request.Headers.Range.Should().NotBeNull();
            request.Headers.Range!.Ranges.Should().ContainSingle(range => range.From == 3 && range.To == null);
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent([4, 5])
            };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(3, 4, 5);
            return response;
        });
        var service = new HttpDownloadService(new HttpClient(handler));
        var link = new LinkCandidate("https://fuckingfast.co/file#File.bin", "fuckingfast.co", "File.bin", "File.bin", null, false);
        var item = new DownloadItem(link);
        var settings = DownloadSettings.CreateDefault();
        settings.DestinationFolder = temp.Path;

        var result = await service.DownloadAsync(
            item,
            new ResolvedDownload("https://cdn.example.test/File.bin", "File.bin", 5),
            temp.Path,
            settings,
            null,
            CancellationToken.None);

        File.ReadAllBytes(result.LocalPath).Should().Equal(1, 2, 3, 4, 5);
        File.Exists(System.IO.Path.Combine(temp.Path, "File.bin.ffdownload")).Should().BeFalse();
        item.DownloadedBytes.Should().Be(5);
        item.Status.Should().Be(DownloadStatus.Completed);
    }

    [Fact]
    public async Task DownloadAsync_restarts_partial_file_when_server_ignores_range_request()
    {
        using var temp = new TempDirectory();
        var localPath = System.IO.Path.Combine(temp.Path, "File.bin.ffdownload");
        await File.WriteAllBytesAsync(localPath, [9, 9, 9]);
        var handler = new StubHttpMessageHandler(request =>
        {
            request.Headers.Range.Should().NotBeNull();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([1, 2, 3, 4, 5])
            };
        });
        var service = new HttpDownloadService(new HttpClient(handler));
        var link = new LinkCandidate("https://fuckingfast.co/file#File.bin", "fuckingfast.co", "File.bin", "File.bin", null, false);
        var item = new DownloadItem(link);
        var settings = DownloadSettings.CreateDefault();
        settings.DestinationFolder = temp.Path;

        var result = await service.DownloadAsync(
            item,
            new ResolvedDownload("https://cdn.example.test/File.bin", "File.bin", 5),
            temp.Path,
            settings,
            null,
            CancellationToken.None);

        File.ReadAllBytes(result.LocalPath).Should().Equal(1, 2, 3, 4, 5);
        item.DownloadedBytes.Should().Be(5);
    }

    [Fact]
    public async Task DownloadAsync_uses_parallel_range_segments_when_server_supports_ranges()
    {
        using var temp = new TempDirectory();
        var bytes = Enumerable.Range(0, 12).Select(value => (byte)value).ToArray();
        var observedRanges = new List<(long From, long? To)>();
        var handler = new StubHttpMessageHandler(request =>
        {
            request.Headers.Range.Should().NotBeNull();
            var range = request.Headers.Range!.Ranges.Single();
            var from = range.From!.Value;
            var to = range.To ?? bytes.Length - 1;
            lock (observedRanges)
            {
                observedRanges.Add((from, range.To));
            }

            var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent(bytes[(int)from..((int)to + 1)])
            };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, to, bytes.Length);
            response.Content.Headers.LastModified = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            response.Headers.ETag = new EntityTagHeaderValue("\"abc\"");
            return response;
        });
        var service = new HttpDownloadService(new HttpClient(handler));
        var link = new LinkCandidate("https://fuckingfast.co/file#File.bin", "fuckingfast.co", "File.bin", "File.bin", null, false);
        var item = new DownloadItem(link);
        var settings = DownloadSettings.CreateDefault();
        settings.DestinationFolder = temp.Path;
        settings.ConnectionsPerFile = 3;
        settings.EnableMultiConnectionDownloads = true;
        settings.EnableAdaptiveConnectionCount = false;

        var result = await service.DownloadAsync(
            item,
            new ResolvedDownload("https://cdn.example.test/File.bin", "File.bin", bytes.Length),
            temp.Path,
            settings,
            null,
            CancellationToken.None);

        File.ReadAllBytes(result.LocalPath).Should().Equal(bytes);
        observedRanges.OrderBy(range => range.From).Should().Equal([(0, 3), (4, 7), (8, null)]);
        File.Exists(System.IO.Path.Combine(temp.Path, "File.bin.ffdownload.state")).Should().BeFalse();
    }

    [Fact]
    public async Task DownloadAsync_completes_when_advertised_size_overshoots_the_real_server_size()
    {
        using var temp = new TempDirectory();
        var bytes = Enumerable.Range(0, 12).Select(value => (byte)value).ToArray();
        var handler = new StubHttpMessageHandler(request =>
        {
            var range = request.Headers.Range!.Ranges.Single();
            var from = (int)range.From!.Value;
            var to = range.To.HasValue ? Math.Min((int)range.To.Value, bytes.Length - 1) : bytes.Length - 1;
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent(bytes[from..(to + 1)])
            };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, to, bytes.Length);
            return response;
        });
        var service = new HttpDownloadService(new HttpClient(handler));
        var link = new LinkCandidate("https://fuckingfast.co/file#File.bin", "fuckingfast.co", "File.bin", "File.bin", null, false);
        var item = new DownloadItem(link);
        var settings = DownloadSettings.CreateDefault();
        settings.DestinationFolder = temp.Path;
        settings.ConnectionsPerFile = 3;
        settings.EnableMultiConnectionDownloads = true;
        settings.EnableAdaptiveConnectionCount = false;

        var result = await service.DownloadAsync(
            item,
            new ResolvedDownload("https://cdn.example.test/File.bin", "File.bin", bytes.Length + 3),
            temp.Path,
            settings,
            null,
            CancellationToken.None);

        File.ReadAllBytes(result.LocalPath).Should().Equal(bytes);
        item.Status.Should().Be(DownloadStatus.Completed);
        item.SizeBytes.Should().Be(bytes.Length);
    }

    [Fact]
    public async Task DownloadAsync_downloads_full_file_when_advertised_size_undershoots_the_real_server_size()
    {
        using var temp = new TempDirectory();
        var bytes = Enumerable.Range(0, 14).Select(value => (byte)value).ToArray();
        var handler = new StubHttpMessageHandler(request =>
        {
            var range = request.Headers.Range!.Ranges.Single();
            var from = (int)range.From!.Value;
            var to = range.To.HasValue ? Math.Min((int)range.To.Value, bytes.Length - 1) : bytes.Length - 1;
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent(bytes[from..(to + 1)])
            };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, to, bytes.Length);
            return response;
        });
        var service = new HttpDownloadService(new HttpClient(handler));
        var link = new LinkCandidate("https://fuckingfast.co/file#File.bin", "fuckingfast.co", "File.bin", "File.bin", null, false);
        var item = new DownloadItem(link);
        var settings = DownloadSettings.CreateDefault();
        settings.DestinationFolder = temp.Path;
        settings.ConnectionsPerFile = 3;
        settings.EnableMultiConnectionDownloads = true;
        settings.EnableAdaptiveConnectionCount = false;

        var result = await service.DownloadAsync(
            item,
            new ResolvedDownload("https://cdn.example.test/File.bin", "File.bin", 12),
            temp.Path,
            settings,
            null,
            CancellationToken.None);

        File.ReadAllBytes(result.LocalPath).Should().Equal(bytes);
        item.Status.Should().Be(DownloadStatus.Completed);
        item.SizeBytes.Should().Be(bytes.Length);
    }

    [Fact]
    public async Task DownloadAsync_falls_back_to_single_stream_when_parallel_range_is_not_supported()
    {
        using var temp = new TempDirectory();
        var bytes = Enumerable.Range(0, 12).Select(value => (byte)value).ToArray();
        var calls = 0;
        var handler = new StubHttpMessageHandler(request =>
        {
            calls++;
            if (request.Headers.Range is not null)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(bytes)
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bytes)
            };
        });
        var service = new HttpDownloadService(new HttpClient(handler));
        var link = new LinkCandidate("https://fuckingfast.co/file#File.bin", "fuckingfast.co", "File.bin", "File.bin", null, false);
        var item = new DownloadItem(link);
        var settings = DownloadSettings.CreateDefault();
        settings.DestinationFolder = temp.Path;
        settings.ConnectionsPerFile = 4;
        settings.EnableMultiConnectionDownloads = true;
        settings.EnableAdaptiveConnectionCount = false;

        var result = await service.DownloadAsync(
            item,
            new ResolvedDownload("https://cdn.example.test/File.bin", "File.bin", bytes.Length),
            temp.Path,
            settings,
            null,
            CancellationToken.None);

        File.ReadAllBytes(result.LocalPath).Should().Equal(bytes);
        calls.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task DownloadAsync_writes_resume_state_while_parallel_segments_are_incomplete()
    {
        using var temp = new TempDirectory();
        var bytes = Enumerable.Range(0, 12).Select(value => (byte)value).ToArray();
        var handler = new StubHttpMessageHandler(request =>
        {
            var range = request.Headers.Range!.Ranges.Single();
            if (range.From == 4)
            {
                throw new IOException("connection lost");
            }

            var from = range.From!.Value;
            var to = range.To ?? bytes.Length - 1;
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent(bytes[(int)from..((int)to + 1)])
            };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, to, bytes.Length);
            return response;
        });
        var service = new HttpDownloadService(new HttpClient(handler));
        var link = new LinkCandidate("https://fuckingfast.co/file#File.bin", "fuckingfast.co", "File.bin", "File.bin", null, false);
        var item = new DownloadItem(link);
        var settings = DownloadSettings.CreateDefault();
        settings.DestinationFolder = temp.Path;
        settings.ConnectionsPerFile = 3;
        settings.EnableMultiConnectionDownloads = true;
        settings.EnableAdaptiveConnectionCount = false;

        var act = () => service.DownloadAsync(
            item,
            new ResolvedDownload("https://cdn.example.test/File.bin", "File.bin", bytes.Length),
            temp.Path,
            settings,
            null,
            CancellationToken.None);

        await act.Should().ThrowAsync<IOException>();
        var statePath = System.IO.Path.Combine(temp.Path, "File.bin.ffdownload.state");
        File.Exists(statePath).Should().BeTrue();
        var json = await File.ReadAllTextAsync(statePath);
        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("TotalBytes").GetInt64().Should().Be(bytes.Length);
        document.RootElement.GetProperty("Segments").GetArrayLength().Should().Be(3);
    }

    [Fact]
    public async Task DownloadAsync_rejects_resume_when_remote_identity_changed()
    {
        using var temp = new TempDirectory();
        var partialPath = System.IO.Path.Combine(temp.Path, "File.bin.ffdownload");
        await File.WriteAllBytesAsync(partialPath, [1, 2, 3]);
        await File.WriteAllTextAsync(
            $"{partialPath}.state",
            """
            {
              "DownloadUrl": "https://cdn.example.test/File.bin",
              "TotalBytes": 5,
              "ETag": "\"old\"",
              "LastModified": null,
              "Segments": []
            }
            """);
        var handler = new StubHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent([4, 5])
            };
            response.Headers.ETag = new EntityTagHeaderValue("\"new\"");
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(3, 4, 5);
            return response;
        });
        var service = new HttpDownloadService(new HttpClient(handler));
        var link = new LinkCandidate("https://fuckingfast.co/file#File.bin", "fuckingfast.co", "File.bin", "File.bin", null, false);
        var item = new DownloadItem(link);
        var settings = DownloadSettings.CreateDefault();
        settings.DestinationFolder = temp.Path;
        settings.ValidateRemoteIdentity = true;

        var act = () => service.DownloadAsync(
            item,
            new ResolvedDownload("https://cdn.example.test/File.bin", "File.bin", 5),
            temp.Path,
            settings,
            null,
            CancellationToken.None);

        await act.Should().ThrowAsync<IOException>();
        File.Exists(partialPath).Should().BeFalse();
        File.Exists($"{partialPath}.state").Should().BeFalse();
    }

    [Fact]
    public async Task DownloadAsync_skips_network_when_existing_file_already_matches_known_size()
    {
        using var temp = new TempDirectory();
        var localPath = System.IO.Path.Combine(temp.Path, "File.bin");
        await File.WriteAllBytesAsync(localPath, [1, 2, 3, 4, 5]);
        var handler = new StubHttpMessageHandler(_ => throw new InvalidOperationException("Network should not be used."));
        var service = new HttpDownloadService(new HttpClient(handler));
        var link = new LinkCandidate("https://fuckingfast.co/file#File.bin", "fuckingfast.co", "File.bin", "File.bin", null, false);
        var item = new DownloadItem(link);
        var settings = DownloadSettings.CreateDefault();
        settings.DestinationFolder = temp.Path;

        var result = await service.DownloadAsync(
            item,
            new ResolvedDownload("https://cdn.example.test/File.bin", "File.bin", 5),
            temp.Path,
            settings,
            null,
            CancellationToken.None);

        result.BytesWritten.Should().Be(5);
        item.Status.Should().Be(DownloadStatus.Completed);
        item.DownloadedBytes.Should().Be(5);
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(responseFactory(request));
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ffdownloader-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, true);
            }
        }
    }
}
