namespace ShiftClub.Shared.Contracts.Settings;

public sealed record MarketingPromoSettingsDto(
    bool Enabled,
    decimal Percent,
    string Label,
    string? Title,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    bool ApplyToTariffs,
    bool IsActiveNow);

public sealed record UpdateMarketingPromoRequest(
    bool Enabled,
    decimal Percent,
    string Label,
    string? Title,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    bool ApplyToTariffs);

/// <summary>Stored JSON: app_settings key marketing.promo</summary>
public sealed class MarketingPromoStoredSettings
{
    public bool Enabled { get; set; } = true;
    /// <summary>Скидка в процентах (50 = −50%).</summary>
    public decimal Percent { get; set; } = 50;
    /// <summary>Короткий бейдж: «1 месяц −50%».</summary>
    public string Label { get; set; } = "1 месяц −50%";
    public string? Title { get; set; } = "Акция";
    public DateTimeOffset? StartsAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }
    public bool ApplyToTariffs { get; set; } = true;
}

public static class MarketingPromoMath
{
    public static bool IsActive(MarketingPromoStoredSettings? s, DateTimeOffset now)
    {
        if (s is null || !s.Enabled || s.Percent <= 0 || !s.ApplyToTariffs)
            return false;
        if (s.StartsAt is { } from && now < from)
            return false;
        if (s.EndsAt is { } to && now > to)
            return false;
        return true;
    }

    public static decimal ClampPercent(decimal percent) =>
        Math.Clamp(percent, 0m, 90m);

    /// <summary>Цена со скидкой (₸, 2 знака).</summary>
    public static decimal Apply(decimal amount, decimal percent)
    {
        if (amount <= 0 || percent <= 0)
            return amount;
        var p = ClampPercent(percent);
        var sale = amount * (1m - p / 100m);
        return Math.Round(sale, 2, MidpointRounding.AwayFromZero);
    }

    public static decimal DiscountAmount(decimal amount, decimal percent) =>
        Math.Max(0, amount - Apply(amount, percent));
}
