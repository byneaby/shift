using System.Text.Json;

namespace ShiftClub.Shared.Licensing;

/// <summary>
/// Server and client installers published from the license admin page.
/// Files sit next to the license database so an app republish does not delete them.
/// </summary>
public sealed class InstallerFileStore
{
    public const long MaxBytes = 2L * 1024 * 1024 * 1024;
    public const string ServerSlot = "server";
    public const string ClientSlot = "client";

    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".zip", ".msi"
    };

    private readonly string _root;

    public InstallerFileStore(string directory)
    {
        _root = directory;
        Directory.CreateDirectory(_root);
    }

    public static InstallerFileStore BesideDatabase(string databasePath)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (string.IsNullOrEmpty(folder))
            folder = Directory.GetCurrentDirectory();
        return new InstallerFileStore(Path.Combine(folder, "downloads"));
    }

    public static bool IsSlot(string? slot) =>
        slot is ServerSlot or ClientSlot;

    public InstallerPublished? Read(string slot)
    {
        if (!IsSlot(slot))
            return null;
        var metaPath = MetaPath(slot);
        var binPath = BinPath(slot);
        if (!File.Exists(metaPath) || !File.Exists(binPath))
            return null;
        try
        {
            var meta = JsonSerializer.Deserialize<InstallerMeta>(File.ReadAllText(metaPath));
            if (meta is null || string.IsNullOrWhiteSpace(meta.FileName) || meta.Size <= 0)
                return null;
            var length = new FileInfo(binPath).Length;
            if (length != meta.Size)
                return null;
            return new InstallerPublished(slot, meta.FileName, length, meta.UpdatedAt, binPath);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task<string?> SaveAsync(string slot, string originalName, Stream content, CancellationToken cancellationToken)
    {
        if (!IsSlot(slot))
            return "Неизвестный установщик.";

        var extension = Path.GetExtension(originalName);
        if (!Extensions.Contains(extension))
            return "Нужен файл .exe, .zip или .msi.";

        var safeName = SanitizeFileName(Path.GetFileName(originalName));
        var binPath = BinPath(slot);
        var tempPath = binPath + ".uploading";
        try
        {
            await using (var output = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[1024 * 128];
                long total = 0;
                while (true)
                {
                    var read = await content.ReadAsync(buffer, cancellationToken);
                    if (read == 0)
                        break;
                    total += read;
                    if (total > MaxBytes)
                        return "Файл больше 2 ГБ.";
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }

                if (total == 0)
                    return "Файл пустой.";
            }

            var size = new FileInfo(tempPath).Length;
            File.Move(tempPath, binPath, overwrite: true);
            var meta = new InstallerMeta(safeName, size, DateTimeOffset.UtcNow);
            var json = JsonSerializer.Serialize(meta);
            var metaTemp = MetaPath(slot) + ".uploading";
            await File.WriteAllTextAsync(metaTemp, json, cancellationToken);
            File.Move(metaTemp, MetaPath(slot), overwrite: true);
            return null;
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    public static string SanitizeFileName(string name)
    {
        var file = Path.GetFileName(name.Replace('\\', '/').Trim());
        if (string.IsNullOrWhiteSpace(file) || file is "." or "..")
            return "installer.exe";
        var chars = file.Select(ch => char.IsAsciiLetterOrDigit(ch) || ch is '.' or '-' or '_' ? ch : '_').ToArray();
        var cleaned = new string(chars).Trim('.', ' ');
        return cleaned.Length == 0 ? "installer.exe" : cleaned;
    }

    private string BinPath(string slot) => Path.Combine(_root, slot + ".bin");
    private string MetaPath(string slot) => Path.Combine(_root, slot + ".json");

    private sealed record InstallerMeta(string FileName, long Size, DateTimeOffset UpdatedAt);
}

public sealed record InstallerPublished(string Slot, string FileName, long Size, DateTimeOffset UpdatedAt, string AbsolutePath);
