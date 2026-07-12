using System.Text;
using System.IO;

namespace Shibori;

internal static class AppLogger
{
    private static readonly object Sync = new();
    private const long MaxBytes = 1024 * 1024;
    private const int RetentionDays = 14;

    public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Shibori", "logs");
    public static string CurrentLogPath => Path.Combine(DirectoryPath, $"shibori-{DateTime.Now:yyyyMMdd}.log");

    public static void Info(string message) => Write("INFO", message);
    public static void Error(Exception exception, string context) => Write("ERROR", $"{context}: {exception}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(DirectoryPath);
                var path = CurrentLogPath;
                if (File.Exists(path) && new FileInfo(path).Length >= MaxBytes) return;
                File.AppendAllText(path, $"[{DateTimeOffset.Now:O}] [{level}] {message}{Environment.NewLine}", Encoding.UTF8);
                foreach (var old in Directory.EnumerateFiles(DirectoryPath, "shibori-*.log")
                    .Where(file => File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-RetentionDays)))
                    File.Delete(old);
            }
        }
        catch { }
    }
}
