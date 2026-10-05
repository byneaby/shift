using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShiftClub.Application.Abstractions;
using ShiftClub.Infrastructure.Options;
using ShiftClub.Shared.Contracts.ClientUpdates;

namespace ShiftClub.Infrastructure.Services;

public sealed class ClientUpdateService : IClientUpdateService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly ClientUpdateOptions _options;
    private readonly IHostEnvironment _env;
    private readonly ILogger<ClientUpdateService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ClientUpdateService(
        IOptions<ClientUpdateOptions> options,
        IHostEnvironment env,
        ILogger<ClientUpdateService> logger)
    {
        _options = options.Value;
        _env = env;
        _logger = logger;
    }

    public async Task<ClientUpdateCheckResponse> CheckAsync(
        string? currentVersion,
        CancellationToken cancellationToken = default)
    {
        var current = NormalizeVersion(currentVersion) ?? "0.0.0";
        if (!_options.Enabled)
        {
            return new ClientUpdateCheckResponse(false, current, null, null, null, null, null, null);
        }

        var manifest = await ReadManifestAsync(cancellationToken);
        if (manifest is null)
        {
            return new ClientUpdateCheckResponse(false, current, null, null, null, null, null, null);
        }

        var latest = NormalizeVersion(manifest.Version) ?? manifest.Version;
        var available = CompareSemVer(latest, current) > 0
                        && PackageExists(manifest.PackageFile);

        return new ClientUpdateCheckResponse(
            available,
            current,
            latest,
            available ? manifest.Sha256 : null,
            available ? $"/api/client/updates/package?version={Uri.EscapeDataString(latest)}" : null,
            available ? manifest.ReleaseNotes : null,
            manifest.Channel,
            manifest.PublishedAt);
    }

    public async Task<(Stream Stream, string FileName, string ContentType)?> OpenPackageAsync(
        string? version,
        CancellationToken cancellationToken = default)
    {
        var manifest = await ReadManifestAsync(cancellationToken);
        if (manifest is null)
            return null;

        if (!string.IsNullOrWhiteSpace(version)
            && !string.Equals(NormalizeVersion(version), NormalizeVersion(manifest.Version), StringComparison.OrdinalIgnoreCase))
            return null;

        var path = Path.Combine(GetPackagesDir(), manifest.PackageFile);
        if (!File.Exists(path))
            return null;

        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return (stream, manifest.PackageFile, "application/zip");
    }

    public Task<ClientUpdateManifestDto?> GetCurrentManifestAsync(CancellationToken cancellationToken = default)
        => ReadManifestAsync(cancellationToken);

    public async Task<ClientUpdateManifestDto> PublishAsync(
        Stream packageStream,
        string version,
        string? releaseNotes,
        string? channel,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeVersion(version)
            ?? throw new InvalidOperationException("Некорректная версия. Ожидается semver, например 0.4.0");

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var dir = GetPackagesDir();
            Directory.CreateDirectory(dir);

            var fileName = $"ShiftClub.Client.Shell-{normalized}.zip";
            var packagePath = Path.Combine(dir, fileName);

            await using (var fs = new FileStream(packagePath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await packageStream.CopyToAsync(fs, cancellationToken);
            }

            var sha = await ComputeSha256Async(packagePath, cancellationToken);
            var dto = new ClientUpdateManifestDto(
                normalized,
                string.IsNullOrWhiteSpace(channel) ? "stable" : channel.Trim(),
                fileName,
                sha,
                null,
                releaseNotes,
                DateTimeOffset.UtcNow);

            var manifestPath = Path.Combine(dir, "manifest.json");
            await using (var ms = new FileStream(manifestPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(ms, dto, JsonOptions, cancellationToken);
            }

            _logger.LogInformation("Published client update {Version} ({File})", normalized, fileName);
            return dto;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ClientUpdateManifestDto?> ReadManifestAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(GetPackagesDir(), "manifest.json");
        if (!File.Exists(path))
            return null;

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<ClientUpdateManifestDto>(stream, JsonOptions, cancellationToken);
    }

    private bool PackageExists(string packageFile)
        => File.Exists(Path.Combine(GetPackagesDir(), packageFile));

    private string GetPackagesDir()
    {
        if (Path.IsPathRooted(_options.PackagesPath))
            return _options.PackagesPath;
        return Path.GetFullPath(Path.Combine(_env.ContentRootPath, _options.PackagesPath));
    }

    private static async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(filePath);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string? NormalizeVersion(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var m = Regex.Match(raw.Trim(), @"^(\d+)\.(\d+)\.(\d+)");
        return m.Success ? $"{m.Groups[1].Value}.{m.Groups[2].Value}.{m.Groups[3].Value}" : null;
    }

    /// <summary>Returns &gt;0 if a &gt; b.</summary>
    private static int CompareSemVer(string a, string b)
    {
        var pa = Parse(a);
        var pb = Parse(b);
        for (var i = 0; i < 3; i++)
        {
            var cmp = pa[i].CompareTo(pb[i]);
            if (cmp != 0) return cmp;
        }

        return 0;

        static int[] Parse(string v)
        {
            var parts = v.Split('.', StringSplitOptions.RemoveEmptyEntries);
            return
            [
                parts.Length > 0 && int.TryParse(parts[0], out var x) ? x : 0,
                parts.Length > 1 && int.TryParse(parts[1], out var y) ? y : 0,
                parts.Length > 2 && int.TryParse(parts[2], out var z) ? z : 0
            ];
        }
    }
}
