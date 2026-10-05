namespace ShiftClub.Shared.SignalR;

public static class HubMethods
{
    public const string ComputerStatusChanged = "ComputerStatusChanged";
    public const string ComputerCommand = "ComputerCommand";
    public const string ComputerCommandUpdated = "ComputerCommandUpdated";
    public const string ComputerApproved = "ComputerApproved";
    public const string SessionStarted = "SessionStarted";
    public const string SessionUpdated = "SessionUpdated";
    public const string SessionEnded = "SessionEnded";
    public const string SessionWarning = "SessionWarning";
    public const string OrderCreated = "OrderCreated";
    public const string OrderStatusChanged = "OrderStatusChanged";
    public const string BookingChanged = "BookingChanged";
    public const string ComputerHelpRequested = "ComputerHelpRequested";
    public const string ComputerSecurityAlert = "ComputerSecurityAlert";
    public const string StaffToast = "StaffToast";
}

public static class HubGroups
{
    public static string Branch(Guid branchId) => $"branch:{branchId}";
    public static string Computer(Guid computerId) => $"computer:{computerId}";
    public static string Staff() => "staff";
}
