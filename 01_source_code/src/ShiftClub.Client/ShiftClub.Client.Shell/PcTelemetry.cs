using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace ShiftClub.Client.Shell;

/// <summary>Лёгкий снимок нагрузки — только когда касса запросила GetDiagnostics.</summary>
internal static class PcTelemetry
{
    private static PerformanceCounter? _cpuTotal;

    public sealed record Snapshot(
        double? CpuLoadPercent,
        double? RamUsedPercent,
        long? RamUsedMb,
        long? RamTotalMb,
        long? FreeDiskMb,
        double? CpuTempC,
        double? GpuTempC,
        string? GpuName,
        double? GpuLoadPercent,
        string? ForegroundProcess,
        string? ForegroundTitle);

    public static Snapshot Collect()
    {
        double? cpu = null;
        try { cpu = SampleCpuLoadPercent(); } catch { /* ignore */ }

        double? ramPct = null;
        long? ramUsed = null;
        long? ramTotal = null;
        try
        {
            if (TryGetMemory(out var usedMb, out var totalMb, out var load))
            {
                ramUsed = usedMb;
                ramTotal = totalMb;
                ramPct = load;
            }
        }
        catch { /* ignore */ }

        long? freeDisk = null;
        try { freeDisk = GetFreeDiskMb(); } catch { /* ignore */ }

        string? fgName = null;
        string? fgTitle = null;
        try { TryGetForeground(out fgName, out fgTitle); } catch { /* ignore */ }

        double? gpuTemp = null;
        double? gpuLoad = null;
        string? gpuName = null;
        try { TryReadNvidia(out gpuName, out gpuTemp, out gpuLoad); } catch { /* ignore */ }

        double? cpuTemp = null;
        try { cpuTemp = TryReadCpuTempC(); } catch { /* ignore */ }

        return new Snapshot(cpu, ramPct, ramUsed, ramTotal, freeDisk, cpuTemp, gpuTemp, gpuName, gpuLoad, fgName, fgTitle);
    }

    private static double? SampleCpuLoadPercent()
    {
        // PerformanceCounter: first NextValue is always 0 — sample twice.
        try
        {
            _cpuTotal ??= new PerformanceCounter("Processor", "% Processor Time", "_Total", readOnly: true);
            _ = _cpuTotal.NextValue();
            Thread.Sleep(450);
            var v = _cpuTotal.NextValue();
            if (!float.IsNaN(v) && !float.IsInfinity(v))
                return Math.Round(Math.Clamp(v, 0f, 100f), 1);
        }
        catch
        {
            /* fallback below */
        }

        return SampleCpuLoadViaSystemTimes();
    }

    private static double? SampleCpuLoadViaSystemTimes()
    {
        if (!GetSystemTimes(out var idle1, out var kernel1, out var user1))
            return null;
        Thread.Sleep(450);
        if (!GetSystemTimes(out var idle2, out var kernel2, out var user2))
            return null;

        var idle = (ulong)(idle2 - idle1);
        var kernel = (ulong)(kernel2 - kernel1);
        var user = (ulong)(user2 - user1);
        // Kernel includes idle time on Windows.
        var total = kernel + user;
        if (total == 0) return null;
        var busy = total - idle;
        if (busy > total) busy = 0;
        return Math.Round(100.0 * busy / total, 1);
    }

    private static double? TryReadCpuTempC()
    {
        // ACPI thermal zones (best-effort; may be missing on diskless images).
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\WMI",
                "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
            double? best = null;
            foreach (ManagementObject obj in searcher.Get())
            {
                try
                {
                    var raw = Convert.ToDouble(obj["CurrentTemperature"]);
                    // Tenths of Kelvin
                    var c = raw / 10.0 - 273.15;
                    if (c is < 0 or > 125) continue;
                    if (best is null || c > best) best = c;
                }
                catch { /* ignore row */ }
                finally { obj.Dispose(); }
            }
            if (best is not null)
                return Math.Round(best.Value, 0);
        }
        catch { /* ignore */ }

        // ThermalZoneInformation (Win10+)
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Temperature FROM Win32_PerfFormattedData_Counters_ThermalZoneInformation");
            double? best = null;
            foreach (ManagementObject obj in searcher.Get())
            {
                try
                {
                    var c = Convert.ToDouble(obj["Temperature"]);
                    if (c is < 0 or > 125) continue;
                    if (best is null || c > best) best = c;
                }
                catch { /* ignore */ }
                finally { obj.Dispose(); }
            }
            if (best is not null)
                return Math.Round(best.Value, 0);
        }
        catch { /* ignore */ }

        return null;
    }

    private static bool TryGetMemory(out long usedMb, out long totalMb, out double loadPercent)
    {
        usedMb = 0;
        totalMb = 0;
        loadPercent = 0;
        var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref status))
            return false;
        totalMb = (long)(status.ullTotalPhys / (1024 * 1024));
        var avail = (long)(status.ullAvailPhys / (1024 * 1024));
        usedMb = Math.Max(0, totalMb - avail);
        loadPercent = status.dwMemoryLoad;
        return totalMb > 0;
    }

    private static long? GetFreeDiskMb()
    {
        long free = 0;
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (d is { IsReady: true, DriveType: DriveType.Fixed })
                    free += d.AvailableFreeSpace / (1024 * 1024);
            }
            catch { /* ignore */ }
        }
        return free > 0 ? free : null;
    }

    private static void TryGetForeground(out string? processName, out string? title)
    {
        processName = null;
        title = null;
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return;

        var sb = new StringBuilder(512);
        if (GetWindowText(hwnd, sb, sb.Capacity) > 0)
            title = sb.ToString();

        _ = GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0) return;
        try
        {
            using var p = Process.GetProcessById((int)pid);
            processName = p.ProcessName;
            if (string.IsNullOrWhiteSpace(title))
            {
                try { title = string.IsNullOrWhiteSpace(p.MainWindowTitle) ? null : p.MainWindowTitle; }
                catch { /* ignore */ }
            }
        }
        catch { /* ignore */ }
    }

    private static void TryReadNvidia(out string? name, out double? tempC, out double? loadPercent)
    {
        name = null;
        tempC = null;
        loadPercent = null;

        var smi = ResolveNvidiaSmi();
        if (smi is null) return;

        var psi = new ProcessStartInfo
        {
            FileName = smi,
            Arguments = "--query-gpu=name,temperature.gpu,utilization.gpu --format=csv,noheader,nounits",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var proc = Process.Start(psi);
        if (proc is null) return;
        if (!proc.WaitForExit(2500))
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
            return;
        }

        var line = proc.StandardOutput.ReadToEnd().Trim();
        if (string.IsNullOrWhiteSpace(line)) return;
        var first = line.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0];
        var parts = first.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length < 3) return;
        name = parts[0];
        if (double.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var t))
            tempC = t;
        if (double.TryParse(parts[2], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var u))
            loadPercent = u;
    }

    private static string? ResolveNvidiaSmi()
    {
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var candidates = new[]
        {
            "nvidia-smi",
            Path.Combine(pf, "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvidia-smi.exe"),
        };
        foreach (var c in candidates)
        {
            try
            {
                if (c == "nvidia-smi") return c;
                if (File.Exists(c)) return c;
            }
            catch { /* ignore */ }
        }
        return null;
    }

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
    private static extern bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
}
