namespace ShiftClub.Client.Shell;

internal static class ShellBrand
{
    public static string Name { get; private set; } = "";

    public static string Display => string.IsNullOrWhiteSpace(Name) ? "Клиент" : Name;

    public static event Action? Changed;

    public static void Set(string? name)
    {
        var next = (name ?? "").Trim();
        if (next == Name)
            return;
        Name = next;
        Changed?.Invoke();
    }
}
