namespace FFDownloader.App.Services;

using Directory = System.IO.Directory;
using File = System.IO.File;
using Path = System.IO.Path;

public static class AppLogger
{
    private static readonly object LockObject = new();

    public static string LogPath => Path.Combine(AppContext.BaseDirectory, "data", "ffdownloader.log");

    public static void Info(string message)
    {
        Write("INFO", message);
    }

    public static void Error(Exception exception, string context)
    {
        Write("ERROR", $"{context}{Environment.NewLine}{exception}");
    }

    private static void Write(string level, string message)
    {
        lock (LockObject)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(
                LogPath,
                $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}] {level} {message}{Environment.NewLine}");
        }
    }
}
