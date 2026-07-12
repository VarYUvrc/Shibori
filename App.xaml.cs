using Application = System.Windows.Application;
using System.IO;
using System.Text;
using System.Windows;

namespace Shibori;

public partial class App : Application
{
    private readonly bool diagnosticMode;

    public App()
    {
        var args = Environment.GetCommandLineArgs();
        diagnosticMode = args.Any(arg => string.Equals(arg, "--diagnose", StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "--self-test", StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "--pause-only", StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "--partial-test", StringComparison.OrdinalIgnoreCase));
        if (diagnosticMode)
        {
            if (args.Any(arg => string.Equals(arg, "--self-test", StringComparison.OrdinalIgnoreCase))) DiagnosticRunner.RunSelfTest();
            else if (args.Any(arg => string.Equals(arg, "--pause-only", StringComparison.OrdinalIgnoreCase))) DiagnosticRunner.RunPauseOnly();
            else if (args.Any(arg => string.Equals(arg, "--partial-test", StringComparison.OrdinalIgnoreCase))) DiagnosticRunner.RunPartialRestoreTest();
            else DiagnosticRunner.Run();
        }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        if (diagnosticMode)
        {
            Shutdown();
            return;
        }
        base.OnStartup(e);
    }
}

internal static class DiagnosticRunner
{
    public static string LogPath => Path.Combine(AppLogger.DirectoryPath, "diagnostics.log");

    public static void Run()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
        var log = new StringBuilder();
        log.AppendLine($"[{DateTimeOffset.Now:O}] Shibori diagnostics started");
        log.AppendLine($"OS: {Environment.OSVersion}");
        log.AppendLine($"Process: {Environment.ProcessPath}");
        try
        {
            var service = new DisplayConfigurationService();
            foreach (var monitor in service.GetMonitors())
                log.AppendLine($"Monitor: {monitor.DeviceName}, {monitor.Width}x{monitor.Height}, primary={monitor.IsPrimary}");
            foreach (var path in service.GetCcdPathSummary()) log.AppendLine($"CCD path: {path}");
            log.AppendLine("Monitor enumeration: OK");
        }
        catch (Exception ex) { log.AppendLine($"ERROR: {ex}"); }
        log.AppendLine($"[{DateTimeOffset.Now:O}] finished");
        WriteLog(log.ToString());
    }

    public static void RunSelfTest()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
        var log = new StringBuilder();
        log.AppendLine($"[{DateTimeOffset.Now:O}] Shibori self-test started");
        try
        {
            var service = new DisplayConfigurationService();
            var monitor = service.GetMonitors().FirstOrDefault(item => !item.IsPrimary)
                ?? throw new InvalidOperationException("サブモニターが見つかりません。");
            log.AppendLine($"Target: {monitor.DeviceName}");
            service.Pause([monitor]);
            log.AppendLine("Pause: OK");
            service.Restore();
            log.AppendLine("Restore: OK");
        }
        catch (Exception ex) { log.AppendLine($"ERROR: {ex}"); }
        log.AppendLine($"[{DateTimeOffset.Now:O}] self-test finished");
        WriteLog(log.ToString());
    }

    public static void RunPauseOnly()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
        var log = new StringBuilder();
        log.AppendLine($"[{DateTimeOffset.Now:O}] pause-only test started");
        try
        {
            var service = new DisplayConfigurationService();
            var monitor = service.GetMonitors().FirstOrDefault(item => !item.IsPrimary && item.IsConnected)
                ?? throw new InvalidOperationException("サブモニターが見つかりません。");
            service.Pause([monitor]);
            log.AppendLine($"Pause only: OK ({monitor.DeviceName})");
        }
        catch (Exception ex) { log.AppendLine($"ERROR: {ex}"); }
        log.AppendLine($"[{DateTimeOffset.Now:O}] pause-only test finished");
        WriteLog(log.ToString());
    }

    public static void RunPartialRestoreTest()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
        var log = new StringBuilder();
        log.AppendLine($"[{DateTimeOffset.Now:O}] partial-restore test started");
        try
        {
            var service = new DisplayConfigurationService();
            var monitors = service.GetMonitors().Where(monitor => !monitor.IsPrimary && monitor.IsConnected).ToArray();
            if (monitors.Length < 2) throw new InvalidOperationException("サブモニターが2台必要です。");
            service.Pause([monitors[0]]);
            service.Pause([monitors[1]]);
            service.Restore(monitors[0]);
            var middle = service.GetMonitors();
            log.AppendLine($"After restoring one: connected={middle.Count(monitor => monitor.IsConnected)}, stopped={middle.Count(monitor => !monitor.IsConnected)}");
            service.Restore(monitors[1]);
            log.AppendLine($"After restoring both: connected={service.GetMonitors().Count(monitor => monitor.IsConnected)}");
        }
        catch (Exception ex) { log.AppendLine($"ERROR: {ex}"); }
        log.AppendLine($"[{DateTimeOffset.Now:O}] partial-restore test finished");
        WriteLog(log.ToString());
    }

    private static void WriteLog(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
        if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 2 * 1024 * 1024) File.Delete(LogPath);
        File.AppendAllText(LogPath, content, Encoding.UTF8);
    }
}
