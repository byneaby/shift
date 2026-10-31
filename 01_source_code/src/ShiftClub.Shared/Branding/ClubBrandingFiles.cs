using System.Text;
using System.Text.Json;
using ShiftClub.Shared.Contracts.Settings;

namespace ShiftClub.Shared.Branding;

public sealed class ClubBrandingFile
{
    public string Name { get; set; } = "";

    public string? LogoFile { get; set; }
}

/// <summary>
/// Club name and logo live in data/branding. The panel, hall screen, and PCs read them
/// from one small response. The file is the source of truth, so a rename does not need a database migration.
/// </summary>
public static class ClubBrandingFiles
{
    public const string FileName = "branding.json";
    public const int MaxLogoBytes = 2_000_000;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static string DirectoryPath(string contentRoot) =>
        Path.Combine(contentRoot, "data", "branding");

    public static string DocumentPath(string contentRoot) =>
        Path.Combine(DirectoryPath(contentRoot), FileName);

    public static bool TryNormalizeName(string? raw, out string name, out string? error)
    {
        name = (raw ?? "").Trim();
        if (name.Length is < 2 or > 48)
        {
            error = "Название клуба — от 2 до 48 символов.";
            return false;
        }

        if (name.Any(c => char.IsControl(c) || c is '"' or '\\' or '/' or '|' or '<' or '>' or ':' or '*' or '?' or '&' or '%'))
        {
            error = "В названии клуба не должно быть кавычек и символов \\ / | < > : * ? & %";
            return false;
        }

        error = null;
        return true;
    }

    public static string? LogoExtension(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" ? ext : null;
    }

    public static ClubBrandingFile Read(string contentRoot)
    {
        var path = DocumentPath(contentRoot);
        if (!File.Exists(path))
            return new ClubBrandingFile();

        try
        {
            var parsed = JsonSerializer.Deserialize<ClubBrandingFile>(File.ReadAllText(path), Json);
            if (parsed is null || !TryNormalizeName(parsed.Name, out var name, out _))
                return new ClubBrandingFile();
            parsed.Name = name;
            parsed.LogoFile = SafeLogoFile(parsed.LogoFile);
            return parsed;
        }
        catch (JsonException)
        {
            return new ClubBrandingFile();
        }
    }

    public static void Write(string contentRoot, string name, string? logoSourcePath)
    {
        if (!TryNormalizeName(name, out var normalized, out var error))
            throw new ArgumentException(error, nameof(name));

        var dir = DirectoryPath(contentRoot);
        Directory.CreateDirectory(dir);
        var current = Read(contentRoot);
        var logoFile = SafeLogoFile(current.LogoFile);

        if (!string.IsNullOrWhiteSpace(logoSourcePath))
        {
            var ext = LogoExtension(logoSourcePath)
                ?? throw new ArgumentException("Логотип — файл png, jpg, webp или gif.", nameof(logoSourcePath));
            var source = new FileInfo(logoSourcePath);
            if (!source.Exists)
                throw new ArgumentException("Файл логотипа не найден.", nameof(logoSourcePath));
            if (source.Length is <= 0 or > MaxLogoBytes)
                throw new ArgumentException("Логотип больше 2 МБ.", nameof(logoSourcePath));

            logoFile = "logo" + ext;
            var dest = Path.Combine(dir, logoFile);
            if (!string.Equals(source.FullName, Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
            {
                foreach (var old in Directory.EnumerateFiles(dir, "logo.*"))
                {
                    if (!string.Equals(Path.GetFullPath(old), source.FullName, StringComparison.OrdinalIgnoreCase))
                        File.Delete(old);
                }
                File.Copy(source.FullName, dest, overwrite: true);
            }
        }

        var document = new ClubBrandingFile { Name = normalized, LogoFile = logoFile };
        File.WriteAllText(DocumentPath(contentRoot), JsonSerializer.Serialize(document, Json), new UTF8Encoding(false));
    }

    public static ClientBrandingDto ToDto(string contentRoot, string? loginBackgroundUrl)
    {
        var brand = Read(contentRoot);
        return new ClientBrandingDto(string.IsNullOrWhiteSpace(loginBackgroundUrl) ? null : loginBackgroundUrl.Trim(), brand.Name, LogoUrl(contentRoot, brand));
    }

    public static string? LogoUrl(string contentRoot, ClubBrandingFile? brand = null)
    {
        brand ??= Read(contentRoot);
        var file = SafeLogoFile(brand.LogoFile);
        if (file is null)
            return null;
        var path = Path.Combine(DirectoryPath(contentRoot), file);
        if (!File.Exists(path))
            return null;
        var version = new DateTimeOffset(File.GetLastWriteTimeUtc(path)).ToUnixTimeSeconds();
        return "/media/branding/" + file + "?v=" + version;
    }

    private static string? SafeLogoFile(string? file)
    {
        if (string.IsNullOrWhiteSpace(file))
            return null;
        var name = Path.GetFileName(file);
        if (!name.StartsWith("logo.", StringComparison.OrdinalIgnoreCase) || LogoExtension(name) is null)
            return null;
        return name;
    }
}
