#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;

namespace CodeCraftOptimizer.Services
{
    /// <summary>
    /// Métricas del sistema capturadas en un instante.
    /// </summary>
    public sealed class SystemMetrics
    {
        public double CpuPercent { get; init; }
        public string CpuName { get; init; } = "";

        public long RamUsedBytes { get; init; }
        public long RamTotalBytes { get; init; }
        public double RamPercent { get; init; }

        public long DiskUsedBytes { get; init; }
        public long DiskTotalBytes { get; init; }
        public long DiskFreeBytes { get; init; }
        public double DiskPercent { get; init; }
        public string DiskLetter { get; init; } = "C:";

        public string DiskType { get; init; } = "—";
        public string DiskModel { get; init; } = "";

        // ── Formatters ──
        public string RamUsedGB => FormatGB(RamUsedBytes);
        public string RamTotalGB => FormatGB(RamTotalBytes);
        public string DiskUsedGB => FormatGB(DiskUsedBytes);
        public string DiskTotalGB => FormatGB(DiskTotalBytes);
        public string DiskFreeGB => FormatGB(DiskFreeBytes);

        private static string FormatGB(long bytes)
        {
            double gb = bytes / (1024.0 * 1024 * 1024);
            return gb >= 100 ? $"{gb:F0}" : $"{gb:F1}";
        }
    }

    /// <summary>
    /// Servicio de monitoreo del sistema en tiempo real.
    /// Captura CPU, RAM, disco y tipo de disco con bajo impacto de rendimiento.
    /// </summary>
    public sealed class SystemMonitorService : IDisposable
    {
        // ─── P/Invoke: CPU Times ───
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemTimes(
            out long idleTime, out long kernelTime, out long userTime);

        // ─── P/Invoke: Memory Status ───
        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
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
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        // ─── State ───
        private Timer? _timer;
        private long _prevIdle, _prevKernel, _prevUser;
        private bool _firstPoll = true;
        private string _cpuName = "";
        private string _diskType = "—";
        private string _diskModel = "";
        private bool _staticInfoLoaded;

        /// <summary>
        /// Se dispara en cada ciclo de actualización con las métricas actuales.
        /// </summary>
        public event Action<SystemMetrics>? OnMetricsUpdated;

        /// <summary>
        /// Inicia el monitoreo con el intervalo especificado.
        /// </summary>
        public void Start(int intervalMs = 1500)
        {
            // Cargar info estática en background (CPU name, disk type)
            if (!_staticInfoLoaded)
            {
                ThreadPool.QueueUserWorkItem(_ => LoadStaticInfo());
            }

            // Inicializar lecturas previas de CPU
            GetSystemTimes(out _prevIdle, out _prevKernel, out _prevUser);

            _timer = new Timer(Poll, null, 500, intervalMs);
        }

        /// <summary>
        /// Detiene el monitoreo.
        /// </summary>
        public void Stop()
        {
            _timer?.Change(Timeout.Infinite, Timeout.Infinite);
        }

        public void Dispose()
        {
            _timer?.Dispose();
            _timer = null;
        }

        // ═══════════════════════════════════════════════════════════════
        //  POLLING
        // ═══════════════════════════════════════════════════════════════

        private void Poll(object? state)
        {
            try
            {
                var metrics = new SystemMetrics
                {
                    CpuPercent = GetCpuUsage(),
                    CpuName = _cpuName,
                    RamUsedBytes = GetRamUsed(out long ramTotal, out double ramPct),
                    RamTotalBytes = ramTotal,
                    RamPercent = ramPct,
                    DiskUsedBytes = GetDiskUsed(out long diskTotal, out long diskFree, out double diskPct),
                    DiskTotalBytes = diskTotal,
                    DiskFreeBytes = diskFree,
                    DiskPercent = diskPct,
                    DiskLetter = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\",
                    DiskType = _diskType,
                    DiskModel = _diskModel
                };

                OnMetricsUpdated?.Invoke(metrics);
            }
            catch
            {
                // Silently handle polling errors to keep the timer alive
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  CPU
        // ═══════════════════════════════════════════════════════════════

        private double GetCpuUsage()
        {
            if (!GetSystemTimes(out long idle, out long kernel, out long user))
                return 0;

            if (_firstPoll)
            {
                _prevIdle = idle;
                _prevKernel = kernel;
                _prevUser = user;
                _firstPoll = false;
                return 0;
            }

            long idleDelta = idle - _prevIdle;
            long kernelDelta = kernel - _prevKernel;
            long userDelta = user - _prevUser;

            _prevIdle = idle;
            _prevKernel = kernel;
            _prevUser = user;

            long totalDelta = kernelDelta + userDelta;
            if (totalDelta == 0) return 0;

            // kernel time includes idle time
            double cpuPct = (1.0 - (double)idleDelta / totalDelta) * 100.0;
            return Math.Clamp(cpuPct, 0, 100);
        }

        // ═══════════════════════════════════════════════════════════════
        //  RAM
        // ═══════════════════════════════════════════════════════════════

        private long GetRamUsed(out long total, out double percent)
        {
            var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };

            if (GlobalMemoryStatusEx(ref mem))
            {
                total = (long)mem.ullTotalPhys;
                long used = total - (long)mem.ullAvailPhys;
                percent = total > 0 ? (double)used / total * 100.0 : 0;
                return used;
            }

            total = 0;
            percent = 0;
            return 0;
        }

        // ═══════════════════════════════════════════════════════════════
        //  DISK
        // ═══════════════════════════════════════════════════════════════

        private long GetDiskUsed(out long total, out long free, out double percent)
        {
            try
            {
                string systemRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
                var drive = new DriveInfo(systemRoot);

                if (drive.IsReady)
                {
                    total = drive.TotalSize;
                    free = drive.AvailableFreeSpace;
                    long used = total - free;
                    percent = total > 0 ? (double)used / total * 100.0 : 0;
                    return used;
                }
            }
            catch { }

            total = 0;
            free = 0;
            percent = 0;
            return 0;
        }

        // ═══════════════════════════════════════════════════════════════
        //  STATIC INFO (loaded once at startup)
        // ═══════════════════════════════════════════════════════════════

        private void LoadStaticInfo()
        {
            try
            {
                // CPU Name from Registry (fastest method)
                _cpuName = Microsoft.Win32.Registry.GetValue(
                    @"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0",
                    "ProcessorNameString", "")?.ToString()?.Trim() ?? "CPU";

                // Disk type via WMI (MSFT_PhysicalDisk)
                DetectDiskType();

                _staticInfoLoaded = true;
            }
            catch
            {
                _cpuName = "CPU";
                _diskType = "—";
            }
        }

        private void DetectDiskType()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    @"\\.\root\Microsoft\Windows\Storage",
                    "SELECT MediaType, FriendlyName FROM MSFT_PhysicalDisk");

                foreach (ManagementObject disk in searcher.Get())
                {
                    var mediaType = disk["MediaType"];
                    var name = disk["FriendlyName"]?.ToString() ?? "";

                    if (mediaType != null)
                    {
                        int type = Convert.ToInt32(mediaType);
                        // 0 = Unspecified, 3 = HDD, 4 = SSD, 5 = SCM
                        _diskType = type switch
                        {
                            3 => "HDD",
                            4 => "SSD",
                            5 => "NVMe",
                            _ => "SSD" // Default modern assumption
                        };
                    }

                    if (!string.IsNullOrWhiteSpace(name))
                        _diskModel = name;

                    break; // Use first physical disk
                }
            }
            catch
            {
                // Fallback: try Win32_DiskDrive
                try
                {
                    using var searcher = new ManagementObjectSearcher(
                        "SELECT Model, MediaType FROM Win32_DiskDrive WHERE Index=0");

                    foreach (ManagementObject disk in searcher.Get())
                    {
                        _diskModel = disk["Model"]?.ToString() ?? "";
                        string media = disk["MediaType"]?.ToString() ?? "";

                        // Heuristic: check model name for SSD/NVMe indicators
                        string combined = (_diskModel + " " + media).ToUpperInvariant();
                        if (combined.Contains("SSD") || combined.Contains("NVME") ||
                            combined.Contains("SOLID"))
                            _diskType = "SSD";
                        else if (combined.Contains("HDD") || combined.Contains("FIXED"))
                            _diskType = "HDD";
                        else
                            _diskType = "SSD";

                        break;
                    }
                }
                catch
                {
                    _diskType = "—";
                }
            }
        }
    }
}
