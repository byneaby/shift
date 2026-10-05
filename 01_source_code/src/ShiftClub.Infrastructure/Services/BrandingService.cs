using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ShiftClub.Application.Abstractions;
using ShiftClub.Domain.Entities;
using ShiftClub.Infrastructure.Persistence;
using ShiftClub.Shared.Contracts.Branding;

namespace ShiftClub.Infrastructure.Services;

/// <summary>
/// Название, логотип и цвет клуба. Читается на каждом экране и в каждом сообщении бота,
/// поэтому держим короткий кэш в памяти — иначе получим запрос в базу на каждый чих.
/// </summary>
public sealed class BrandingCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(2);

    private readonly object _gate = new();
    private BrandingDto? _value;
    private DateTimeOffset _loadedAt;

    public BrandingDto? TryGet()
    {
        lock (_gate)
        {
            if (_value is null || DateTimeOffset.UtcNow - _loadedAt > Ttl)
                return null;
            return _value;
        }
    }

    public void Set(BrandingDto value)
    {
        lock (_gate)
        {
            _value = value;
            _loadedAt = DateTimeOffset.UtcNow;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _value = null;
        }
    }
}

public sealed class BrandingService : IBrandingService
{
    public const string SettingKey = "branding.settings";

    public const string DefaultAccentColor = "#ff6a00";

    /// <summary>Название, под которым клуб работает, если своё ещё не задали.</summary>
    public const string FallbackClubName = "Компьютерный клуб";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly ShiftClubDbContext _db;
    private readonly BrandingCache _cache;

    public BrandingService(ShiftClubDbContext db, BrandingCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<BrandingDto> GetAsync(CancellationToken cancellationToken = default)
    {
        var cached = _cache.TryGet();
        if (cached is not null)
            return cached;

        var raw = await _db.AppSettings.AsNoTracking()
            .Where(s => s.Key == SettingKey && s.BranchId == null)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(cancellationToken);

        Stored? stored = null;
        if (!string.IsNullOrWhiteSpace(raw))
        {
            try
            {
                stored = JsonSerializer.Deserialize<Stored>(raw, JsonOptions);
            }
            catch (JsonException)
            {
                stored = null;
            }
        }

        // Пока клуб ничего не настроил, берём название филиала — оно уже введено
        // при установке, и это заметно лучше, чем показывать чужой бренд.
        var fallbackName = await _db.Branches.AsNoTracking()
            .OrderBy(b => b.CreatedAt)
            .Select(b => b.Name)
            .FirstOrDefaultAsync(cancellationToken);

        var loginBackground = await _db.AppSettings.AsNoTracking()
            .Where(s => s.Key == ClubSettingsService.LoginBackgroundKey && s.BranchId == null)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(cancellationToken);

        var dto = Build(stored, fallbackName, loginBackground);
        _cache.Set(dto);
        return dto;
    }

    public async Task<BrandingDto> UpdateAsync(
        UpdateBrandingRequest request,
        Guid? updatedBy,
        CancellationToken cancellationToken = default)
    {
        var current = await GetAsync(cancellationToken);

        var stored = new Stored
        {
            ClubName = Clean(request.ClubName) ?? current.ClubName,
            ShortName = Clean(request.ShortName) ?? current.ShortName,
            LogoUrl = CleanOrNull(request.LogoUrl, current.LogoUrl),
            AccentColor = NormalizeColor(request.AccentColor) ?? current.AccentColor,
            WebsiteUrl = CleanOrNull(request.WebsiteUrl, current.WebsiteUrl),
            SupportContact = CleanOrNull(request.SupportContact, current.SupportContact),
            TelegramSignature = Clean(request.TelegramSignature) ?? current.TelegramSignature
        };

        if (stored.ClubName.Length > 80)
            throw new InvalidOperationException("Название клуба — не больше 80 символов.");

        if (stored.ShortName.Length > 24)
            throw new InvalidOperationException("Короткое название — не больше 24 символов.");

        var json = JsonSerializer.Serialize(stored, JsonOptions);
        var setting = await _db.AppSettings
            .FirstOrDefaultAsync(s => s.Key == SettingKey && s.BranchId == null, cancellationToken);

        if (setting is null)
        {
            _db.AppSettings.Add(new AppSetting
            {
                Key = SettingKey,
                Value = json,
                Description = "Название, логотип и цвет клуба (белый лейбл)",
                CreatedBy = updatedBy,
                UpdatedBy = updatedBy,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }
        else
        {
            setting.Value = json;
            setting.UpdatedBy = updatedBy;
            setting.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);
        _cache.Clear();

        return await GetAsync(cancellationToken);
    }

    private static BrandingDto Build(Stored? stored, string? fallbackName, string? loginBackground)
    {
        var clubName = Clean(stored?.ClubName) ?? Clean(fallbackName) ?? FallbackClubName;
        var shortName = Clean(stored?.ShortName) ?? ShortenName(clubName);

        return new BrandingDto(
            clubName,
            shortName,
            Clean(stored?.LogoUrl),
            NormalizeColor(stored?.AccentColor) ?? DefaultAccentColor,
            Clean(loginBackground),
            Clean(stored?.WebsiteUrl),
            Clean(stored?.SupportContact),
            Clean(stored?.TelegramSignature) ?? clubName);
    }

    private static string ShortenName(string clubName)
    {
        var first = clubName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? clubName;
        return first.Length <= 24 ? first : first[..24];
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Пустая строка в запросе = «очистить поле», отсутствие поля = «не менять».</summary>
    private static string? CleanOrNull(string? incoming, string? current)
    {
        if (incoming is null)
            return current;
        return string.IsNullOrWhiteSpace(incoming) ? null : incoming.Trim();
    }

    private static string? NormalizeColor(string? value)
    {
        var cleaned = Clean(value);
        if (cleaned is null)
            return null;

        if (!cleaned.StartsWith('#'))
            cleaned = "#" + cleaned;

        var isHex = (cleaned.Length == 7 || cleaned.Length == 4)
                    && cleaned[1..].All(Uri.IsHexDigit);

        return isHex ? cleaned.ToLowerInvariant() : null;
    }

    private sealed class Stored
    {
        public string? ClubName { get; set; }
        public string? ShortName { get; set; }
        public string? LogoUrl { get; set; }
        public string? AccentColor { get; set; }
        public string? WebsiteUrl { get; set; }
        public string? SupportContact { get; set; }
        public string? TelegramSignature { get; set; }
    }
}
