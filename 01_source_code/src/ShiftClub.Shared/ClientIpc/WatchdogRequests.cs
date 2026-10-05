using System.IO;

namespace ShiftClub.Shared.ClientIpc;

/// <summary>
/// Простой IPC: Shell кладёт request-файл, служба ShiftClubClient (SYSTEM) выполняет и пишет ack.
/// </summary>
public static class WatchdogRequests
{
    public const string RestartNvContainer = "restart-nv-container";
    public const string ReloadService = "reload-service";

    public static string RequestsDirectory
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "ShiftClub",
                "Client",
                "requests");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string RequestPath(string name) =>
        Path.Combine(RequestsDirectory, name + ".request");

    public static string AckPath(string name) =>
        Path.Combine(RequestsDirectory, name + ".ack");

    public static void WriteRequest(string name)
    {
        var req = RequestPath(name);
        var ack = AckPath(name);
        try { if (File.Exists(ack)) File.Delete(ack); } catch { /* ignore */ }
        File.WriteAllText(req, DateTimeOffset.UtcNow.ToString("O"));
    }

    /// <summary>Ждёт ack от SYSTEM-службы. Возвращает true, если ack появился.</summary>
    public static bool WaitForAck(string name, TimeSpan timeout)
    {
        var ack = AckPath(name);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            try
            {
                if (File.Exists(ack))
                {
                    try { File.Delete(ack); } catch { /* ignore */ }
                    return true;
                }
            }
            catch { /* ignore */ }

            Thread.Sleep(150);
        }

        return false;
    }

    /// <summary>Служба: если есть request — удаляет его и возвращает true.</summary>
    public static bool TryTakeRequest(string name)
    {
        var req = RequestPath(name);
        try
        {
            if (!File.Exists(req))
                return false;
            File.Delete(req);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void WriteAck(string name, string detail = "ok")
    {
        try
        {
            File.WriteAllText(AckPath(name), detail + "\n" + DateTimeOffset.UtcNow.ToString("O"));
        }
        catch { /* ignore */ }
    }
}
