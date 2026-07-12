using Application = System.Windows.Application;
using System.IO;
using System.Text;
using System.Windows;

namespace Shibori;

public partial class App : Application
{
    public static bool StartedWithWindows { get; private set; }
    private readonly bool diagnosticMode;

    public App()
    {
        var args = Environment.GetCommandLineArgs();
        StartedWithWindows = args.Any(arg => string.Equals(arg, "--startup", StringComparison.OrdinalIgnoreCase));
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
            if (service.HasBackup) service.Restore();
            var initial = service.GetMonitors().Where(item => item.IsConnected).ToArray();
            var originalPrimary = initial.Single(item => item.IsPrimary);
            var monitor = initial.FirstOrDefault(item => !item.IsPrimary)
                ?? throw new InvalidOperationException("サブモニターが見つかりません。");
            log.AppendLine($"Target: {monitor.DeviceName}");
            service.SetPrimary(monitor);
            if (!service.GetMonitors().Single(item => item.PathKey == monitor.PathKey).IsPrimary)
                throw new InvalidOperationException("メインモニターの変更結果が反映されていません。");
            log.AppendLine("Set primary: OK");
            service.SetPrimary(originalPrimary);
            if (!service.GetMonitors().Single(item => item.PathKey == originalPrimary.PathKey).IsPrimary)
                throw new InvalidOperationException("元のメインモニターへ戻せませんでした。");
            log.AppendLine("Restore primary: OK");
            service.Pause([monitor]);
            if (service.GetMonitors().Single(item => item.PathKey == monitor.PathKey).IsConnected)
                throw new InvalidOperationException("サブモニターが停止されていません。");
            log.AppendLine("Pause: OK");
            service.Restore();
            if (service.GetMonitors().Count(item => item.IsConnected) != initial.Length)
                throw new InvalidOperationException("全モニターが復元されていません。");
            log.AppendLine("Restore: OK");
            service.Pause([originalPrimary]);
            var withoutPrimary = service.GetMonitors();
            if (withoutPrimary.Single(item => item.PathKey == originalPrimary.PathKey).IsConnected
                || withoutPrimary.Count(item => item.IsConnected) != initial.Length - 1
                || withoutPrimary.Count(item => item.IsPrimary) != 1)
                throw new InvalidOperationException("メインモニターの停止結果が不正です。");
            log.AppendLine("Pause primary: OK");
            service.Restore();
            var final = service.GetMonitors();
            if (final.Count(item => item.IsConnected) != initial.Length
                || !final.Single(item => item.PathKey == originalPrimary.PathKey).IsPrimary)
                throw new InvalidOperationException("元の表示構成へ復元できませんでした。");
            log.AppendLine("Restore original configuration: OK");
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
            var paused = service.GetMonitors();
            if (paused.Count(monitor => monitor.IsConnected) != 1 || paused.Count(monitor => !monitor.IsConnected) != 2)
                throw new InvalidOperationException("Partial pause changed an unrelated monitor.");
            service.Restore(monitors[0]);
            var middle = service.GetMonitors();
            var connectedAfterOne = middle.Count(monitor => monitor.IsConnected);
            var stoppedAfterOne = middle.Count(monitor => !monitor.IsConnected);
            if (connectedAfterOne != 2 || stoppedAfterOne != 1)
                throw new InvalidOperationException("Partial restore changed an unrelated monitor.");
            log.AppendLine($"After restoring one: connected={connectedAfterOne}, stopped={stoppedAfterOne}");
            service.Restore(monitors[1]);
            var restored = service.GetMonitors();
            if (restored.Count(monitor => monitor.IsConnected) != 3 || restored.Any(monitor => !monitor.IsConnected))
                throw new InvalidOperationException("Full restore did not restore every monitor.");
            log.AppendLine($"After restoring both: connected={restored.Count(monitor => monitor.IsConnected)}");
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
