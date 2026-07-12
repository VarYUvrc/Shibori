using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;

namespace Shibori;

public sealed class DisplayConfigurationService
{
    private const string BackupFileName = "display-backup.json";
    private CcdConfiguration? backupConfiguration;

    public IReadOnlyList<MonitorInfo> GetMonitors() =>
        Screen.AllScreens
            .OrderByDescending(screen => screen.Primary)
            .ThenBy(screen => screen.DeviceName)
            .Select((screen, index) => new MonitorInfo(index + 1, screen.DeviceName,
                screen.Bounds.Width, screen.Bounds.Height, screen.Primary))
            .ToArray();

    public IReadOnlyList<string> GetCcdPathSummary()
    {
        var configuration = CcdConfiguration.Read();
        return configuration.Paths.Select(path => $"{path.SourceDeviceName ?? "<unknown>"}: flags=0x{path.Info.Flags:X8}, sourceMode={path.Info.SourceInfo.ModeInfoIdx}, targetMode={path.Info.TargetInfo.ModeInfoIdx}").ToArray();
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
        if (selected.Count == 0) throw new InvalidOperationException("一時停止するモニターを選択してください。");
        if (selected.Any(m => m.IsPrimary)) throw new InvalidOperationException("メインモニターは一時停止できません。");
        var configuration = CcdConfiguration.Read();
        if (backupConfiguration is null)
        {
            if (!HasBackup) SaveBackup(GetMonitors());
            backupConfiguration = configuration.Clone();
        }
        var selectedNames = selected.Select(m => m.DeviceName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var activePathsAfterChange = configuration.Paths.Count(path => (path.Info.Flags & NativeMethods.DISPLAYCONFIG_PATH_ACTIVE) != 0
            && (path.SourceDeviceName is null || !selectedNames.Contains(path.SourceDeviceName)));
        if (activePathsAfterChange == 0) throw new InvalidOperationException("少なくとも1台のモニターを残してください。");
        var changed = configuration.Paths.Any(path => path.SourceDeviceName is not null && selectedNames.Contains(path.SourceDeviceName));
        if (!changed) throw new InvalidOperationException("選択したモニターの表示パスを特定できませんでした。");

        var pathsToApply = configuration.Paths
            .Where(path => path.SourceDeviceName is null || !selectedNames.Contains(path.SourceDeviceName))
            .Select(path => path.Info)
            .ToArray();

        var result = NativeMethods.SetDisplayConfig(
            (uint)pathsToApply.Length,
            pathsToApply,
            (uint)configuration.Modes.Length,
            configuration.Modes,
            NativeMethods.SDC_APPLY | NativeMethods.SDC_USE_SUPPLIED_DISPLAY_CONFIG | NativeMethods.SDC_ALLOW_CHANGES
                | NativeMethods.SDC_VIRTUAL_MODE_AWARE | NativeMethods.SDC_VIRTUAL_REFRESH_RATE_AWARE);

        if (result != 0) throw new InvalidOperationException($"表示構成の変更に失敗しました (エラーコード: {result})。");
    }

    public void Restore()
    {
        var result = -1;
        if (backupConfiguration is not null)
        {
            var configuration = backupConfiguration;
            foreach (var path in configuration.Paths)
            {
                var info = path.Info;
                info.Flags |= NativeMethods.DISPLAYCONFIG_PATH_ACTIVE;
                path.Info = info;
            }
            result = NativeMethods.SetDisplayConfig(
                (uint)configuration.Paths.Count,
                configuration.Paths.Select(p => p.Info).ToArray(),
                (uint)configuration.Modes.Length,
                configuration.Modes,
                ApplyFlags);
        }

        if (result != 0)
            result = NativeMethods.SetDisplayConfig(0, null, 0, null,
                NativeMethods.SDC_APPLY | NativeMethods.SDC_USE_DATABASE_CURRENT);
        if (result != 0) throw new InvalidOperationException($"表示構成の復元に失敗しました (エラーコード: {result})。");
        File.Delete(BackupPath);
    }

    public void RestoreFromDatabase()
    {
        var result = NativeMethods.SetDisplayConfig(0, null, 0, null,
            NativeMethods.SDC_APPLY | NativeMethods.SDC_USE_DATABASE_CURRENT);
        if (result != 0) throw new InvalidOperationException($"保存済み表示構成の復元に失敗しました (エラーコード: {result})。");
    }

    private const uint ApplyFlags = NativeMethods.SDC_APPLY | NativeMethods.SDC_USE_SUPPLIED_DISPLAY_CONFIG
        | NativeMethods.SDC_ALLOW_CHANGES | NativeMethods.SDC_VIRTUAL_MODE_AWARE
        | NativeMethods.SDC_VIRTUAL_REFRESH_RATE_AWARE | NativeMethods.SDC_FORCE_MODE_ENUMERATION;

    private void SaveBackup(IReadOnlyList<MonitorInfo> monitors)
    {
        if (HasBackup) return;
        Directory.CreateDirectory(Path.GetDirectoryName(BackupPath)!);
        File.WriteAllText(BackupPath, JsonSerializer.Serialize(new DisplayBackup(DateTimeOffset.UtcNow, monitors), new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string BackupPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Shibori", BackupFileName);
    private sealed record DisplayBackup(DateTimeOffset CreatedAt, IReadOnlyList<MonitorInfo> Monitors);

    private sealed class CcdConfiguration
    {
        public List<CcdPath> Paths { get; } = [];
        public NativeMethods.DISPLAYCONFIG_MODE_INFO[] Modes { get; private init; } = [];

        public CcdConfiguration Clone()
        {
            var clone = new CcdConfiguration { Modes = Modes.ToArray() };
            clone.Paths.AddRange(Paths.Select(path => new CcdPath(path.Info, path.SourceDeviceName)));
            return clone;
        }

        public static CcdConfiguration Read()
        {
            var flags = NativeMethods.QDC_ALL_PATHS | NativeMethods.QDC_VIRTUAL_MODE_AWARE;
            NativeMethods.GetDisplayConfigBufferSizes(flags, out var pathCount, out var modeCount);
            var paths = new NativeMethods.DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new NativeMethods.DISPLAYCONFIG_MODE_INFO[modeCount];
            var result = NativeMethods.QueryDisplayConfig(flags, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
            if (result != 0) throw new InvalidOperationException($"現在の表示構成を読み込めませんでした (エラーコード: {result})。");

            var config = new CcdConfiguration { Modes = modes[..(int)modeCount] };
            foreach (var path in paths[..(int)pathCount])
            {
                var sourceName = new NativeMethods.DISPLAYCONFIG_SOURCE_DEVICE_NAME
                {
                    Header = new NativeMethods.DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        Type = NativeMethods.DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME,
                        Size = (uint)Marshal.SizeOf<NativeMethods.DISPLAYCONFIG_SOURCE_DEVICE_NAME>(),
                        AdapterId = path.SourceInfo.AdapterId,
                        Id = path.SourceInfo.SourceId
                    }
                };
                NativeMethods.DisplayConfigGetDeviceInfo(ref sourceName);
                config.Paths.Add(new CcdPath(path, sourceName.ViewGdiDeviceName));
            }
            return config;
        }
    }

    private sealed class CcdPath(NativeMethods.DISPLAYCONFIG_PATH_INFO info, string sourceDeviceName)
    {
        public NativeMethods.DISPLAYCONFIG_PATH_INFO Info { get; set; } = info;
        public string SourceDeviceName { get; } = sourceDeviceName;
    }
}

public sealed record MonitorInfo(int Index, string DeviceName, int Width, int Height, bool IsPrimary)
{
    public string Role => IsPrimary ? "メイン" : "サブ";
    public string Resolution => $"{Width} × {Height}";
}

internal static class NativeMethods
{
    public const uint DISPLAYCONFIG_PATH_ACTIVE = 0x00000001;
    public const uint DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME = 1;
    public const uint QDC_ALL_PATHS = 1;
    public const uint QDC_VIRTUAL_MODE_AWARE = 0x10;
    public const uint SDC_APPLY = 0x80;
    public const uint SDC_USE_SUPPLIED_DISPLAY_CONFIG = 0x20;
    public const uint SDC_ALLOW_CHANGES = 0x400;
    public const uint SDC_VIRTUAL_MODE_AWARE = 0x8000;
    public const uint SDC_VIRTUAL_REFRESH_RATE_AWARE = 0x20000;
    public const uint SDC_FORCE_MODE_ENUMERATION = 0x1000;
    public const uint SDC_USE_DATABASE_CURRENT = 0x0F;

    [DllImport("user32.dll")]
    public static extern int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);

    [DllImport("user32.dll")]
    public static extern int QueryDisplayConfig(uint flags, ref uint pathCount, [Out] DISPLAYCONFIG_PATH_INFO[] paths, ref uint modeCount, [Out] DISPLAYCONFIG_MODE_INFO[] modes, IntPtr topologyId);

    [DllImport("user32.dll")]
    public static extern int SetDisplayConfig(uint pathCount, [In] DISPLAYCONFIG_PATH_INFO[]? paths, uint modeCount, [In] DISPLAYCONFIG_MODE_INFO[]? modes, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME requestPacket);

    [StructLayout(LayoutKind.Sequential)] public struct LUID { public uint LowPart; public int HighPart; }
    [StructLayout(LayoutKind.Sequential)] public struct DISPLAYCONFIG_RATIONAL { public uint Numerator; public uint Denominator; }
    [StructLayout(LayoutKind.Sequential)] public struct DISPLAYCONFIG_PATH_SOURCE_INFO { public LUID AdapterId; public uint SourceId; public uint ModeInfoIdx; public uint StatusFlags; }
    [StructLayout(LayoutKind.Sequential)] public struct DISPLAYCONFIG_PATH_TARGET_INFO { public LUID AdapterId; public uint Id; public uint ModeInfoIdx; public uint OutputTechnology; public uint Rotation; public uint Scaling; public DISPLAYCONFIG_RATIONAL RefreshRate; public uint ScanLineOrdering; public int TargetAvailable; public uint StatusFlags; }
    [StructLayout(LayoutKind.Sequential)] public struct DISPLAYCONFIG_PATH_INFO { public DISPLAYCONFIG_PATH_SOURCE_INFO SourceInfo; public DISPLAYCONFIG_PATH_TARGET_INFO TargetInfo; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)] public struct DISPLAYCONFIG_VIDEO_SIGNAL_INFO { public ulong PixelRate; public DISPLAYCONFIG_RATIONAL HSyncFreq; public DISPLAYCONFIG_RATIONAL VSyncFreq; public DISPLAYCONFIG_2DREGION ActiveSize; public DISPLAYCONFIG_2DREGION TotalSize; public uint VideoStandard; public uint ScanLineOrdering; public uint AdditionalVideoSignalInfo; }
    [StructLayout(LayoutKind.Sequential)] public struct DISPLAYCONFIG_2DREGION { public uint CX; public uint CY; }
    [StructLayout(LayoutKind.Sequential)] public struct DISPLAYCONFIG_TARGET_MODE { public DISPLAYCONFIG_VIDEO_SIGNAL_INFO TargetVideoSignalInfo; }
    [StructLayout(LayoutKind.Sequential)] public struct DISPLAYCONFIG_SOURCE_MODE { public uint Width; public uint Height; public uint PixelFormat; public uint PositionX; public uint PositionY; }
    [StructLayout(LayoutKind.Explicit, Size = 48)] public struct DISPLAYCONFIG_MODE_INFO_UNION { [FieldOffset(0)] public DISPLAYCONFIG_TARGET_MODE TargetMode; [FieldOffset(0)] public DISPLAYCONFIG_SOURCE_MODE SourceMode; }
    [StructLayout(LayoutKind.Sequential)] public struct DISPLAYCONFIG_MODE_INFO { public uint InfoType; public uint Id; public LUID AdapterId; public DISPLAYCONFIG_MODE_INFO_UNION ModeInfo; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] public struct DISPLAYCONFIG_DEVICE_INFO_HEADER { public uint Type; public uint Size; public LUID AdapterId; public uint Id; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] public struct DISPLAYCONFIG_SOURCE_DEVICE_NAME { public DISPLAYCONFIG_DEVICE_INFO_HEADER Header; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string ViewGdiDeviceName; }
}
