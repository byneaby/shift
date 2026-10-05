namespace ShiftClub.Infrastructure.Options;

public sealed class KaspiPosOptions
{
    public const string SectionName = "KaspiPos";

    /// <summary>Enable integration when Host is set (or stored in app_settings).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Static LAN IP of Smart POS, e.g. 192.168.1.50</summary>
    public string? Host { get; set; }

    public string TerminalId { get; set; } = "32555483";

    public string RegisterName { get; set; } = "ShiftClub";

    public bool OwnCheque { get; set; } = true;

    public bool SkipSslValidation { get; set; } = true;

    public int PollIntervalMs { get; set; } = 1000;

    public int PaymentTimeoutSeconds { get; set; } = 180;
}
