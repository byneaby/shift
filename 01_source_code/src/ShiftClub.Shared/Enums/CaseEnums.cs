namespace ShiftClub.Shared.Enums;

public enum CasePrizeType
{
    Time = 1,
    Balance = 2,
    BarItem = 3,
    Discount = 4,
    Service = 5,
    Custom = 9
}

public enum CasePrizeRarity
{
    Common = 1,
    Uncommon = 2,
    Rare = 3,
    Epic = 4,
    Legendary = 5
}

public enum CaseRewardStatus
{
    Pending = 1,
    Applied = 2,
    Claimed = 3,
    Expired = 4,
    Cancelled = 5
}

public enum CaseKeyReason
{
    Registration = 1,
    FirstDepositGe2000 = 2,
    DepositGe5000 = 3,
    BuyNightPackage = 4,
    PlayHours15 = 5,
    Birthday = 6,
    StaffGrant = 7,
    OpenSpend = 8,
    Adjustment = 9,
    Purchase = 10
}
