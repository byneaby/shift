using ShiftClub.Shared.Contracts.Cases;

namespace ShiftClub.Application.Abstractions;

public interface ICaseService
{
    Task EnsureSeededAsync(CancellationToken cancellationToken = default);

    Task<CasePublicHomeDto> GetPublicHomeAsync(CancellationToken cancellationToken = default);

    /// <summary>Анонимный спин рулетки (без TG / ключей / наград на аккаунт).</summary>
    Task<CasePublicSpinResultDto> SpinPublicAsync(CancellationToken cancellationToken = default);

    Task<CaseAdminDto> GetAdminAsync(CancellationToken cancellationToken = default);

    Task<CaseAdminDto> UpdateAdminAsync(UpdateCaseAdminRequest request, CancellationToken cancellationToken = default);

    Task<CaseAdminPrizeDto> UpsertPrizeAsync(Guid? prizeId, UpsertCasePrizeRequest request, CancellationToken cancellationToken = default);

    Task SetPrizeActiveAsync(Guid prizeId, bool isActive, CancellationToken cancellationToken = default);

    Task<CaseAdminPrizeDto> SetPrizeImageUrlAsync(Guid prizeId, string? imageUrl, CancellationToken cancellationToken = default);

    Task<CaseMyStateDto> GetMyStateAsync(Guid customerId, CancellationToken cancellationToken = default);

    Task<CaseOpenResultDto> OpenAsync(
        Guid customerId,
        CaseOpenRequest request,
        CancellationToken cancellationToken = default);

    Task GrantKeysAsync(
        Guid customerId,
        int keys,
        Shared.Enums.CaseKeyReason reason,
        string? comment,
        string? idempotencyKey,
        Guid? employeeId = null,
        Guid? relatedEntityId = null,
        CancellationToken cancellationToken = default);

    Task<BuyCaseKeysResultDto> BuyKeysAsync(
        BuyCaseKeysRequest request,
        Guid employeeId,
        CancellationToken cancellationToken = default);

    Task TryGrantRegistrationKeyAsync(Guid customerId, CancellationToken cancellationToken = default);

    Task TryGrantDepositKeysAsync(Guid customerId, decimal depositAmount, Guid? receiptId, CancellationToken cancellationToken = default);

    Task TryGrantBirthdayKeyAsync(Guid customerId, int year, CancellationToken cancellationToken = default);

    Task TryGrantPlayHoursKeyAsync(Guid customerId, int sessionMinutes, Guid sessionId, CancellationToken cancellationToken = default);

    Task TryGrantNightPackageKeyAsync(Guid customerId, string? tariffCode, Guid sessionId, CancellationToken cancellationToken = default);

    Task<CaseUserRewardDto> ClaimRewardAsync(
        Guid rewardId,
        Guid employeeId,
        CaseClaimRewardRequest request,
        CancellationToken cancellationToken = default);

    Task<ApplyCaseRewardResultDto> ApplyRewardAsync(
        Guid rewardId,
        Guid employeeId,
        ApplyCaseRewardRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CasePendingClaimDto>> ListPendingClaimsAsync(CancellationToken cancellationToken = default);

    Task<CaseStatsDto> GetStatsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Смотрит pending DISCOUNT без списания. После успешного старта сеанса — ConsumeDiscountRewardAsync.
    /// </summary>
    Task<(decimal Percent, Guid? RewardId)> PeekPendingDiscountAsync(
        Guid customerId,
        CancellationToken cancellationToken = default);

    Task ConsumeDiscountRewardAsync(
        Guid rewardId,
        Guid sessionId,
        CancellationToken cancellationToken = default);
}
