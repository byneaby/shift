using System.Diagnostics;

namespace ShiftClub.Infrastructure.Networking;

/// <summary>
/// Best-effort wake via local CCBoot/CCBootCloud CLI (<c>CCBoot.exe -wol ClientName</c>).
/// Club diskless hall uses CCBoot; UDP magic packets alone may not reach NICs through LBFO teaming.
/// </summary>
public static class CcBootWake
{
    private static readonly string[] ExeCandidates =
    [
        @"C:\CCBoot\CCBoot.exe",
        @"C:\Program Files\CCBoot\CCBoot.exe",
        @"D:\CCBoot\CCBoot.exe",
    ];

    public static IReadOnlyList<string> BuildClientNameCandidates(
        string? displayName,
        string? windowsName)
    {
        var list = new List<string>();
        void Add(string? s)
        {
            s = (s ?? "").Trim();
            if (s.Length == 0) return;
            if (!list.Contains(s, StringComparer.OrdinalIgnoreCase))
                list.Add(s);
        }

        Add(windowsName);
        Add(displayName);
        if (!string.IsNullOrWhiteSpace(displayName)
            && displayName.All(char.IsDigit))
        {
            Add("PC" + displayName);
            Add("PC-" + displayName);
            Add("PC " + displayName);
        }

        return list;
    }

    /// <summary>Fire-and-forget; never throws. Returns which names were attempted.</summary>
    public static async Task<string?> TryWakeAsync(
        string? displayName,
        string? windowsName,
        CancellationToken cancellationToken = default)
    {
        var exe = ExeCandidates.FirstOrDefault(File.Exists);
        if (exe is null)
            return null;

        var names = BuildClientNameCandidates(displayName, windowsName);
        if (names.Count == 0)
            return null;

        // One attempt with the best name first (WindowsName usually matches CCBoot client name).
        var name = names[0];
        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = "-wol " + QuoteArg(name),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = Path.GetDirectoryName(exe) ?? @"C:\CCBoot",
                }
            };
            if (!proc.Start())
                return null;

            // CCBoot Cloud CLI often hangs on service control — don't block the API.
            _ = Task.Run(async () =>
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                    await proc.WaitForExitAsync(cts.Token);
                }
                catch
                {
                    try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
                }
            }, CancellationToken.None);

            await Task.Delay(50, cancellationToken);
            return name;
        }
        catch
        {
            return null;
        }
    }

    private static string QuoteArg(string value)
        => value.Contains(' ', StringComparison.Ordinal) ? "\"" + value.Replace("\"", "") + "\"" : value;
}
