using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;

namespace Shibori;

public sealed class DisplayConfigurationService
{
    private const string BackupFileName = "display-backup.json";
    private readonly object sync = new();
    private CcdConfiguration? backupConfiguration;

    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var screens = Screen.AllScreens.ToDictionary(screen => screen.DeviceName, StringComparer.OrdinalIgnoreCase);
        var configuration = CcdConfiguration.Read();
        var backup = ReadBackup();
        var active = configuration.Paths
            .Where(path => (path.Info.Flags & NativeMethods.DISPLAYCONFIG_PATH_ACTIVE) != 0 && !string.IsNullOrWhiteSpace(path.SourceDeviceName))
            .GroupBy(path => path.PathKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Select((path, index) =>
            {
                var saved = backup?.Monitors?.FirstOrDefault(monitor => string.Equals(monitor.PathKey, path.PathKey, StringComparison.OrdinalIgnoreCase));
                var screen = screens.GetValueOrDefault(path.SourceDeviceName);
                return new MonitorInfo(index + 1, path.SourceDeviceName, screen?.Bounds.Width ?? saved?.Width ?? 0,
                    screen?.Bounds.Height ?? saved?.Height ?? 0, screen?.Primary ?? saved?.IsPrimary ?? false, true, path.PathKey);
            }).ToList();

        if (backup?.Monitors is not null)
        {
            var activeKeys = active.Select(monitor => monitor.PathKey)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var monitor in backup.Monitors.Where(monitor => !activeKeys.Contains(monitor.PathKey)))
                active.Add(monitor with { IsConnected = false });
        }

        return active.Select((monitor, index) => monitor with { Index = index + 1 }).ToArray();
    }

    public IReadOnlyList<string> GetCcdPathSummary()
    {
        var configuration = CcdConfiguration.Read();
        return configuration.Paths.Select(path =>
            $"{path.SourceDeviceName ?? "<unknown>"}: flags=0x{path.Info.Flags:X8}, sourceMode={path.Info.SourceInfo.ModeInfoIdx}, targetMode={path.Info.TargetInfo.ModeInfoIdx}").ToArray();
    }

    public bool HasBackup => File.Exists(BackupPath);

    public DateTimeOffset? GetBackupTime() => ReadBackup()?.CreatedAt;

    public void Pause(IReadOnlyList<MonitorInfo> selected)
    {
        lock (sync)
        {
            if (selected.Count == 0) throw new InvalidOperationException("切断するモニターを選択してください。");
            if (selected.Any(monitor => monitor.IsPrimary)) throw new InvalidOperationException("メインモニターは切断できません。");

            var configuration = CcdConfiguration.Read();
            EnsureBackup(configuration);

            var selectedKeys = selected.Select(monitor => monitor.PathKey)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var activePaths = configuration.Paths
                .Where(path => (path.Info.Flags & NativeMethods.DISPLAYCONFIG_PATH_ACTIVE) != 0)
                .ToArray();
            var pathsToApply = activePaths
                .Where(path => !selectedKeys.Contains(path.PathKey))
                .Select(path => path.Info)
                .ToArray();

            if (pathsToApply.Length == 0) throw new InvalidOperationException("少なくとも1台のモニターを残してください。");
            if (pathsToApply.Length == activePaths.Length) throw new InvalidOperationException("選択したモニターの表示パスを特定できませんでした。");

            var result = NativeMethods.SetDisplayConfig(
                (uint)pathsToApply.Length,
                pathsToApply,
                (uint)configuration.Modes.Length,
                configuration.Modes,
                NativeMethods.SDC_APPLY | NativeMethods.SDC_USE_SUPPLIED_DISPLAY_CONFIG
                    | NativeMethods.SDC_ALLOW_CHANGES | NativeMethods.SDC_VIRTUAL_MODE_AWARE
                    | NativeMethods.SDC_VIRTUAL_REFRESH_RATE_AWARE);
            if (result != 0) throw new InvalidOperationException($"表示構成の変更に失敗しました (エラーコード: {result})。");
        }
    }

    public void Restore()
    {
        lock (sync)
        {
            var configuration = backupConfiguration ?? LoadBackupConfiguration()
                ?? throw new InvalidOperationException("復元用のバックアップがありません。");
            var paths = configuration.Paths
                .Where(path => (path.Info.Flags & NativeMethods.DISPLAYCONFIG_PATH_ACTIVE) != 0)
                .Select(path => path.Info)
                .ToArray();

            var result = NativeMethods.SetDisplayConfig(
                (uint)paths.Length, paths,
                (uint)configuration.Modes.Length, configuration.Modes,
                NativeMethods.SDC_APPLY | NativeMethods.SDC_USE_SUPPLIED_DISPLAY_CONFIG
                    | NativeMethods.SDC_ALLOW_CHANGES | NativeMethods.SDC_VIRTUAL_MODE_AWARE
                    | NativeMethods.SDC_VIRTUAL_REFRESH_RATE_AWARE | NativeMethods.SDC_FORCE_MODE_ENUMERATION);
            if (result != 0) throw new InvalidOperationException($"表示構成の復元に失敗しました (エラーコード: {result})。");

            backupConfiguration = null;
            File.Delete(BackupPath);
        }
    }

    private void EnsureBackup(CcdConfiguration configuration)
    {
        if (backupConfiguration is not null) return;
        var loadedBackup = LoadBackupConfiguration();
        backupConfiguration = loadedBackup ?? configuration.Clone();
        if (loadedBackup is null)
        {
            var monitors = GetMonitors().Where(monitor => monitor.IsConnected).ToArray();
            SaveBackup(new DisplayBackup(DateTimeOffset.UtcNow, monitors,
                backupConfiguration.Paths.Select(path => new BackupPathEntry(path.PathKey, path.SourceDeviceName, Serialize(path.Info))).ToArray(),
                Serialize(backupConfiguration.Modes)));
        }
    }

    private void SaveBackup(DisplayBackup backup)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(BackupPath)!);
        File.WriteAllText(BackupPath, JsonSerializer.Serialize(backup, JsonOptions));
    }

    private CcdConfiguration? LoadBackupConfiguration()
    {
        var backup = ReadBackup();
        if (backup is null) return null;
        if (backup.Paths.Length == 0 || string.IsNullOrWhiteSpace(backup.Modes))
            throw new InvalidDataException("Shiboriのバックアップ形式が不正です。バックアップを削除して再作成してください。");
        var paths = backup.Paths.Select(path => new CcdPath(
            DeserializeValue<NativeMethods.DISPLAYCONFIG_PATH_INFO>(path.Info), path.SourceDeviceName, path.PathKey)).ToList();
        return new CcdConfiguration(paths, DeserializeArray<NativeMethods.DISPLAYCONFIG_MODE_INFO>(backup.Modes));
    }

    private DisplayBackup? ReadBackup()
    {
        if (!File.Exists(BackupPath)) return null;
        return JsonSerializer.Deserialize<DisplayBackup>(File.ReadAllText(BackupPath), JsonOptions)
            ?? throw new InvalidDataException("Shiboriのバックアップを読み込めません。");
    }

    private static string BackupPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Shibori", BackupFileName);

    private static string Serialize<T>(T value) where T : struct
    {
        var size = Marshal.SizeOf<T>();
        var bytes = new byte[size];
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try { Marshal.StructureToPtr(value, handle.AddrOfPinnedObject(), false); }
        finally { handle.Free(); }
        return Convert.ToBase64String(bytes);
    }

    private static string Serialize<T>(T[] values) where T : struct
    {
        var size = Marshal.SizeOf<T>();
        var bytes = new byte[size * values.Length];
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            for (var index = 0; index < values.Length; index++)
                Marshal.StructureToPtr(values[index], IntPtr.Add(handle.AddrOfPinnedObject(), index * size), false);
        }
        finally { handle.Free(); }
        return Convert.ToBase64String(bytes);
    }

    private static T DeserializeValue<T>(string encoded) where T : struct
    {
        var bytes = Convert.FromBase64String(encoded);
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try { return Marshal.PtrToStructure<T>(handle.AddrOfPinnedObject()); }
        finally { handle.Free(); }
    }

    private static T[] DeserializeArray<T>(string encoded) where T : struct
    {
        var bytes = Convert.FromBase64String(encoded);
        var size = Marshal.SizeOf<T>();
        if (bytes.Length % size != 0) throw new InvalidDataException("Invalid CCD backup length.");
        var values = new T[bytes.Length / size];
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            for (var index = 0; index < values.Length; index++)
                values[index] = Marshal.PtrToStructure<T>(IntPtr.Add(handle.AddrOfPinnedObject(), index * size));
        }
        finally { handle.Free(); }
        return values;
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private sealed record DisplayBackup(DateTimeOffset CreatedAt, MonitorInfo[] Monitors, BackupPathEntry[] Paths, string Modes);
    private sealed record BackupPathEntry(string PathKey, string SourceDeviceName, string Info);

    private sealed class CcdConfiguration
    {
        public List<CcdPath> Paths { get; }
        public NativeMethods.DISPLAYCONFIG_MODE_INFO[] Modes { get; }

        public CcdConfiguration(List<CcdPath> paths, NativeMethods.DISPLAYCONFIG_MODE_INFO[] modes)
        {
            Paths = paths;
            Modes = modes;
        }

        public CcdConfiguration Clone() => new(
            Paths.Select(path => new CcdPath(path.Info, path.SourceDeviceName, path.PathKey)).ToList(), Modes.ToArray());

        public static CcdConfiguration Read()
        {
            var flags = NativeMethods.QDC_ALL_PATHS | NativeMethods.QDC_VIRTUAL_MODE_AWARE;
            var result = NativeMethods.GetDisplayConfigBufferSizes(flags, out var pathCount, out var modeCount);
            if (result != 0) throw new InvalidOperationException($"表示構成のサイズを取得できませんでした (エラーコード: {result})。");
            var paths = new NativeMethods.DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new NativeMethods.DISPLAYCONFIG_MODE_INFO[modeCount];
            result = NativeMethods.QueryDisplayConfig(flags, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
            if (result != 0) throw new InvalidOperationException($"現在の表示構成を読み込めませんでした (エラーコード: {result})。");

            var resultPaths = new List<CcdPath>();
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
                resultPaths.Add(new CcdPath(path, sourceName.ViewGdiDeviceName ?? string.Empty, PathKey(path.SourceInfo.AdapterId, path.SourceInfo.SourceId)));
            }
            return new CcdConfiguration(resultPaths, modes[..(int)modeCount]);
        }
    }

    private static string PathKey(NativeMethods.LUID adapterId, uint sourceId) => $"{adapterId.HighPart:X8}:{adapterId.LowPart:X8}:{sourceId}";

    private sealed class CcdPath(NativeMethods.DISPLAYCONFIG_PATH_INFO info, string sourceDeviceName, string pathKey)
    {
        public NativeMethods.DISPLAYCONFIG_PATH_INFO Info { get; } = info;
        public string SourceDeviceName { get; } = sourceDeviceName;
        public string PathKey { get; } = pathKey;
    }
}

public sealed record MonitorInfo(int Index, string DeviceName, int Width, int Height, bool IsPrimary, bool IsConnected, string PathKey)
{
    public string Role => IsPrimary ? "メイン" : "サブ";
    public string Resolution => $"{Width} × {Height}";
    public string Status => IsConnected ? "接続中" : "一時停止中";
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

    [DllImport("user32.dll")] public static extern int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);
    [DllImport("user32.dll")] public static extern int QueryDisplayConfig(uint flags, ref uint pathCount, [Out] DISPLAYCONFIG_PATH_INFO[] paths, ref uint modeCount, [Out] DISPLAYCONFIG_MODE_INFO[] modes, IntPtr topologyId);
    [DllImport("user32.dll")] public static extern int SetDisplayConfig(uint pathCount, [In] DISPLAYCONFIG_PATH_INFO[]? paths, uint modeCount, [In] DISPLAYCONFIG_MODE_INFO[]? modes, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME requestPacket);

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
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] public struct DISPLAYCONFIG_SOURCE_DEVICE_NAME { public DISPLAYCONFIG_DEVICE_INFO_HEADER Header; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string? ViewGdiDeviceName; }
}
