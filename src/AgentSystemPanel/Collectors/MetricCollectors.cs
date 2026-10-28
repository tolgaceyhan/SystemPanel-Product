using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using SystemPanel.Contracts.Agents;

namespace AgentSystemPanel.Collectors;

public interface IMetricCollector
{
    /// <summary>Yapılandırmadaki ad (cpu, memory, disk).</summary>
    string Name { get; }

    /// <summary>Ölçümü isteğe yazar. Ölçüm yapılamazsa sessizce atlar (null bırakır).</summary>
    void Collect(AgentMetricsRequest request);
}

/// <summary>
/// CPU kullanımı. PerformanceCounter tek örnek olarak tutulur: ilk okuma 0 döner, sonraki okumalar
/// iki ölçüm arasındaki ortalamadır (eski sürüm her ölçümde 1 sn Thread.Sleep yapıyordu).
/// </summary>
public sealed class CpuCollector : IMetricCollector, IDisposable
{
    private readonly PerformanceCounter? _counter;

    public CpuCollector()
    {
        if (OperatingSystem.IsWindows())
            _counter = CreateCounter();
    }

    [SupportedOSPlatform("windows")]
    private static PerformanceCounter? CreateCounter()
    {
        try
        {
            var c = new PerformanceCounter("Processor", "% Processor Time", "_Total", readOnly: true);
            c.NextValue(); // ısındırma
            return c;
        }
        catch (Exception)
        {
            return null; // sayaç erişim izni yoksa (ör. Performance Monitor Users üyeliği) CPU raporlanmaz
        }
    }

    public string Name => "cpu";

    public void Collect(AgentMetricsRequest request)
    {
        if (_counter == null || !OperatingSystem.IsWindows()) return;
        try
        {
            request.Cpu = new CpuMetric { UsagePercent = Math.Clamp(Read(_counter), 0, 100) };
        }
        catch (Exception)
        {
            // geçici sayaç hatası: bu turda CPU yok
        }
    }

    [SupportedOSPlatform("windows")]
    private static double Read(PerformanceCounter c) => c.NextValue();

    public void Dispose()
    {
        if (OperatingSystem.IsWindows()) _counter?.Dispose();
    }
}

/// <summary>Fiziksel bellek kullanımı (Windows: GlobalMemoryStatusEx; Linux: /proc/meminfo).</summary>
public sealed class MemoryCollector : IMetricCollector
{
    public string Name => "memory";

    public void Collect(AgentMetricsRequest request)
    {
        if (OperatingSystem.IsWindows())
        {
            var status = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            if (GlobalMemoryStatusEx(ref status) && status.ullTotalPhys > 0)
            {
                request.Memory = Build((long)status.ullTotalPhys, (long)status.ullAvailPhys);
            }
            return;
        }

        if (File.Exists("/proc/meminfo"))
        {
            var (total, available) = ParseMemInfo(File.ReadAllLines("/proc/meminfo"));
            if (total > 0) request.Memory = Build(total, available);
        }
    }

    internal static MemoryMetric Build(long total, long available)
        => new()
        {
            TotalBytes = total,
            AvailableBytes = available,
            UsagePercent = Math.Round((total - available) * 100.0 / total, 1),
        };

    internal static (long Total, long Available) ParseMemInfo(IEnumerable<string> lines)
    {
        long total = 0, available = 0;
        foreach (var line in lines)
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !long.TryParse(parts[1], out var kb)) continue;
            if (parts[0] == "MemTotal:") total = kb * 1024;
            else if (parts[0] == "MemAvailable:") available = kb * 1024;
        }
        return (total, available);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);
}

/// <summary>Tüm hazır sabit diskler (eski sürüm yalnızca ilk diski raporluyordu).</summary>
public sealed class DiskCollector : IMetricCollector
{
    public string Name => "disk";

    public void Collect(AgentMetricsRequest request)
    {
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady || drive.DriveType != DriveType.Fixed || drive.TotalSize <= 0)
                    continue;

                var name = OperatingSystem.IsWindows() ? drive.Name.TrimEnd('\\') : drive.Name;
                request.Disks.Add(new DiskMetric
                {
                    Name = name.Length > 32 ? name[..32] : name,
                    TotalBytes = drive.TotalSize,
                    FreeBytes = drive.TotalFreeSpace,
                    UsagePercent = Math.Round((drive.TotalSize - drive.TotalFreeSpace) * 100.0 / drive.TotalSize, 1),
                });
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            if (request.Disks.Count >= AgentApi.MaxDisks) break;
        }
    }
}
