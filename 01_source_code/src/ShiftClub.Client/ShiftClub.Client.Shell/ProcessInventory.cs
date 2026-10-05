using System.Diagnostics;
using System.IO;
using System.Text.Json;
using ShiftClub.Shared.Contracts.Computers;

namespace ShiftClub.Client.Shell;

internal static class ProcessInventory
{
    private static readonly HashSet<string> ProtectedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Idle", "System", "Registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass",
        "svchost", "fontdrvhost", "dwm", "Memory Compression", "Secure System",
        "ShiftClub.Client.Shell", "ShiftClub.Client.Service", "ShiftClub.Client.Updater"
    };

    public static ComputerProcessSnapshotDto Capture()
    {
        var list = new List<ComputerProcessDto>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                var name = string.IsNullOrWhiteSpace(p.ProcessName) ? "?" : p.ProcessName;
                long memMb = 0;
                try { memMb = Math.Max(0, p.WorkingSet64 / (1024 * 1024)); }
                catch { /* access denied */ }

                string? title = null;
                try
                {
                    if (p.MainWindowHandle != IntPtr.Zero)
                        title = string.IsNullOrWhiteSpace(p.MainWindowTitle) ? null : p.MainWindowTitle;
                }
                catch { /* ignore */ }

                var canKill = !ProtectedNames.Contains(name)
                              && p.Id > 4
                              && p.Id != Environment.ProcessId;

                list.Add(new ComputerProcessDto(p.Id, name, memMb, title, canKill));
            }
            catch
            {
                /* ignore inaccessible */
            }
            finally
            {
                try { p.Dispose(); } catch { /* ignore */ }
            }
        }

        var telemetry = PcTelemetry.Collect();
        return new ComputerProcessSnapshotDto(
            DateTimeOffset.UtcNow,
            list
                .OrderByDescending(x => x.MemoryMb)
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            telemetry.CpuLoadPercent,
            telemetry.RamUsedPercent,
            telemetry.RamUsedMb,
            telemetry.RamTotalMb,
            telemetry.FreeDiskMb,
            telemetry.CpuTempC,
            telemetry.GpuTempC,
            telemetry.GpuName,
            telemetry.GpuLoadPercent,
            telemetry.ForegroundProcess,
            telemetry.ForegroundTitle);
    }

    public static string CaptureJson() =>
        JsonSerializer.Serialize(Capture(), ShellJsonOptions.Instance);

    public static void Kill(int? pid, string? name)
    {
        if (pid is > 0)
        {
            using var p = Process.GetProcessById(pid.Value);
            EnsureCanKill(p.ProcessName, p.Id);
            p.Kill(entireProcessTree: true);
            return;
        }

        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Укажите pid или имя процесса");

        var procs = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(name.Trim()));
        if (procs.Length == 0)
            throw new InvalidOperationException($"Процесс «{name}» не найден");

        Exception? last = null;
        var killed = 0;
        foreach (var p in procs)
        {
            try
            {
                EnsureCanKill(p.ProcessName, p.Id);
                p.Kill(entireProcessTree: true);
                killed++;
            }
            catch (Exception ex)
            {
                last = ex;
            }
            finally
            {
                p.Dispose();
            }
        }

        if (killed == 0)
            throw last ?? new InvalidOperationException("Не удалось завершить процесс");
    }

    private static void EnsureCanKill(string processName, int pid)
    {
        if (pid == Environment.ProcessId || ProtectedNames.Contains(processName))
            throw new InvalidOperationException($"Процесс «{processName}» защищён");
    }
}

internal static class ShellJsonOptions
{
    public static readonly JsonSerializerOptions Instance = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
