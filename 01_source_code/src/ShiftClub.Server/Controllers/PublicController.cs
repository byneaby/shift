using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ShiftClub.Application.Abstractions;
using ShiftClub.Domain.Entities;
using ShiftClub.Infrastructure.Persistence;
using ShiftClub.Infrastructure.Services;
using ShiftClub.Infrastructure.SignalR;
using ShiftClub.Shared.Contracts;
using ShiftClub.Shared.Contracts.Public;
using ShiftClub.Shared.Contracts.Cases;
using ShiftClub.Shared.Contracts.Settings;
using ShiftClub.Shared.Enums;
using ShiftClub.Shared.ErrorCodes;
using ShiftClub.Shared.SignalR;
using QRCoder;

namespace ShiftClub.Server.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/public")]
public sealed class PublicController : ControllerBase
{
    private static readonly ConcurrentDictionary<string, DateTimeOffset> DeskCallCooldown = new();
    private static readonly TimeSpan DeskCallMinInterval = TimeSpan.FromSeconds(10);

    private readonly ShiftClubDbContext _db;
    private readonly IClubSettingsService _settings;
    private readonly IFloorMapService _floorMap;
    private readonly ITelegramAuthService _telegramAuth;
    private readonly ICaseService _cases;
    private readonly DeskDisplayStore _deskDisplay;
    private readonly IHubContext<StaffHub> _staffHub;
    private readonly ITelegramAlertSink _telegramAlerts;

    public PublicController(
        ShiftClubDbContext db,
        IClubSettingsService settings,
        IFloorMapService floorMap,
        ITelegramAuthService telegramAuth,
        ICaseService cases,
        DeskDisplayStore deskDisplay,
        IHubContext<StaffHub> staffHub,
        ITelegramAlertSink telegramAlerts)
    {
        _db = db;
        _settings = settings;
        _floorMap = floorMap;
        _telegramAuth = telegramAuth;
        _cases = cases;
        _deskDisplay = deskDisplay;
        _staffHub = staffHub;
        _telegramAlerts = telegramAlerts;
    }

    [HttpGet("club")]
    public async Task<ActionResult<ApiResponse<PublicClubInfoDto>>> Club(CancellationToken cancellationToken)
    {
        var branch = await _db.Branches.AsNoTracking()
            .OrderBy(b => b.CreatedAt)
            .Select(b => new { b.Name, b.Address, b.Phone })
            .FirstOrDefaultAsync(cancellationToken);

        var tg = await _settings.GetTelegramBotStoredAsync(cancellationToken);
        var eng = await _settings.GetEngagementStoredAsync(cancellationToken);
        var web = !string.IsNullOrWhiteSpace(tg.PublicWebAppBaseUrl)
            ? tg.PublicWebAppBaseUrl
            : eng.PublicWebAppBaseUrl;

        var dto = new PublicClubInfoDto(
            branch?.Name ?? "SHIFT CYBER CLUB",
            City: "Алматы",
            branch?.Address,
            branch?.Phone,
            tg.BotUsername,
            web);

        return Ok(ApiResponse<PublicClubInfoDto>.Ok(dto));
    }

    /// <summary>PNG QR для рекламных экранов (только http(s)/tg-ссылки клуба).</summary>
    [HttpGet("qr.png")]
    public IActionResult QrPng(
        [FromQuery(Name = "d")] string? data,
        [FromQuery(Name = "s")] int pixelsPerModule = 16)
    {
        if (string.IsNullOrWhiteSpace(data) || data.Length > 1500)
            return BadRequest(ApiResponse.Fail(CommonErrorCodes.ValidationFailed, "Некорректные данные QR"));

        var payload = data.Trim();
        if (!IsAllowedPromoQrPayload(payload))
            return BadRequest(ApiResponse.Fail(CommonErrorCodes.ValidationFailed, "Ссылка не разрешена для QR"));

        var ppm = Math.Clamp(pixelsPerModule, 6, 28);
        using var gen = new QRCodeGenerator();
        using var qrData = gen.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(qrData);
        Response.Headers.CacheControl = "public, max-age=3600";
        return File(png.GetGraphic(ppm), "image/png");
    }

    private static bool IsAllowedPromoQrPayload(string payload)
    {
        if (!Uri.TryCreate(payload, UriKind.Absolute, out var uri))
            return false;
        if (uri.Scheme is not ("http" or "https" or "tg"))
            return false;

        var host = uri.Host;
        if (host.Equals("t.me", StringComparison.OrdinalIgnoreCase)
            || host.Equals("telegram.me", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".t.me", StringComparison.OrdinalIgnoreCase))
            return true;
        if (host.Equals("go.2gis.com", StringComparison.OrdinalIgnoreCase)
            || host.Equals("2gis.kz", StringComparison.OrdinalIgnoreCase)
            || host.Equals("2gis.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".2gis.kz", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".2gis.com", StringComparison.OrdinalIgnoreCase))
            return true;
        if (host.Equals("shift-club.kz", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".shift-club.kz", StringComparison.OrdinalIgnoreCase))
            return true;
        if (host.Equals("tg.shift-club.kz", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    /// <summary>Публичная карта зала: занятость ПК без персональных данных гостей.</summary>
    [HttpGet("floor")]
    [ResponseCache(Duration = 5, Location = ResponseCacheLocation.Any, NoStore = false)]
    public async Task<ActionResult<ApiResponse<PublicFloorMapDto>>> Floor(CancellationToken cancellationToken)
    {
        var map = await _floorMap.GetMapAsync(null, cancellationToken);
        var pcs = map.Computers
            .Where(c => c.IsApproved)
            .Select(c => new PublicFloorPcDto(
                c.Id,
                string.IsNullOrWhiteSpace(c.DisplayName) ? c.WindowsName : c.DisplayName!,
                c.Occupancy,
                c.ZoneName,
                c.ZoneColorHex,
                c.GridCol,
                c.GridRow))
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        static bool IsBusy(string o) =>
            o.Equals("Busy", StringComparison.OrdinalIgnoreCase)
            || o.Equals("Paused", StringComparison.OrdinalIgnoreCase);

        var free = pcs.Count(p => p.Occupancy.Equals("Free", StringComparison.OrdinalIgnoreCase));
        var busy = pcs.Count(p => IsBusy(p.Occupancy));
        var reserved = pcs.Count(p => p.Occupancy.Equals("Reserved", StringComparison.OrdinalIgnoreCase));
        var offline = pcs.Count(p => p.Occupancy.Equals("Offline", StringComparison.OrdinalIgnoreCase));
        var maintenance = pcs.Count(p =>
            p.Occupancy.Equals("Maintenance", StringComparison.OrdinalIgnoreCase)
            || p.Occupancy.Equals("Updating", StringComparison.OrdinalIgnoreCase)
            || p.Occupancy.Equals("Setup", StringComparison.OrdinalIgnoreCase));

        var elements = map.Elements
            .Where(e => e.IsVisible && e.Kind != FloorMapElementKind.Wall)
            .Select(e => new PublicFloorElementDto(
                e.Kind.ToString(),
                e.Label,
                e.GridCol,
                e.GridRow,
                Math.Max(1, e.ColSpan),
                Math.Max(1, e.RowSpan),
                e.ColorHex ?? e.FillHex))
            .ToList();

        var dto = new PublicFloorMapDto(
            map.GridCols,
            map.GridRows,
            map.BackgroundHex,
            new PublicFloorCountsDto(free, busy, reserved, offline, maintenance, pcs.Count),
            pcs,
            elements);

        return Ok(ApiResponse<PublicFloorMapDto>.Ok(dto));
    }

    [HttpGet("loyalty")]
    public async Task<ActionResult<ApiResponse<PublicLoyaltyInfoDto>>> Loyalty(CancellationToken cancellationToken)
    {
        var branch = await _db.Branches.AsNoTracking()
            .OrderBy(b => b.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var levels = await _db.LoyaltyLevels.AsNoTracking()
            .Where(l => l.IsActive && (branch == null || l.BranchId == branch.Id))
            .OrderBy(l => l.SortOrder)
            .ThenBy(l => l.MinSpent)
            .Select(l => new PublicLoyaltyLevelDto(
                l.Name,
                l.Code,
                l.MinSpent,
                l.BonusPercent,
                l.TimeDiscountPercent))
            .ToListAsync(cancellationToken);

        var eng = await _settings.GetEngagementStoredAsync(cancellationToken);
        var tiers = (eng.StreakTiers ?? [])
            .OrderBy(t => t.Days)
            .Select(t => new PublicStreakTierDto(t.Days, t.BonusAmount, t.BarRewards))
            .ToList();
        var depositTiers = eng.DepositBonusEnabled
            ? (eng.DepositBonusTiers ?? [])
                .Where(t => t.MinAmount > 0 && t.BonusAmount > 0)
                .OrderBy(t => t.MinAmount)
                .Select(t => new PublicDepositBonusTierDto(t.MinAmount, t.BonusAmount))
                .ToList()
            : [];

        var dto = new PublicLoyaltyInfoDto(
            branch?.Name ?? "SHIFT Club",
            eng.Enabled,
            levels,
            tiers,
            eng.BirthdayBonusAmount,
            eng.BirthdayTimeBankMinutes,
            eng.DepositBonusEnabled,
            depositTiers);

        return Ok(ApiResponse<PublicLoyaltyInfoDto>.Ok(dto));
    }

    /// <summary>Прайс для TV-стойки: активные тарифы по зонам + кальян из каталога.</summary>
    [HttpGet("price")]
    [ResponseCache(Duration = 15, Location = ResponseCacheLocation.Any, NoStore = false)]
    public async Task<ActionResult<ApiResponse<PublicPriceBoardDto>>> Price(CancellationToken cancellationToken)
    {
        var branchId = await _db.Branches.AsNoTracking()
            .OrderBy(b => b.CreatedAt)
            .Select(b => b.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var zones = await _db.Zones.AsNoTracking()
            .Where(z => z.BranchId == branchId && z.IsActive)
            .OrderBy(z => z.SortOrder)
            .ThenBy(z => z.Name)
            .ToListAsync(cancellationToken);

        var tariffs = await _db.Tariffs.AsNoTracking()
            .Where(t => t.BranchId == branchId && t.IsActive)
            .OrderBy(t => t.SortOrder)
            .ThenBy(t => t.Name)
            .ToListAsync(cancellationToken);

        var zoneOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["STD"] = 0,
            ["STANDARD"] = 0,
            ["BOOT"] = 1,
            ["BOOTCAMP"] = 1,
            ["VIP"] = 2,
            ["PS5"] = 3,
            ["PS"] = 3
        };

        var colorFallback = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["STD"] = "#ff6a00",
            ["STANDARD"] = "#ff6a00",
            ["BOOT"] = "#17a3ff",
            ["BOOTCAMP"] = "#17a3ff",
            ["VIP"] = "#ffc021",
            ["PS5"] = "#2e75ff",
            ["PS"] = "#2e75ff"
        };

        static string DisplayZoneName(string code, string name)
        {
            var c = (code ?? "").Trim().ToUpperInvariant();
            return c switch
            {
                "STD" or "STANDARD" => "STANDARD",
                "BOOT" or "BOOTCAMP" => "BOOTCAMP",
                "VIP" => "VIP",
                "PS5" or "PS" => "PS5",
                _ => string.IsNullOrWhiteSpace(name) ? c : name.ToUpperInvariant()
            };
        }

        static string? RowKey(Tariff t)
        {
            var code = (t.Code ?? "").ToUpperInvariant();
            var name = (t.Name ?? "").ToLowerInvariant();
            if (code.Contains("_HOUR") || code.EndsWith("HOUR") || t.Kind == TariffKind.Hourly
                || name is "1 час" or "час" || name.Contains("1 час"))
                return "hour";
            if (code.Contains("2P1") || code.Contains("2+1") || name.Contains("2+1") || name.Contains("2 + 1")
                || t.FixedDurationMinutes == 180)
                return "combo21";
            if (code.Contains("3P2") || code.Contains("3+2") || name.Contains("3+2") || name.Contains("3 + 2")
                || t.FixedDurationMinutes == 300)
                return "combo32";
            if (code.Contains("_DAY") || code.EndsWith("DAY") || name.Contains("день") || name.Contains("днев"))
                return "day";
            if (code.Contains("_NIGHT") || code.EndsWith("NIGHT") || name.Contains("ночь") || name.Contains("ночн"))
                return "night";
            return null;
        }

        static (string Label, string Icon, string DefaultNote) RowMeta(string key) => key switch
        {
            "hour" => ("Час", "i-clock", ""),
            "combo21" => ("2 + 1", "i-gift", "2 часа · третий в подарок"),
            "combo32" => ("3 + 2", "i-gift", "3 часа · +2 в подарок"),
            "day" => ("День", "i-sun", ""),
            "night" => ("Ночь", "i-moon", ""),
            _ => (key, "i-info", "")
        };

        static decimal PriceOf(Tariff t) =>
            t.Kind == TariffKind.Hourly
                ? t.PricePerHour
                : (t.FixedPrice ?? t.PricePerHour);

        var marketing = await _settings.GetMarketingPromoStoredAsync(cancellationToken);
        PublicPricePromoDto? promoDto = null;
        if (MarketingPromoMath.IsActive(marketing, DateTimeOffset.UtcNow))
        {
            promoDto = new PublicPricePromoDto(
                true,
                marketing.Percent,
                marketing.Label,
                marketing.Title,
                marketing.EndsAt);
        }

        PublicPriceRowDto MakeRow(string key, string label, string note, string icon, decimal listPrice)
        {
            if (promoDto is null || key is "hour")
                return new PublicPriceRowDto(key, label, note, icon, listPrice);
            var sale = MarketingPromoMath.Apply(listPrice, promoDto.Percent);
            return new PublicPriceRowDto(key, label, note, icon, sale, listPrice);
        }

        int dayFrom = 12, dayTo = 18, nightFrom = 23, nightTo = 8;
        var sampleDay = tariffs.FirstOrDefault(t => RowKey(t) == "day" && t.AvailableFrom is not null);
        var sampleNight = tariffs.FirstOrDefault(t => RowKey(t) == "night" && t.AvailableFrom is not null);
        if (sampleDay?.AvailableFrom is { } df) dayFrom = df.Hours;
        if (sampleDay?.AvailableTo is { } dt) dayTo = dt.Hours;
        if (sampleNight?.AvailableFrom is { } nf) nightFrom = nf.Hours;
        if (sampleNight?.AvailableTo is { } nt) nightTo = nt.Hours;

        var dayLabel = $"{dayFrom:00}:00 — {dayTo:00}:00";
        var nightLabel = $"{nightFrom:00}:00 — {nightTo:00}:00";

        var rowOrder = new[] { "hour", "combo21", "combo32", "day", "night" };
        var zoneDtos = new List<PublicPriceZoneDto>();

        foreach (var zone in zones
                     .OrderBy(z => zoneOrder.GetValueOrDefault(z.Code, 50))
                     .ThenBy(z => z.SortOrder))
        {
            var zTariffs = tariffs.Where(t => t.ZoneId == zone.Id).ToList();
            if (zTariffs.Count == 0) continue;

            var byKey = new Dictionary<string, Tariff>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in zTariffs)
            {
                var key = RowKey(t);
                if (key is null) continue;
                if (!byKey.ContainsKey(key))
                    byKey[key] = t;
            }

            if (byKey.Count == 0) continue;

            var rows = new List<PublicPriceRowDto>();
            foreach (var key in rowOrder)
            {
                if (!byKey.TryGetValue(key, out var t)) continue;
                var (label, icon, defNote) = RowMeta(key);
                var note = key switch
                {
                    "day" => dayLabel,
                    "night" => nightLabel,
                    "combo21" or "combo32" => string.IsNullOrWhiteSpace(t.Description) ? defNote : t.Description!,
                    _ => t.Description ?? defNote
                };
                rows.Add(MakeRow(key, label, note ?? "", icon, PriceOf(t)));
            }

            if (rows.Count == 0) continue;

            var color = string.IsNullOrWhiteSpace(zone.ColorHex)
                ? colorFallback.GetValueOrDefault(zone.Code, "#ff6a00")
                : zone.ColorHex!;
            zoneDtos.Add(new PublicPriceZoneDto(
                zone.Id.ToString("N")[..8],
                zone.Code,
                DisplayZoneName(zone.Code, zone.Name),
                color,
                rows));
        }

        // Тарифы без зоны (например PS5) — отдельная карточка, если есть.
        var orphan = tariffs.Where(t => t.ZoneId is null).ToList();
        if (orphan.Count > 0)
        {
            var byKey = new Dictionary<string, Tariff>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in orphan)
            {
                var key = RowKey(t);
                if (key is null) continue;
                if (!byKey.ContainsKey(key)) byKey[key] = t;
            }
            if (byKey.Count > 0)
            {
                var rows = rowOrder
                    .Where(byKey.ContainsKey)
                    .Select(key =>
                    {
                        var t = byKey[key];
                        var (label, icon, defNote) = RowMeta(key);
                        var note = key switch
                        {
                            "day" => dayLabel,
                            "night" => nightLabel,
                            _ => string.IsNullOrWhiteSpace(t.Description) ? defNote : t.Description!
                        };
                        return MakeRow(key, label, note, icon, PriceOf(t));
                    })
                    .ToList();
                zoneDtos.Add(new PublicPriceZoneDto("orphan", "EXTRA", "ДРУГОЕ", "#9aa4b2", rows));
            }
        }

        PublicPriceExtrasDto? extras = null;
        var hookah = await _db.Products.AsNoTracking()
            .Where(p => p.BranchId == branchId && p.IsActive && p.Category.Code == "HOOKAH")
            .OrderBy(p => p.Sku)
            .Select(p => new { p.Sku, p.Name, p.SalePrice, p.Notes })
            .ToListAsync(cancellationToken);

        if (hookah.Count > 0)
        {
            static (string Label, string Note) HookahMeta(string sku, string name, string? notes)
            {
                var s = sku.ToUpperInvariant();
                if (s.Contains("LIGHT") || name.Contains("лайт", StringComparison.OrdinalIgnoreCase))
                    return ("Лайт", string.IsNullOrWhiteSpace(notes) ? "мягкий микс" : notes!);
                if (s.Contains("HARD") || name.Contains("хард", StringComparison.OrdinalIgnoreCase))
                    return ("Хард", string.IsNullOrWhiteSpace(notes) ? "крепкий микс" : notes!);
                if (s.Contains("BOWL") || name.Contains("чаш", StringComparison.OrdinalIgnoreCase))
                    return ("Замена чаши", notes ?? "");
                return (name, notes ?? "");
            }

            extras = new PublicPriceExtrasDto(
                "КАЛЬЯН",
                hookah.Select(p =>
                {
                    var (label, note) = HookahMeta(p.Sku, p.Name, p.Notes);
                    return new PublicPriceExtraItemDto(p.Sku, label, note, p.SalePrice);
                }).ToList());
        }

        var eng = await _settings.GetEngagementStoredAsync(cancellationToken);
        PublicPriceDepositBonusDto? depositDto = null;
        if (eng.DepositBonusEnabled)
        {
            var tiers = (eng.DepositBonusTiers ?? [])
                .Where(t => t.MinAmount > 0 && t.BonusAmount > 0)
                .OrderBy(t => t.MinAmount)
                .Select(t => new PublicDepositBonusTierDto(t.MinAmount, t.BonusAmount))
                .ToList();
            if (tiers.Count > 0)
                depositDto = new PublicPriceDepositBonusDto(true, tiers);
        }

        var dto = new PublicPriceBoardDto(
            "T",
            zoneDtos,
            extras,
            new PublicPriceWindowsDto(dayFrom, dayTo, nightFrom, nightTo, dayLabel, nightLabel),
            promoDto,
            depositDto);

        return Ok(ApiResponse<PublicPriceBoardDto>.Ok(dto));
    }

    /// <summary>Меню бара для TV-стойки /price/bar: активные категории и товары.</summary>
    [HttpGet("bar")]
    [ResponseCache(Duration = 15, Location = ResponseCacheLocation.Any, NoStore = false)]
    public async Task<ActionResult<ApiResponse<PublicBarMenuDto>>> Bar(CancellationToken cancellationToken)
    {
        var branchId = await _db.Branches.AsNoTracking()
            .OrderBy(b => b.CreatedAt)
            .Select(b => b.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var categories = await _db.ProductCategories.AsNoTracking()
            .Where(c => c.BranchId == branchId && c.IsActive)
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .ToListAsync(cancellationToken);

        var products = await _db.Products.AsNoTracking()
            .Where(p => p.BranchId == branchId && p.IsActive)
            .OrderBy(p => p.Name)
            .Select(p => new
            {
                p.Id,
                p.CategoryId,
                p.Sku,
                p.Name,
                p.Notes,
                p.SalePrice,
                p.ImageUrl
            })
            .ToListAsync(cancellationToken);

        static string CategoryColor(string code) => (code ?? "").Trim().ToUpperInvariant() switch
        {
            "DRINKS" or "DRINK" => "#17a3ff",
            "ENERGY" or "ENERGIES" => "#ff6a00",
            "SNACKS" or "SNACK" => "#ffc021",
            "HOOKAH" => "#a78bfa",
            _ => "#9aa4b2"
        };

        static string DisplayCategoryName(string code, string name)
        {
            var c = (code ?? "").Trim().ToUpperInvariant();
            return c switch
            {
                "DRINKS" or "DRINK" => "НАПИТКИ",
                "ENERGY" or "ENERGIES" => "ЭНЕРГЕТИКИ",
                "SNACKS" or "SNACK" => "СНЕКИ",
                "HOOKAH" => "КАЛЬЯН",
                _ => string.IsNullOrWhiteSpace(name) ? c : name.ToUpperInvariant()
            };
        }

        var byCat = products.ToLookup(p => p.CategoryId);
        var categoryDtos = categories
            .Select(c =>
            {
                var items = byCat[c.Id]
                    .Select(p => new PublicBarItemDto(
                        p.Id.ToString("N")[..8],
                        p.Sku,
                        p.Name,
                        p.Notes ?? "",
                        p.SalePrice,
                        p.ImageUrl))
                    .ToList();
                return new PublicBarCategoryDto(
                    c.Id.ToString("N")[..8],
                    c.Code,
                    DisplayCategoryName(c.Code, c.Name),
                    CategoryColor(c.Code),
                    items);
            })
            .Where(c => c.Items.Count > 0)
            .ToList();

        var dto = new PublicBarMenuDto("T", categoryDtos);
        return Ok(ApiResponse<PublicBarMenuDto>.Ok(dto));
    }

    [HttpGet("case")]
    public async Task<ActionResult<ApiResponse<CasePublicHomeDto>>> CaseHome(CancellationToken cancellationToken)
    {
        try
        {
            var dto = await _cases.GetPublicHomeAsync(cancellationToken);
            return Ok(ApiResponse<CasePublicHomeDto>.Ok(dto));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<CasePublicHomeDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpPost("case/spin")]
    public ActionResult<ApiResponse<CasePublicSpinResultDto>> CaseSpin()
    {
        return BadRequest(ApiResponse<CasePublicSpinResultDto>.Fail(
            CommonErrorCodes.ValidationFailed,
            "Кейс открывается только на кассе для гостя с ключом."));
    }

    /// <summary>Экран акции (другой ПК): есть ли команда показать кейс.</summary>
    [HttpGet("desk-display")]
    [ResponseCache(NoStore = true, Duration = 0, Location = ResponseCacheLocation.None)]
    public ActionResult<ApiResponse<DeskDisplayCommandDto?>> DeskDisplay()
    {
        Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        Response.Headers.Pragma = "no-cache";
        var cmd = _deskDisplay.Peek(TimeSpan.FromMinutes(3));
        return Ok(ApiResponse<DeskDisplayCommandDto?>.Ok(cmd));
    }

    /// <summary>Экран акции: данные гостя по одноразовому токену с кассы (без логина сотрудника).</summary>
    [HttpGet("desk-case/{token}")]
    [ResponseCache(NoStore = true, Duration = 0, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<ApiResponse<CaseDeskGuestDto>>> DeskCaseGuest(
        string token,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        var cmd = _deskDisplay.TryGetByToken(token, TimeSpan.FromMinutes(5));
        if (cmd is null)
        {
            return BadRequest(ApiResponse<CaseDeskGuestDto>.Fail(
                CommonErrorCodes.ValidationFailed,
                "Сессия экрана устарела. На кассе снова нажми «Открыть кейс»."));
        }

        try
        {
            var state = await _cases.GetMyStateAsync(cmd.CustomerId, cancellationToken);
            return Ok(ApiResponse<CaseDeskGuestDto>.Ok(new CaseDeskGuestDto(
                cmd.CustomerId,
                cmd.DisplayName,
                cmd.Phone,
                state.KeysBalance,
                state.Catalog.KeyCost,
                state.Catalog.IsEnabled)));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<CaseDeskGuestDto>.Fail(CommonErrorCodes.NotFound, ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<CaseDeskGuestDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    /// <summary>Экран акции: открыть кейс по токену с кассы (один раз).</summary>
    [HttpPost("desk-case/{token}/open")]
    public async Task<ActionResult<ApiResponse<CaseOpenResultDto>>> DeskCaseOpen(
        string token,
        CancellationToken cancellationToken)
    {
        var cmd = _deskDisplay.TryGetByToken(token, TimeSpan.FromMinutes(5));
        if (cmd is null)
        {
            return BadRequest(ApiResponse<CaseOpenResultDto>.Fail(
                CommonErrorCodes.ValidationFailed,
                "Токен недействителен или устарел. На кассе снова нажми «Открыть кейс»."));
        }

        try
        {
            var dto = await _cases.OpenAsync(
                cmd.CustomerId,
                new CaseOpenRequest($"desk-display-{cmd.CommandId:N}"),
                cancellationToken);
            _deskDisplay.PublishResult(cmd, dto);
            return Ok(ApiResponse<CaseOpenResultDto>.Ok(dto));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse<CaseOpenResultDto>.Fail(CommonErrorCodes.NotFound, ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<CaseOpenResultDto>.Fail(CommonErrorCodes.ValidationFailed, ex.Message));
        }
    }

    [HttpPost("desk-display/ack")]
    public ActionResult<ApiResponse> DeskDisplayAck([FromQuery] Guid? commandId = null)
    {
        _deskDisplay.Clear(commandId);
        return Ok(ApiResponse.Ok("Ок"));
    }

    /// <summary>
    /// Публичная форма на /promo/upgrade отключена: аккаунт только через Telegram (антиабьюз).
    /// </summary>
    [HttpPost("campaign/register-open-case")]
    public ActionResult<ApiResponse<CampaignRegisterResultDto>> CampaignRegisterOpenCase()
    {
        return BadRequest(ApiResponse<CampaignRegisterResultDto>.Fail(
            CommonErrorCodes.ValidationFailed,
            "Аккаунт создаётся только через Telegram-бот SHIFT. Открой бота с этой страницы и зарегистрируйся там."));
    }

    /// <summary>Киоск у кассы: вызов администратора → Telegram + toast в панели.</summary>
    [HttpPost("call-admin")]
    public async Task<ActionResult<ApiResponse<object>>> CallAdminFromDesk(
        [FromBody] PublicDeskCallAdminRequest? request,
        CancellationToken cancellationToken)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var now = DateTimeOffset.UtcNow;
        if (DeskCallCooldown.TryGetValue(ip, out var last) && now - last < DeskCallMinInterval)
        {
            var wait = (int)Math.Ceiling((DeskCallMinInterval - (now - last)).TotalSeconds);
            return BadRequest(ApiResponse<object>.Fail(
                CommonErrorCodes.ValidationFailed,
                $"Подождите {wait} сек. перед следующим вызовом."));
        }

        DeskCallCooldown[ip] = now;

        const string place = "Касса";
        var note = string.IsNullOrWhiteSpace(request?.Message)
            ? "Гость у стойки зовёт администратора"
            : request!.Message!.Trim();
        if (note.Length > 200)
            note = note[..200];

        await _staffHub.Clients.Group(HubGroups.Staff()).SendAsync(
            HubMethods.ComputerHelpRequested,
            new
            {
                computerId = (Guid?)null,
                computerName = place,
                message = note,
                at = now
            },
            cancellationToken);

        await _staffHub.Clients.Group(HubGroups.Staff()).SendAsync(
            HubMethods.StaffToast,
            new
            {
                kind = "help",
                title = "Вызов администратора",
                body = $"{place}: {note}",
                sound = true,
                sticky = true,
                computerId = (Guid?)null,
                at = now
            },
            cancellationToken);

        await _telegramAlerts.PublishAsync(
            new StaffAlertMessage(
                "help",
                "Вызов администратора",
                $"{place}: {note}",
                ComputerId: null,
                ComputerName: place),
            cancellationToken);

        return Ok(ApiResponse<object>.Ok(new { sent = true, cooldownSeconds = (int)DeskCallMinInterval.TotalSeconds }));
    }
}

public sealed record PublicDeskCallAdminRequest(string? Message = null);
