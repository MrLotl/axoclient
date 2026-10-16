using System.Runtime.InteropServices;

namespace AxoClient.Core.Common;

/// <summary>Arbeitsspeicher und Speicherplatz des PCs (für Start-Pipeline, Diagnose und RAM-Empfehlung).</summary>
public static partial class SystemInfo
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    /// <summary>(Gesamt, frei) in MB. Fällt auf GC-Werte zurück, falls die Abfrage scheitert.</summary>
    public static (long TotalMb, long FreeMb) Memory()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (OperatingSystem.IsWindows() && GlobalMemoryStatusEx(ref status))
            return ((long)(status.TotalPhys / 1024 / 1024), (long)(status.AvailPhys / 1024 / 1024));
        var info = GC.GetGCMemoryInfo();
        return (info.TotalAvailableMemoryBytes / 1024 / 1024, info.TotalAvailableMemoryBytes / 1024 / 1024 / 2);
    }

    public static long FreeDiskBytes(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            return root == null ? long.MaxValue : new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return long.MaxValue;
        }
    }

    /// <summary>
    /// Empfohlener RAM für eine Instanz: Vanilla 2 GB, mit Mods mehr, nie mehr als die Hälfte des PCs.
    /// </summary>
    public static int RecommendedMemoryMb(int modCount, long totalMb)
    {
        var wanted = modCount switch
        {
            0 => 2048,
            < 30 => 4096,
            < 100 => 6144,
            < 200 => 8192,
            _ => 10240
        };
        var cap = (int)Math.Max(2048, totalMb / 2);
        return Math.Min(wanted, cap) / 512 * 512;
    }

    /// <summary>Minimum, unter dem es mit dieser Mod-Anzahl ruckelt oder abstürzt.</summary>
    public static int MinimumMemoryMb(int modCount) => modCount switch
    {
        0 => 1024,
        < 30 => 2048,
        < 100 => 3072,
        _ => 4096
    };

    public static string WindowsVersion => Environment.OSVersion.VersionString;
}
