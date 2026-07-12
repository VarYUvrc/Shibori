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
            || string.Equals(arg, "--pause-only", StringComparison.OrdinalIgnoreCase));
        if (diagnosticMode)
        {
            if (args.Any(arg => string.Equals(arg, "--self-test", StringComparison.OrdinalIgnoreCase))) DiagnosticRunner.RunSelfTest();
            else if (args.Any(arg => string.Equals(arg, "--pause-only", StringComparison.OrdinalIgnoreCase))) DiagnosticRunner.RunPauseOnly();
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
    public static string LogPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Shibori", "shibori.log");

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
        File.AppendAllText(LogPath, log.ToString(), Encoding.UTF8);
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
        File.AppendAllText(LogPath, log.ToString(), Encoding.UTF8);
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
        File.AppendAllText(LogPath, log.ToString(), Encoding.UTF8);
    }
}
