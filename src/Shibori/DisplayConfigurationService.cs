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
                var savedByTarget = backup?.Monitors?.FirstOrDefault(monitor => string.Equals(monitor.TargetDevicePath, path.TargetDevicePath, StringComparison.OrdinalIgnoreCase));
                saved ??= savedByTarget;
                var position = GetSourceModePosition(path.Info, configuration.Modes);
                return new MonitorInfo(index + 1, path.SourceDeviceName, path.FriendlyName, screen?.Bounds.Width ?? saved?.Width ?? 0,
                    screen?.Bounds.Height ?? saved?.Height ?? 0, position == (0, 0), true, path.PathKey, path.TargetDevicePath);
            }).ToList();

        if (backup?.Monitors is not null)
        {
            var activeKeys = active.Select(monitor => monitor.PathKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var activeTargets = active.Select(monitor => monitor.TargetDevicePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var monitor in backup.Monitors.Where(monitor => !activeKeys.Contains(monitor.PathKey) && !activeTargets.Contains(monitor.TargetDevicePath)))
                active.Add(monitor with { IsPrimary = false, IsConnected = false });
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

            var configuration = CcdConfiguration.Read();
            EnsureBackup(configuration);

            var selectedKeys = selected.Select(monitor => monitor.PathKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var selectedTargets = selected.Select(monitor => monitor.TargetDevicePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var activePaths = configuration.Paths
                .Where(path => (path.Info.Flags & NativeMethods.DISPLAYCONFIG_PATH_ACTIVE) != 0)
                .ToArray();
            var pathsToApply = activePaths
                .Where(path => !selectedKeys.Contains(path.PathKey) && !selectedTargets.Contains(path.TargetDevicePath))
                .Select(path => path.Info)
                .ToArray();

            if (pathsToApply.Length == 0) throw new InvalidOperationException("少なくとも1台のモニターを残してください。");
            if (pathsToApply.Length == activePaths.Length) throw new InvalidOperationException("選択したモニターの表示パスを特定できませんでした。");
            var modes = configuration.Modes.ToArray();
            var origin = pathsToApply.FirstOrDefault(path => GetSourceModePosition(path, modes) == (0, 0));
            if (origin.Equals(default(NativeMethods.DISPLAYCONFIG_PATH_INFO))) origin = pathsToApply[0];
            MoveOrigin(pathsToApply, modes, origin);
            ApplyConfiguration("Pause", pathsToApply, modes);
        }
    }

    public void SetPrimary(MonitorInfo monitor)
    {
        lock (sync)
        {
            var configuration = CcdConfiguration.Read(activeOnly: true);
            var selected = configuration.Paths.FirstOrDefault(path => Matches(path, monitor));
            if (selected is null || (selected.Info.Flags & NativeMethods.DISPLAYCONFIG_PATH_ACTIVE) == 0)
                throw new InvalidOperationException("選択したモニターが現在の表示構成に見つかりません。");
            if (GetSourceModePosition(selected.Info, configuration.Modes) == (0, 0)) return;

            var paths = configuration.Paths
                .Where(path => (path.Info.Flags & NativeMethods.DISPLAYCONFIG_PATH_ACTIVE) != 0)
                .Select(path => path.Info).ToArray();
            var modes = configuration.Modes.ToArray();
            MoveOrigin(paths, modes, selected.Info);
            ApplyConfiguration("SetPrimary", paths, modes);
        }
    }

    public void Restore()
    {
        RestoreInternalStable(null);
    }

    public void Restore(MonitorInfo monitor)
    {
        RestoreInternalStable(monitor);
    }

    private void RestoreInternalStable(MonitorInfo? monitorToRestore)
    {
        lock (sync)
        {
            var backup = backupConfiguration ?? LoadBackupConfiguration()
                ?? throw new InvalidOperationException("復旧用のバックアップがありません。");
            var current = CcdConfiguration.Read();
            var activeKeys = current.Paths.Where(path => (path.Info.Flags & NativeMethods.DISPLAYCONFIG_PATH_ACTIVE) != 0)
                .Select(path => path.PathKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var activeTargets = current.Paths.Where(path => (path.Info.Flags & NativeMethods.DISPLAYCONFIG_PATH_ACTIVE) != 0)
                .Select(path => path.TargetDevicePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var paths = backup.Paths
                .Where(path => (path.Info.Flags & NativeMethods.DISPLAYCONFIG_PATH_ACTIVE) != 0
                    && (monitorToRestore is null || activeKeys.Contains(path.PathKey) || activeTargets.Contains(path.TargetDevicePath)
                        || (monitorToRestore is not null && Matches(path, monitorToRestore))))
                .Select(path => path.Info).ToArray();
            if (paths.Length == 0) throw new InvalidOperationException("復旧対象の表示パスが見つかりません。");
            ApplyConfiguration("Restore", paths, backup.Modes, forceModeEnumeration: true);
            if (monitorToRestore is null || !GetMonitors().Any(monitor => !monitor.IsConnected))
            {
                backupConfiguration = null;
                File.Delete(BackupPath);
            }
        }
    }

    private static void MoveOrigin(NativeMethods.DISPLAYCONFIG_PATH_INFO[] paths,
        NativeMethods.DISPLAYCONFIG_MODE_INFO[] modes, NativeMethods.DISPLAYCONFIG_PATH_INFO originPath)
    {
        var originIndex = GetSourceModeIndex(originPath, modes);
        var originX = modes[originIndex].ModeInfo.SourceMode.PositionX;
        var originY = modes[originIndex].ModeInfo.SourceMode.PositionY;
        var moved = new HashSet<int>();
        foreach (var path in paths)
        {
            var index = GetSourceModeIndex(path, modes);
            if (!moved.Add(index)) continue;
            modes[index].ModeInfo.SourceMode.PositionX -= originX;
            modes[index].ModeInfo.SourceMode.PositionY -= originY;
        }
    }

    private static (int X, int Y) GetSourceModePosition(NativeMethods.DISPLAYCONFIG_PATH_INFO path,
        NativeMethods.DISPLAYCONFIG_MODE_INFO[] modes)
    {
        var source = modes[GetSourceModeIndex(path, modes)].ModeInfo.SourceMode;
        return (source.PositionX, source.PositionY);
    }

    private static int GetSourceModeIndex(NativeMethods.DISPLAYCONFIG_PATH_INFO path,
        NativeMethods.DISPLAYCONFIG_MODE_INFO[] modes)
    {
        var rawIndex = (path.Flags & NativeMethods.DISPLAYCONFIG_PATH_SUPPORT_VIRTUAL_MODE) != 0
            ? path.SourceInfo.SourceModeInfoIdx
            : path.SourceInfo.ModeInfoIdx;
        if (rawIndex == NativeMethods.DISPLAYCONFIG_PATH_MODE_IDX_INVALID || rawIndex >= modes.Length
            || modes[rawIndex].InfoType != NativeMethods.DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE)
            throw new InvalidOperationException("表示モード配列の参照が不正です。");
        return checked((int)rawIndex);
    }

    private static void ApplyConfiguration(string operation, NativeMethods.DISPLAYCONFIG_PATH_INFO[] paths,
        NativeMethods.DISPLAYCONFIG_MODE_INFO[] modes, bool forceModeEnumeration = false)
    {
        var commonFlags = NativeMethods.SDC_USE_SUPPLIED_DISPLAY_CONFIG | NativeMethods.SDC_ALLOW_CHANGES
            | NativeMethods.SDC_VIRTUAL_MODE_AWARE | NativeMethods.SDC_VIRTUAL_REFRESH_RATE_AWARE;
        var summary = string.Join("; ", paths.Select(path =>
        {
            var sourceIndex = GetSourceModeIndex(path, modes);
            var source = modes[sourceIndex].ModeInfo.SourceMode;
            return $"source={path.SourceInfo.SourceId}, flags=0x{path.Flags:X8}, sourceMode={sourceIndex}, "
                + $"targetMode={path.TargetInfo.TargetModeInfoIdx}, desktopMode={path.TargetInfo.DesktopModeInfoIdx}, "
                + $"position=({source.PositionX},{source.PositionY})";
        }));
        AppLogger.Info($"{operation} configuration: paths={paths.Length}, modes={modes.Length}; {summary}");

        var validation = NativeMethods.SetDisplayConfig((uint)paths.Length, paths, (uint)modes.Length, modes,
            NativeMethods.SDC_VALIDATE | commonFlags);
        if (validation != 0)
            throw new InvalidOperationException($"表示構成を適用できません (検証エラーコード: {validation})。");
        var result = NativeMethods.SetDisplayConfig((uint)paths.Length, paths, (uint)modes.Length, modes,
            NativeMethods.SDC_APPLY | commonFlags
                | (forceModeEnumeration ? NativeMethods.SDC_FORCE_MODE_ENUMERATION : 0));
        if (result != 0) throw new InvalidOperationException($"表示構成の変更に失敗しました (エラーコード: {result})。");
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
                backupConfiguration.Paths.Select(path => new BackupPathEntry(path.PathKey, path.SourceDeviceName, path.FriendlyName, path.TargetDevicePath, Serialize(path.Info))).ToArray(),
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
            DeserializeValue<NativeMethods.DISPLAYCONFIG_PATH_INFO>(path.Info), path.SourceDeviceName, path.PathKey, path.FriendlyName, path.TargetDevicePath)).ToList();
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

    private static bool Matches(CcdPath path, MonitorInfo monitor) =>
        string.Equals(path.PathKey, monitor.PathKey, StringComparison.OrdinalIgnoreCase)
        || (!string.IsNullOrWhiteSpace(path.TargetDevicePath)
            && string.Equals(path.TargetDevicePath, monitor.TargetDevicePath, StringComparison.OrdinalIgnoreCase));

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
    private sealed record BackupPathEntry(string PathKey, string SourceDeviceName, string FriendlyName, string TargetDevicePath, string Info);

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
            Paths.Select(path => new CcdPath(path.Info, path.SourceDeviceName, path.PathKey, path.FriendlyName, path.TargetDevicePath)).ToList(), Modes.ToArray());

        public static CcdConfiguration Read(bool activeOnly = false)
        {
            var flags = (activeOnly ? NativeMethods.QDC_ONLY_ACTIVE_PATHS : NativeMethods.QDC_ALL_PATHS)
                | NativeMethods.QDC_VIRTUAL_MODE_AWARE | NativeMethods.QDC_VIRTUAL_REFRESH_RATE_AWARE;
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
                var targetName = new NativeMethods.DISPLAYCONFIG_TARGET_DEVICE_NAME
                {
                    Header = new NativeMethods.DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        Type = NativeMethods.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME,
                        Size = (uint)Marshal.SizeOf<NativeMethods.DISPLAYCONFIG_TARGET_DEVICE_NAME>(),
                        AdapterId = path.TargetInfo.AdapterId,
                        Id = path.TargetInfo.Id
                    }
                };
                NativeMethods.DisplayConfigGetDeviceInfo(ref targetName);
                resultPaths.Add(new CcdPath(path, sourceName.ViewGdiDeviceName ?? string.Empty,
                    PathKey(path.SourceInfo.AdapterId, path.SourceInfo.SourceId),
                    string.IsNullOrWhiteSpace(targetName.MonitorFriendlyDeviceName)
                        ? sourceName.ViewGdiDeviceName ?? string.Empty
                        : targetName.MonitorFriendlyDeviceName,
                    targetName.MonitorDevicePath ?? string.Empty));
            }
            return new CcdConfiguration(resultPaths, modes[..(int)modeCount]);
        }
    }

    private static string PathKey(NativeMethods.LUID adapterId, uint sourceId) => $"{adapterId.HighPart:X8}:{adapterId.LowPart:X8}:{sourceId}";

    private sealed class CcdPath(NativeMethods.DISPLAYCONFIG_PATH_INFO info, string sourceDeviceName, string pathKey, string friendlyName, string targetDevicePath)
    {
        public NativeMethods.DISPLAYCONFIG_PATH_INFO Info { get; } = info;
        public string SourceDeviceName { get; } = sourceDeviceName;
        public string PathKey { get; } = pathKey;
        public string FriendlyName { get; } = friendlyName;
        public string TargetDevicePath { get; } = targetDevicePath;
    }
}

public sealed record MonitorInfo(int Index, string DeviceName, string FriendlyName, int Width, int Height, bool IsPrimary, bool IsConnected, string PathKey, string TargetDevicePath)
{
    public string Role => IsPrimary ? "メイン" : "サブ";
    public string Resolution => $"{Width} × {Height}";
    public string Status => IsConnected ? "接続中" : "一時停止中";
}

internal static class NativeMethods
{
    public const uint DISPLAYCONFIG_PATH_ACTIVE = 0x00000001;
    public const uint DISPLAYCONFIG_PATH_SUPPORT_VIRTUAL_MODE = 0x00000008;
    public const uint DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME = 1;
    public const uint DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME = 2;
    public const uint DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE = 1;
    public const ushort DISPLAYCONFIG_PATH_SOURCE_MODE_IDX_INVALID = 0xFFFF;
    public const uint DISPLAYCONFIG_PATH_MODE_IDX_INVALID = 0xFFFFFFFF;
    public const uint QDC_ALL_PATHS = 1;
    public const uint QDC_ONLY_ACTIVE_PATHS = 2;
    public const uint QDC_VIRTUAL_MODE_AWARE = 0x10;
    public const uint QDC_VIRTUAL_REFRESH_RATE_AWARE = 0x40;
    public const uint SDC_VALIDATE = 0x40;
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
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME requestPacket);

    [StructLayout(LayoutKind.Sequential)] public struct LUID { public uint LowPart; public int HighPart; }
    [StructLayout(LayoutKind.Sequential)] public struct DISPLAYCONFIG_RATIONAL { public uint Numerator; public uint Denominator; }
    [StructLayout(LayoutKind.Explicit, Size = 20)] public struct DISPLAYCONFIG_PATH_SOURCE_INFO
    {
        [FieldOffset(0)] public LUID AdapterId;
        [FieldOffset(8)] public uint SourceId;
        [FieldOffset(12)] public uint ModeInfoIdx;
        [FieldOffset(14)] public ushort SourceModeInfoIdx;
        [FieldOffset(16)] public uint StatusFlags;
    }
    [StructLayout(LayoutKind.Explicit, Size = 48)] public struct DISPLAYCONFIG_PATH_TARGET_INFO
    {
        [FieldOffset(0)] public LUID AdapterId;
        [FieldOffset(8)] public uint Id;
        [FieldOffset(12)] public uint ModeInfoIdx;
        [FieldOffset(12)] public ushort DesktopModeInfoIdx;
        [FieldOffset(14)] public ushort TargetModeInfoIdx;
        [FieldOffset(16)] public uint OutputTechnology;
        [FieldOffset(20)] public uint Rotation;
        [FieldOffset(24)] public uint Scaling;
        [FieldOffset(28)] public DISPLAYCONFIG_RATIONAL RefreshRate;
        [FieldOffset(36)] public uint ScanLineOrdering;
        [FieldOffset(40)] public int TargetAvailable;
        [FieldOffset(44)] public uint StatusFlags;
    }
    [StructLayout(LayoutKind.Sequential)] public struct DISPLAYCONFIG_PATH_INFO { public DISPLAYCONFIG_PATH_SOURCE_INFO SourceInfo; public DISPLAYCONFIG_PATH_TARGET_INFO TargetInfo; public uint Flags; }
    [StructLayout(LayoutKind.Explicit, Size = 48)] public struct DISPLAYCONFIG_VIDEO_SIGNAL_INFO
    {
        [FieldOffset(0)] public ulong PixelRate;
        [FieldOffset(8)] public DISPLAYCONFIG_RATIONAL HSyncFreq;
        [FieldOffset(16)] public DISPLAYCONFIG_RATIONAL VSyncFreq;
        [FieldOffset(24)] public DISPLAYCONFIG_2DREGION ActiveSize;
        [FieldOffset(32)] public DISPLAYCONFIG_2DREGION TotalSize;
        [FieldOffset(40)] public uint VideoStandard;
        [FieldOffset(40)] public uint AdditionalVideoSignalInfo;
        [FieldOffset(44)] public uint ScanLineOrdering;
    }
    [StructLayout(LayoutKind.Sequential)] public struct DISPLAYCONFIG_2DREGION { public uint CX; public uint CY; }
    [StructLayout(LayoutKind.Sequential)] public struct DISPLAYCONFIG_TARGET_MODE { public DISPLAYCONFIG_VIDEO_SIGNAL_INFO TargetVideoSignalInfo; }
    [StructLayout(LayoutKind.Sequential)] public struct DISPLAYCONFIG_SOURCE_MODE { public uint Width; public uint Height; public uint PixelFormat; public int PositionX; public int PositionY; }
    [StructLayout(LayoutKind.Explicit, Size = 48)] public struct DISPLAYCONFIG_MODE_INFO_UNION { [FieldOffset(0)] public DISPLAYCONFIG_TARGET_MODE TargetMode; [FieldOffset(0)] public DISPLAYCONFIG_SOURCE_MODE SourceMode; }
    [StructLayout(LayoutKind.Sequential)] public struct DISPLAYCONFIG_MODE_INFO { public uint InfoType; public uint Id; public LUID AdapterId; public DISPLAYCONFIG_MODE_INFO_UNION ModeInfo; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] public struct DISPLAYCONFIG_DEVICE_INFO_HEADER { public uint Type; public uint Size; public LUID AdapterId; public uint Id; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] public struct DISPLAYCONFIG_SOURCE_DEVICE_NAME { public DISPLAYCONFIG_DEVICE_INFO_HEADER Header; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string? ViewGdiDeviceName; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] public struct DISPLAYCONFIG_TARGET_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER Header;
        public uint Flags;
        public uint OutputTechnology;
        public ushort EdidManufactureId;
        public ushort EdidProductCodeId;
        public uint ConnectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string? MonitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string? MonitorDevicePath;
    }
}
