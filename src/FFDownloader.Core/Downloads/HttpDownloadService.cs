using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FFDownloader.Core.Hosts;
using FFDownloader.Core.Settings;

namespace FFDownloader.Core.Downloads;

public sealed class HttpDownloadService
{
    private const int BufferSize = 128 * 1024;
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private readonly object _stateLock = new();

    public HttpDownloadService()
        : this(new HttpClient())
    {
    }

    public HttpDownloadService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<DownloadResult> DownloadAsync(
        DownloadItem item,
        ResolvedDownload resolved,
        string destinationFolder,
        DownloadSettings settings,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        settings.Validate();
        Directory.CreateDirectory(destinationFolder);

        var paths = DownloadPaths.Create(destinationFolder, resolved.FileName, settings.UseTemporaryDownloadFiles);
        item.Status = DownloadStatus.Downloading;
        item.ResolvedUrl = resolved.DownloadUrl;
        item.LocalPath = paths.FinalPath;
        item.SizeBytes = resolved.SizeBytes;
        item.ErrorMessage = null;
        progress?.Report(CreateProgress(item));

        try
        {
            if (TryCompleteFromExistingFinalFile(item, resolved, paths, progress, out var completed))
            {
                return completed;
            }

            MigrateLegacyPartialFile(paths, resolved.SizeBytes);

            var effectiveConnections = GetEffectiveConnections(item.Host, resolved.SizeBytes, settings);
            if (effectiveConnections > 1 && resolved.SizeBytes is > 0)
            {
                return await DownloadMultiSegmentAsync(item, resolved, paths, settings, effectiveConnections, progress, cancellationToken);
            }

            return await DownloadSingleStreamAsync(item, resolved, paths, settings, progress, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            item.Status = DownloadStatus.Paused;
            item.SpeedBytesPerSecond = 0;
            item.DownloadedBytes = GetPartialBytes(paths);
            progress?.Report(CreateProgress(item));
            throw;
        }
        catch (Exception ex)
        {
            item.Status = DownloadStatus.Failed;
            item.SpeedBytesPerSecond = 0;
            item.DownloadedBytes = GetPartialBytes(paths);
            item.ErrorMessage = ex.Message;
            progress?.Report(CreateProgress(item));
            throw;
        }
    }

    private async Task<DownloadResult> DownloadSingleStreamAsync(
        DownloadItem item,
        ResolvedDownload resolved,
        DownloadPaths paths,
        DownloadSettings settings,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var existingBytes = GetExistingBytes(paths.TempPath);
        if (resolved.SizeBytes is > 0 && existingBytes > resolved.SizeBytes.Value)
        {
            DeletePartial(paths);
            existingBytes = 0;
        }

        HttpResponseMessage? response = null;
        try
        {
            response = await SendDownloadRequestAsync(resolved.DownloadUrl, existingBytes, null, cancellationToken);
            if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable && existingBytes > 0)
            {
                response.Dispose();
                if (resolved.SizeBytes is > 0 && GetExistingBytes(paths.TempPath) == resolved.SizeBytes.Value)
                {
                    return CompleteDownload(item, paths, resolved.SizeBytes.Value, progress);
                }

                DeletePartial(paths);
                existingBytes = 0;
                response = await SendDownloadRequestAsync(resolved.DownloadUrl, null, null, cancellationToken);
            }

            var resumeAccepted = existingBytes > 0 && response.StatusCode == HttpStatusCode.PartialContent;
            if (existingBytes > 0 && !resumeAccepted)
            {
                existingBytes = 0;
            }

            response.EnsureSuccessStatusCode();
            item.SizeBytes = ResolveTotalSize(resolved.SizeBytes, response, existingBytes, resumeAccepted);
            var state = (LoadResumeState(paths) ?? CreateSingleSegmentState(resolved, item.SizeBytes, response)) with
            {
                DownloadUrl = resolved.DownloadUrl,
                TotalBytes = item.SizeBytes
            };
            ValidateRemoteIdentity(paths, state, response, settings);
            UpdateStateIdentity(state, response);
            SaveResumeState(paths, state);

            var total = resumeAccepted ? existingBytes : 0L;
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = new FileStream(
                paths.TempPath,
                resumeAccepted ? FileMode.Append : FileMode.Create,
                FileAccess.Write,
                FileShare.Read,
                BufferSize,
                true))
            {
                var buffer = new byte[BufferSize];
                var limiter = new DownloadSpeedLimiter(() => settings.SpeedLimitBytesPerSecond);
                var speedWindow = new SpeedWindow();
                item.DownloadedBytes = total;
                progress?.Report(CreateProgress(item));

                while (true)
                {
                    var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                    if (read == 0)
                    {
                        break;
                    }

                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    total += read;
                    item.DownloadedBytes = total;
                    UpdateSpeed(item, speedWindow, read, progress);
                    await limiter.ThrottleAsync(read, cancellationToken);
                }
            }

            ValidateFinalSize(item.SizeBytes, total);
            return CompleteDownload(item, paths, total, progress);
        }
        finally
        {
            response?.Dispose();
        }
    }

    private async Task<DownloadResult> DownloadMultiSegmentAsync(
        DownloadItem item,
        ResolvedDownload resolved,
        DownloadPaths paths,
        DownloadSettings settings,
        int connectionCount,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var totalBytes = resolved.SizeBytes!.Value;
        var loadedState = LoadResumeState(paths);
        var segments = IsCompatibleSegmentState(loadedState, totalBytes)
            ? loadedState!.Segments
            : CreateSegments(totalBytes, connectionCount, paths.TempPath);
        var state = IsCompatibleSegmentState(loadedState, totalBytes)
            ? loadedState! with { DownloadUrl = resolved.DownloadUrl, TotalBytes = totalBytes }
            : CreateSegmentedState(resolved, totalBytes, segments);
        SaveResumeState(paths, state);

        var limiter = new DownloadSpeedLimiter(() => settings.SpeedLimitBytesPerSecond);
        var speedWindow = new SpeedWindow();
        var lastSegmentIndex = segments.Max(segment => segment.Index);

        var firstResult = await DownloadSegmentAsync(item, paths, segments[0], resolved.DownloadUrl, state, settings, limiter, speedWindow, segments[0].Index == lastSegmentIndex, progress, cancellationToken);
        if (firstResult == SegmentDownloadResult.RangeNotSupported)
        {
            DeletePartial(paths);
            return await DownloadSingleStreamAsync(item, resolved, paths, settings, progress, cancellationToken);
        }

        var remainingTasks = segments
            .Skip(1)
            .Select(segment => DownloadSegmentAsync(item, paths, segment, resolved.DownloadUrl, state, settings, limiter, speedWindow, segment.Index == lastSegmentIndex, progress, cancellationToken))
            .ToArray();

        var results = await Task.WhenAll(remainingTasks);
        if (results.Any(result => result == SegmentDownloadResult.RangeNotSupported))
        {
            DeletePartial(paths);
            return await DownloadSingleStreamAsync(item, resolved, paths, settings, progress, cancellationToken);
        }

        var confirmedTotal = state.ConfirmedTotalBytes ?? totalBytes;
        await MergeSegmentsAsync(paths.TempPath, segments, cancellationToken);
        ValidateFinalSize(confirmedTotal, GetExistingBytes(paths.TempPath));
        return CompleteDownload(item, paths, confirmedTotal, progress);
    }

    private async Task<SegmentDownloadResult> DownloadSegmentAsync(
        DownloadItem item,
        DownloadPaths paths,
        SegmentState segment,
        string downloadUrl,
        ResumeState state,
        DownloadSettings settings,
        DownloadSpeedLimiter limiter,
        SpeedWindow speedWindow,
        bool isLastSegment,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var existingBytes = GetExistingBytes(segment.Path);

        // Last segment's real length is unknown until the server confirms it, so its estimated
        // End is an open-ended request rather than a hard boundary.
        var confirmedSegmentLength = isLastSegment
            ? state.ConfirmedTotalBytes - segment.Start
            : segment.End - segment.Start + 1;

        if (confirmedSegmentLength.HasValue)
        {
            if (existingBytes >= confirmedSegmentLength.Value)
            {
                RefreshSegmentProgress(item, state, progress);
                return SegmentDownloadResult.Completed;
            }

            if (existingBytes < 0 || existingBytes > confirmedSegmentLength.Value)
            {
                File.Delete(segment.Path);
                existingBytes = 0;
            }
        }
        else if (existingBytes < 0)
        {
            File.Delete(segment.Path);
            existingBytes = 0;
        }

        var from = segment.Start + existingBytes;
        var to = isLastSegment ? null : (long?)segment.End;
        using var response = await SendDownloadRequestAsync(downloadUrl, from, to, cancellationToken);
        if (response.StatusCode == HttpStatusCode.OK)
        {
            return SegmentDownloadResult.RangeNotSupported;
        }

        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable && isLastSegment)
        {
            UpdateConfirmedTotalBytes(state, response);
            RefreshSegmentProgress(item, state, progress);
            return SegmentDownloadResult.Completed;
        }

        response.EnsureSuccessStatusCode();
        if (response.StatusCode != HttpStatusCode.PartialContent)
        {
            return SegmentDownloadResult.RangeNotSupported;
        }

        ValidateRemoteIdentity(paths, state, response, settings);
        UpdateStateIdentity(state, response);
        UpdateConfirmedTotalBytes(state, response);
        SaveResumeState(paths, state);

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(segment.Path, FileMode.Append, FileAccess.Write, FileShare.Read, BufferSize, true);
        var buffer = new byte[BufferSize];

        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0)
            {
                break;
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            RefreshSegmentProgress(item, state, progress);
            UpdateSpeed(item, speedWindow, read, progress);
            await limiter.ThrottleAsync(read, cancellationToken);
        }

        return SegmentDownloadResult.Completed;
    }

    private async Task<HttpResponseMessage> SendDownloadRequestAsync(string downloadUrl, long? from, long? to, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
        if (from.HasValue)
        {
            request.Headers.Range = new RangeHeaderValue(from.Value, to);
        }

        return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private static int GetEffectiveConnections(string host, long? totalBytes, DownloadSettings settings)
    {
        if (!settings.EnableMultiConnectionDownloads)
        {
            return 1;
        }

        var rule = HostDownloadRules.GetForHost(host);
        if (!rule.AllowMultiConnection)
        {
            return 1;
        }

        if (settings.EnableAdaptiveConnectionCount && totalBytes is > 0)
        {
            var minimumSize = Math.Max(settings.MinMultiConnectionSizeBytes, rule.MinMultiConnectionSizeBytes);
            if (totalBytes.Value < minimumSize)
            {
                return 1;
            }
        }

        return Math.Clamp(Math.Min(settings.ConnectionsPerFile, rule.MaxConnectionsPerFile), 1, 16);
    }

    private static List<SegmentState> CreateSegments(long totalBytes, int connectionCount, string tempPath)
    {
        var segmentCount = Math.Clamp(connectionCount, 1, (int)Math.Min(totalBytes, connectionCount));
        var segmentSize = (long)Math.Ceiling(totalBytes / (double)segmentCount);
        var segments = new List<SegmentState>(segmentCount);

        for (var index = 0; index < segmentCount; index++)
        {
            var start = index * segmentSize;
            if (start >= totalBytes)
            {
                break;
            }

            var end = Math.Min(totalBytes - 1, start + segmentSize - 1);
            segments.Add(new SegmentState(index, start, end, $"{tempPath}.seg{index:000}"));
        }

        return segments;
    }

    private async Task MergeSegmentsAsync(string tempPath, IReadOnlyList<SegmentState> segments, CancellationToken cancellationToken)
    {
        await using var output = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.Read, BufferSize, true);
        var buffer = new byte[BufferSize];
        foreach (var segment in segments.OrderBy(segment => segment.Index))
        {
            await using var input = new FileStream(segment.Path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, true);
            while (true)
            {
                var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read == 0)
                {
                    break;
                }

                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
        }
    }

    private static void RefreshSegmentProgress(DownloadItem item, ResumeState state, IProgress<DownloadProgress>? progress)
    {
        var downloaded = state.Segments.Sum(segment =>
        {
            var length = segment.End - segment.Start + 1;
            return Math.Min(length, GetExistingBytes(segment.Path));
        });

        item.DownloadedBytes = downloaded;
        item.SizeBytes = state.ConfirmedTotalBytes ?? state.TotalBytes;
        progress?.Report(CreateProgress(item));
    }

    private DownloadResult CompleteDownload(DownloadItem item, DownloadPaths paths, long totalBytes, IProgress<DownloadProgress>? progress)
    {
        if (!string.Equals(paths.TempPath, paths.FinalPath, StringComparison.OrdinalIgnoreCase))
        {
            if (File.Exists(paths.FinalPath))
            {
                File.Delete(paths.FinalPath);
            }

            File.Move(paths.TempPath, paths.FinalPath);
        }

        DeleteResumeArtifacts(paths);
        item.LocalPath = paths.FinalPath;
        item.DownloadedBytes = totalBytes;
        item.SizeBytes = totalBytes;
        item.SpeedBytesPerSecond = 0;
        item.Status = DownloadStatus.Completed;
        progress?.Report(CreateProgress(item));
        return new DownloadResult(paths.FinalPath, totalBytes);
    }

    private bool TryCompleteFromExistingFinalFile(
        DownloadItem item,
        ResolvedDownload resolved,
        DownloadPaths paths,
        IProgress<DownloadProgress>? progress,
        out DownloadResult result)
    {
        var finalBytes = GetExistingBytes(paths.FinalPath);
        if (resolved.SizeBytes is > 0 && finalBytes == resolved.SizeBytes.Value)
        {
            item.SizeBytes = resolved.SizeBytes;
            item.DownloadedBytes = finalBytes;
            item.SpeedBytesPerSecond = 0;
            item.Status = DownloadStatus.Completed;
            progress?.Report(CreateProgress(item));
            result = new DownloadResult(paths.FinalPath, finalBytes);
            return true;
        }

        result = new DownloadResult(paths.FinalPath, 0);
        return false;
    }

    private static void MigrateLegacyPartialFile(DownloadPaths paths, long? expectedSize)
    {
        if (string.Equals(paths.FinalPath, paths.TempPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!File.Exists(paths.FinalPath) || File.Exists(paths.TempPath))
        {
            return;
        }

        var finalBytes = GetExistingBytes(paths.FinalPath);
        if (expectedSize is > 0 && finalBytes >= expectedSize.Value)
        {
            return;
        }

        File.Move(paths.FinalPath, paths.TempPath, true);
    }

    private static long? ResolveTotalSize(long? resolvedSizeBytes, HttpResponseMessage response, long existingBytes, bool resumeAccepted)
    {
        if (response.Content.Headers.ContentRange?.Length is > 0)
        {
            return response.Content.Headers.ContentRange.Length.Value;
        }

        if (resolvedSizeBytes is > 0)
        {
            return resolvedSizeBytes;
        }

        if (response.Content.Headers.ContentLength is not > 0)
        {
            return null;
        }

        return resumeAccepted
            ? existingBytes + response.Content.Headers.ContentLength.Value
            : response.Content.Headers.ContentLength.Value;
    }

    private static void ValidateFinalSize(long? expectedBytes, long actualBytes)
    {
        if (expectedBytes is > 0 && actualBytes != expectedBytes.Value)
        {
            throw new IOException($"Downloaded size mismatch. Expected {expectedBytes.Value} bytes, got {actualBytes} bytes.");
        }
    }

    private static void ValidateRemoteIdentity(DownloadPaths paths, ResumeState state, HttpResponseMessage response, DownloadSettings settings)
    {
        if (!settings.ValidateRemoteIdentity)
        {
            return;
        }

        var responseEtag = response.Headers.ETag?.Tag;
        if (!string.IsNullOrWhiteSpace(state.ETag)
            && !string.IsNullOrWhiteSpace(responseEtag)
            && !string.Equals(state.ETag, responseEtag, StringComparison.Ordinal))
        {
            DeletePartial(paths);
            throw new IOException("Remote file identity changed while resuming. Restarting this item is required.");
        }

        var responseLastModified = response.Content.Headers.LastModified;
        if (state.LastModified.HasValue
            && responseLastModified.HasValue
            && state.LastModified.Value != responseLastModified.Value)
        {
            DeletePartial(paths);
            throw new IOException("Remote file timestamp changed while resuming. Restarting this item is required.");
        }
    }

    private static void UpdateStateIdentity(ResumeState state, HttpResponseMessage response)
    {
        state.ETag ??= response.Headers.ETag?.Tag;
        state.LastModified ??= response.Content.Headers.LastModified;
    }

    private static void UpdateConfirmedTotalBytes(ResumeState state, HttpResponseMessage response)
    {
        var confirmed = response.Content.Headers.ContentRange?.Length;
        if (confirmed is > 0)
        {
            state.ConfirmedTotalBytes = confirmed;
        }
    }

    private ResumeState CreateSingleSegmentState(ResolvedDownload resolved, long? totalBytes, HttpResponseMessage response)
    {
        var state = new ResumeState(
            resolved.DownloadUrl,
            totalBytes,
            response.Headers.ETag?.Tag,
            response.Content.Headers.LastModified,
            []);
        return state;
    }

    private static ResumeState CreateSegmentedState(ResolvedDownload resolved, long totalBytes, IReadOnlyList<SegmentState> segments)
    {
        return new ResumeState(resolved.DownloadUrl, totalBytes, null, null, segments.ToList());
    }

    private ResumeState? LoadResumeState(DownloadPaths paths)
    {
        if (!File.Exists(paths.StatePath))
        {
            return null;
        }

        try
        {
            var state = JsonSerializer.Deserialize<ResumeState>(File.ReadAllText(paths.StatePath), _jsonOptions);
            return state?.Segments is null ? null : state;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsCompatibleSegmentState(ResumeState? state, long totalBytes)
    {
        return state?.TotalBytes == totalBytes
            && state.Segments.Count > 0
            && state.Segments.All(segment => segment.Start >= 0 && segment.End >= segment.Start && segment.End < totalBytes);
    }

    private void SaveResumeState(DownloadPaths paths, ResumeState state)
    {
        lock (_stateLock)
        {
            File.WriteAllText(paths.StatePath, JsonSerializer.Serialize(state, _jsonOptions));
        }
    }

    private static void DeletePartial(DownloadPaths paths)
    {
        DeleteIfExists(paths.TempPath);
        DeleteResumeArtifacts(paths);
    }

    private static void DeleteResumeArtifacts(DownloadPaths paths)
    {
        DeleteIfExists(paths.StatePath);
        var directory = Path.GetDirectoryName(paths.TempPath);
        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
        {
            foreach (var segment in Directory.EnumerateFiles(directory, $"{Path.GetFileName(paths.TempPath)}.seg*"))
            {
                DeleteIfExists(segment);
            }
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static long GetPartialBytes(DownloadPaths paths)
    {
        if (File.Exists(paths.FinalPath))
        {
            return GetExistingBytes(paths.FinalPath);
        }

        if (File.Exists(paths.TempPath))
        {
            return GetExistingBytes(paths.TempPath);
        }

        var directory = Path.GetDirectoryName(paths.TempPath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return 0;
        }

        return Directory.EnumerateFiles(directory, $"{Path.GetFileName(paths.TempPath)}.seg*")
            .Sum(GetExistingBytes);
    }

    private static long GetExistingBytes(string path)
    {
        return File.Exists(path) ? new FileInfo(path).Length : 0;
    }

    private static void UpdateSpeed(DownloadItem item, SpeedWindow speedWindow, int bytes, IProgress<DownloadProgress>? progress)
    {
        if (speedWindow.Add(bytes, out var bytesPerSecond))
        {
            item.SpeedBytesPerSecond = bytesPerSecond;
            progress?.Report(CreateProgress(item));
        }
    }

    private static DownloadProgress CreateProgress(DownloadItem item)
    {
        return new DownloadProgress(item.Id, item.DownloadedBytes, item.SizeBytes, item.SpeedBytesPerSecond, item.Status);
    }

    private static string SanitizeFileName(string fileName)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(invalid, '_');
        }

        return string.IsNullOrWhiteSpace(fileName) ? "download.bin" : fileName;
    }

    private sealed record DownloadPaths(string FinalPath, string TempPath, string StatePath)
    {
        public static DownloadPaths Create(string destinationFolder, string fileName, bool useTemporaryFile)
        {
            var finalPath = Path.Combine(destinationFolder, SanitizeFileName(fileName));
            var tempPath = useTemporaryFile ? $"{finalPath}.ffdownload" : finalPath;
            return new DownloadPaths(finalPath, tempPath, $"{tempPath}.state");
        }
    }

    private sealed record SegmentState(int Index, long Start, long End, string Path);

    private sealed record ResumeState(
        string DownloadUrl,
        long? TotalBytes,
        string? ETag,
        DateTimeOffset? LastModified,
        List<SegmentState> Segments)
    {
        public string? ETag { get; set; } = ETag;

        public DateTimeOffset? LastModified { get; set; } = LastModified;

        // Server-confirmed size from a Content-Range response; TotalBytes stays the (often rounded)
        // advertised estimate used for segment planning so resume-compatibility checks stay stable.
        public long? ConfirmedTotalBytes { get; set; }
    }

    private enum SegmentDownloadResult
    {
        Completed,
        RangeNotSupported
    }

    private sealed class DownloadSpeedLimiter(Func<long> bytesPerSecondProvider)
    {
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private readonly SemaphoreSlim _gate = new(1, 1);
        private long _bytes;
        private long _activeLimit;

        public async Task ThrottleAsync(int bytes, CancellationToken cancellationToken)
        {
            var bytesPerSecond = bytesPerSecondProvider();
            if (bytesPerSecond <= 0)
            {
                _activeLimit = 0;
                return;
            }

            await _gate.WaitAsync(cancellationToken);
            try
            {
                if (_activeLimit != bytesPerSecond)
                {
                    _activeLimit = bytesPerSecond;
                    _bytes = 0;
                    _stopwatch.Restart();
                }

                _bytes += bytes;
                var expectedSeconds = _bytes / (double)bytesPerSecond;
                var delay = expectedSeconds - _stopwatch.Elapsed.TotalSeconds;
                if (delay > 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Min(delay, 2)), cancellationToken);
                }
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    private sealed class SpeedWindow
    {
        private readonly object _lock = new();
        private readonly Stopwatch _window = Stopwatch.StartNew();
        private long _bytes;

        public bool Add(int bytes, out double bytesPerSecond)
        {
            lock (_lock)
            {
                _bytes += bytes;
                if (_window.ElapsedMilliseconds < 250)
                {
                    bytesPerSecond = 0;
                    return false;
                }

                bytesPerSecond = _bytes / Math.Max(_window.Elapsed.TotalSeconds, 0.001);
                _bytes = 0;
                _window.Restart();
                return true;
            }
        }
    }
}
