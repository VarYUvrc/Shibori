using System.Runtime.InteropServices;
using System.Text.Json;
using System.IO;
using System.Windows.Forms;

namespace Shibori;

public sealed class DisplayConfigurationService
{
    private const string BackupFileName = "display-backup.json";

    public IReadOnlyList<MonitorInfo> GetMonitors() =>
        Screen.AllScreens
            .OrderByDescending(screen => screen.Primary)
            .ThenBy(screen => screen.DeviceName)
            .Select((screen, index) => new MonitorInfo(
                index + 1,
                screen.DeviceName,
                screen.Bounds.Width,
                screen.Bounds.Height,
                screen.Primary,
                screen.Bounds.X,
                screen.Bounds.Y))
            .ToArray();

    public void SaveBackup(IReadOnlyList<MonitorInfo> monitors)
    {
        var path = BackupPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var snapshot = new DisplayBackup(DateTimeOffset.UtcNow, monitors.ToArray());
        File.WriteAllText(path, JsonSerializer.Serialize(snapshot, JsonOptions));
    }

    public bool HasBackup => File.Exists(BackupPath);

    public DateTimeOffset? GetBackupTime()
    {
        if (!HasBackup) return null;
        try { return JsonSerializer.Deserialize<DisplayBackup>(File.ReadAllText(BackupPath))?.CreatedAt; }
        catch { return null; }
    }

    public void Pause(IReadOnlyList<MonitorInfo> selected)
    {
        if (selected.Count == 0) throw new InvalidOperationException("切り離すモニターを選択してください。");
        if (selected.Any(m => m.IsPrimary)) throw new InvalidOperationException("メインモニターは切り離せません。別のモニターを選択してください。");
        if (selected.Count >= Screen.AllScreens.Length) throw new InvalidOperationException("少なくとも1台のモニターを残してください。");

        SaveBackup(GetMonitors());
        foreach (var monitor in selected)
        {
            var result = NativeMethods.ChangeDisplaySettingsEx(
                monitor.DeviceName, IntPtr.Zero, IntPtr.Zero,
                NativeMethods.ChangeDisplaySettingsFlags.CDS_DISABLE, IntPtr.Zero);
            if (result != NativeMethods.DisplayChangeResult.Success)
                throw new InvalidOperationException($"{monitor.DeviceName} の切り離しに失敗しました (コード: {result})。");
        }
    }

    public void Restore()
    {
        if (!HasBackup) throw new InvalidOperationException("復元用のバックアップがありません。");

        // CCD APIで現在の表示トポロジーを再適用し、Windowsに保存済み構成を再評価させます。
        var result = NativeMethods.SetDisplayConfig(
            0, IntPtr.Zero, 0, IntPtr.Zero,
            NativeMethods.SetDisplayConfigFlags.SDC_TOPOLOGY_EXTEND | NativeMethods.SetDisplayConfigFlags.SDC_APPLY);
        if (result != 0 && result != NativeMethods.ErrorSuccess)
        {
            // 古い構成を持つ環境ではChangeDisplaySettingsExが復元の最後の手段になります。
            foreach (var monitor in ReadBackup().Monitors)
            {
                NativeMethods.ChangeDisplaySettingsEx(monitor.DeviceName, IntPtr.Zero, IntPtr.Zero,
                    NativeMethods.ChangeDisplaySettingsFlags.CDS_RESET, IntPtr.Zero);
            }
        }

        File.Delete(BackupPath);
    }

    private DisplayBackup ReadBackup() => JsonSerializer.Deserialize<DisplayBackup>(File.ReadAllText(BackupPath), JsonOptions)
        ?? throw new InvalidOperationException("バックアップを読み込めません。");

    private static string BackupPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Shibori", BackupFileName);

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private sealed record DisplayBackup(DateTimeOffset CreatedAt, MonitorInfo[] Monitors);
}

public sealed record MonitorInfo(int Index, string DeviceName, int Width, int Height, bool IsPrimary, int X, int Y)
{
    public string Role => IsPrimary ? "メイン" : "サブ";
    public string Resolution => $"{Width} × {Height}";
    public override string ToString() => $"モニター {Index} 　{Resolution} 　{Role}";
}

internal static class NativeMethods
{
    public const int ErrorSuccess = 0;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern DisplayChangeResult ChangeDisplaySettingsEx(string? deviceName, IntPtr devMode, IntPtr hwnd, ChangeDisplaySettingsFlags flags, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern int SetDisplayConfig(uint numPathArrayElements, IntPtr pathArray, uint numModeInfoArrayElements, IntPtr modeInfoArray, SetDisplayConfigFlags flags);

    public enum DisplayChangeResult : int { Success = 0, Restart = 1, Failed = -1, BadMode = -2, NotUpdated = -3, BadFlags = -4, BadParam = -5 }
    [Flags] public enum ChangeDisplaySettingsFlags { CDS_RESET = 0x40000000, CDS_DISABLE = 0x02000000 }
    [Flags] public enum SetDisplayConfigFlags : uint { SDC_APPLY = 0x80, SDC_TOPOLOGY_EXTEND = 0x04 }
}
