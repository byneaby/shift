using ShiftClub.Shared.Enums;

namespace ShiftClub.Domain.Entities;

public class CaseDefinition : Common.Entity
{
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? EconomicsNote { get; set; }
    public decimal ExpectedCostKzt { get; set; }
    public string? LimitsJson { get; set; }
    public bool ShowProbabilitiesToUsers { get; set; }
    public bool IsEnabled { get; set; } = true;
    public int KeyCost { get; set; } = 1;

    public ICollection<CasePrize> Prizes { get; set; } = new List<CasePrize>();
}

public class CasePrize : Common.Entity
{
    public Guid CaseDefinitionId { get; set; }
    public CaseDefinition CaseDefinition { get; set; } = null!;

    public string PrizeCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public CasePrizeType PrizeType { get; set; }
    public CasePrizeRarity Rarity { get; set; }
    public int Weight { get; set; }
    public decimal CostEstimateKzt { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public string? ImageUrl { get; set; }
    public int? DailyLimit { get; set; }
    public int? TotalLimit { get; set; }
    public bool RequiresClaim { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class CaseKeyLedger : Common.Entity
{
    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    public int Delta { get; set; }
    public int BalanceAfter { get; set; }
    public CaseKeyReason Reason { get; set; }
    public string? IdempotencyKey { get; set; }
    public string? Comment { get; set; }
    public Guid? EmployeeId { get; set; }
    public Guid? RelatedEntityId { get; set; }
}

public class CaseOpening : Common.Entity
{
    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    public Guid CaseDefinitionId { get; set; }
    public CaseDefinition CaseDefinition { get; set; } = null!;

    public Guid CasePrizeId { get; set; }
    public CasePrize CasePrize { get; set; } = null!;

    public string PrizeCodeSnapshot { get; set; } = string.Empty;
    public string PrizeNameSnapshot { get; set; } = string.Empty;
    public CasePrizeRarity RaritySnapshot { get; set; }
    public CasePrizeType PrizeTypeSnapshot { get; set; }
    public string PayloadJsonSnapshot { get; set; } = "{}";
    public string? ImageUrlSnapshot { get; set; }

    public int KeyCost { get; set; } = 1;
    public int WeightRoll { get; set; }
    public int WeightTotal { get; set; }
    /// <summary>Календарный день клуба (Asia/Almaty) yyyy-MM-dd для дневных лимитов.</summary>
    public string ClubDayKey { get; set; } = string.Empty;
    public string? IdempotencyKey { get; set; }

    public CaseUserReward? Reward { get; set; }
}

public class CaseUserReward : Common.Entity
{
    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    public Guid CaseOpeningId { get; set; }
    public CaseOpening CaseOpening { get; set; } = null!;

    public Guid CasePrizeId { get; set; }
    public string PrizeCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public CasePrizeType PrizeType { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public string? ImageUrl { get; set; }

    public CaseRewardStatus Status { get; set; } = CaseRewardStatus.Pending;
    public DateTimeOffset? AppliedAt { get; set; }
    public DateTimeOffset? ClaimedAt { get; set; }
    public Guid? ClaimedByEmployeeId { get; set; }
    public string? ClaimNote { get; set; }
}
