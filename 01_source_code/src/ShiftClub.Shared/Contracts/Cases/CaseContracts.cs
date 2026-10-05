using ShiftClub.Shared.Contracts.Cash;
using ShiftClub.Shared.Enums;

namespace ShiftClub.Shared.Contracts.Cases;

public sealed record CaseKeyRuleDto(string Event, int Keys, string? Note);

public sealed record CasePrizePublicDto(
    string PrizeCode,
    string Name,
    string? Description,
    CasePrizeType PrizeType,
    CasePrizeRarity Rarity,
    string? ImageUrl,
    bool RequiresClaim,
    decimal? ChancePercent);

public sealed record CaseCatalogDto(
    string CaseCode,
    string Title,
    string? Description,
    string? EconomicsNote,
    bool IsEnabled,
    int KeyCost,
    bool ShowProbabilitiesToUsers,
    IReadOnlyList<CasePrizePublicDto> Prizes,
    IReadOnlyList<CaseKeyRuleDto> KeyRules);

public sealed record CaseRecentWinDto(
    string PrizeName,
    CasePrizeType PrizeType,
    string? ImageUrl,
    string CustomerDisplay,
    DateTimeOffset OpenedAt);

public sealed record CasePublicHomeDto(
    CaseCatalogDto Catalog,
    IReadOnlyList<CaseRecentWinDto> RecentWins);

public sealed record CaseMyStateDto(
    int KeysBalance,
    CaseCatalogDto Catalog,
    IReadOnlyList<CaseUserRewardDto> PendingRewards,
    IReadOnlyList<CaseOpeningDto> RecentOpenings);

public sealed record CaseOpeningDto(
    Guid Id,
    string PrizeCode,
    string PrizeName,
    CasePrizeType PrizeType,
    CasePrizeRarity Rarity,
    string? ImageUrl,
    string PayloadJson,
    CaseRewardStatus RewardStatus,
    DateTimeOffset OpenedAt);

public sealed record CaseUserRewardDto(
    Guid Id,
    Guid OpeningId,
    string PrizeCode,
    string Name,
    CasePrizeType PrizeType,
    CasePrizeRarity? Rarity,
    string? ImageUrl,
    string PayloadJson,
    CaseRewardStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ClaimedAt);

public sealed record CaseOpenRequest(string? IdempotencyKey, string CaseCode = "SHIFT_CASE");

public sealed record CaseOpenResultDto(
    Guid OpeningId,
    Guid RewardId,
    string PrizeCode,
    string PrizeName,
    CasePrizeType PrizeType,
    CasePrizeRarity Rarity,
    string? ImageUrl,
    string PayloadJson,
    CaseRewardStatus RewardStatus,
    string? ApplyMessage,
    int KeysRemaining,
    IReadOnlyList<string> StripPrizeCodes);

public sealed record CaseGrantKeysRequest(
    Guid CustomerId,
    int Keys,
    string Comment,
    string? IdempotencyKey);

public sealed record BuyCaseKeysRequest(
    Guid CustomerId,
    int Quantity,
    decimal UnitPrice,
    PaymentMethod PaymentMethod,
    string? IdempotencyKey = null,
    string? Comment = null,
    IReadOnlyList<PaymentPartDto>? Payments = null);

public sealed record BuyCaseKeysResultDto(
    Guid ReceiptId,
    string ReceiptNumber,
    int KeysGranted,
    int KeysBalance,
    decimal PaidTotal);

public sealed record CaseClaimRewardRequest(string? Note);

public sealed record ApplyCaseRewardRequest(Guid? ZoneId, string? Note);

public sealed record ApplyCaseRewardResultDto(
    Guid RewardId,
    CaseRewardStatus Status,
    string Message);

public sealed record CaseStatsDto(
    int OpeningsToday,
    int OpeningsTotal,
    int KeysGrantedTotal,
    int PendingClaims,
    IReadOnlyList<CasePrizeStatDto> ByPrize);

public sealed record CasePrizeStatDto(
    string PrizeCode,
    string Name,
    CasePrizeRarity Rarity,
    int WinsTotal,
    int WinsToday);

public sealed record CasePendingClaimDto(
    Guid RewardId,
    Guid CustomerId,
    string CustomerName,
    string CustomerPhone,
    string PrizeCode,
    string PrizeName,
    CasePrizeType PrizeType,
    string? ImageUrl,
    string PayloadJson,
    DateTimeOffset CreatedAt);

/// <summary>Публичный спин без аккаунта / ключей — только показать приз.</summary>
public sealed record CasePublicSpinResultDto(
    string PrizeCode,
    string PrizeName,
    CasePrizeType PrizeType,
    CasePrizeRarity Rarity,
    string? ImageUrl,
    string? Description);

/// <summary>Касса: гость перед открытием SHIFT CASE.</summary>
public sealed record CaseDeskGuestDto(
    Guid CustomerId,
    string DisplayName,
    string Phone,
    int KeysBalance,
    int KeyCost,
    bool CaseEnabled);

/// <summary>Команда на экран акции / второй монитор: показать кейс гостя.</summary>
public sealed record DeskDisplayCommandDto(
    Guid CommandId,
    Guid CustomerId,
    string DisplayName,
    string Phone,
    DateTimeOffset CreatedAt,
    string DisplayToken);

/// <summary>Результат открытия кейса на экране — касса забирает и показывает в /customers.</summary>
public sealed record DeskCaseResultDto(
    Guid CommandId,
    Guid CustomerId,
    string DisplayName,
    Guid RewardId,
    string PrizeCode,
    string PrizeName,
    CasePrizeType PrizeType,
    string PayloadJson,
    CaseRewardStatus RewardStatus,
    string? ImageUrl,
    string? ApplyMessage,
    int KeysRemaining,
    DateTimeOffset OpenedAt);

public sealed record CaseAdminPrizeDto(
    Guid Id,
    string PrizeCode,
    string Name,
    string? Description,
    CasePrizeType PrizeType,
    decimal DropPercent,
    decimal CostEstimateKzt,
    string PayloadJson,
    string? ImageUrl,
    int? DailyLimit,
    int? TotalLimit,
    bool RequiresClaim,
    int SortOrder,
    bool IsActive);

public sealed record CaseAdminDto(
    Guid Id,
    string CaseCode,
    string Title,
    string? Description,
    bool IsEnabled,
    bool ShowProbabilitiesToUsers,
    int KeyCost,
    IReadOnlyList<CaseAdminPrizeDto> Prizes,
    decimal ActiveDropPercentSum);

public sealed record UpdateCaseAdminRequest(
    string Title,
    string? Description,
    bool IsEnabled,
    bool ShowProbabilitiesToUsers);

public sealed record UpsertCasePrizeRequest(
    string PrizeCode,
    string Name,
    string? Description,
    CasePrizeType PrizeType,
    decimal DropPercent,
    decimal CostEstimateKzt,
    string? PayloadJson,
    string? ImageUrl,
    int? DailyLimit,
    int? TotalLimit,
    bool RequiresClaim,
    int SortOrder,
    bool IsActive);
