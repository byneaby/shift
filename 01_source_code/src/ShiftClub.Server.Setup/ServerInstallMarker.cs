using System.Text;
using System.Text.Json;

namespace ShiftClub.Server.Setup;

public sealed class ServerInstallMarker
{
    public string InstallDirectory { get; set; } = "";

    public int ListenPort { get; set; }

    public bool BundledPostgres { get; set; }

    public string ClubName { get; set; } = "";

    public string FirewallRuleName { get; set; } = "";

    public static string FilePath(string installDirectory) =>
        Path.Combine(installDirectory, "data", "install.json");

    public static void Write(string installDirectory, ServerInstallMarker marker)
    {
        var path = FilePath(installDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(marker, Json);
        File.WriteAllText(path, json, new UTF8Encoding(false));
    }

    public static ServerInstallMarker? Read(string installDirectory)
    {
        var path = FilePath(installDirectory);
        if (!File.Exists(path))
            return null;
        try
        {
            return JsonSerializer.Deserialize<ServerInstallMarker>(File.ReadAllText(path), Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
}
