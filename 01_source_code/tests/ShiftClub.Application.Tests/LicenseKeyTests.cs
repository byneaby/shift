using ShiftClub.Application.Licensing;
using ShiftClub.Shared.Contracts.Licensing;
using ShiftClub.Shared.Licensing;

namespace ShiftClub.Application.Tests;

public class LicenseKeyTests
{
    private static readonly (string Private, string Public) Keys = LicenseKeyCodec.GenerateKeyPair();

    private static LicensePayload Payload(
        int maxPc = 40,
        int days = 30,
        int graceDays = 14,
        IReadOnlyList<string>? features = null) =>
        new(
            V: 1,
            ClubId: Guid.NewGuid(),
            ClubName: "Nexus Arena",
            Plan: "care",
            MaxComputers: maxPc,
            IssuedAt: DateTimeOffset.UtcNow,
            ExpiresAt: DateTimeOffset.UtcNow.AddDays(days),
            GraceDays: graceDays,
            Features: features ?? LicenseFeatures.All);

    [Fact]
    public void Issued_key_passes_verification_and_round_trips_payload()
    {
        var payload = Payload(maxPc: 25, days: 45);
        var key = LicenseKeyCodec.Issue(payload, Keys.Private);

        var parsed = LicenseKeyCodec.Parse(key, Keys.Public);

        Assert.True(parsed.Ok);
        Assert.NotNull(parsed.Payload);
        Assert.Equal(payload.ClubId, parsed.Payload!.ClubId);
        Assert.Equal("Nexus Arena", parsed.Payload.ClubName);
        Assert.Equal(25, parsed.Payload.MaxComputers);
        Assert.Equal(payload.ExpiresAt.ToUnixTimeSeconds(), parsed.Payload.ExpiresAt.ToUnixTimeSeconds());
        Assert.Equal(LicenseFeatures.All, parsed.Payload.Features);
    }

    [Fact]
    public void Key_signed_by_another_vendor_is_rejected()
    {
        var other = LicenseKeyCodec.GenerateKeyPair();
        var key = LicenseKeyCodec.Issue(Payload(), other.PrivateKeyBase64);

        var parsed = LicenseKeyCodec.Parse(key, Keys.Public);

        Assert.False(parsed.Ok);
        Assert.Null(parsed.Payload);
    }

    [Fact]
    public void Tampered_payload_is_rejected()
    {
        // Клуб правит срок/лимит в середине ключа — подпись перестаёт сходиться.
        var key = LicenseKeyCodec.Issue(Payload(maxPc: 10), Keys.Private);
        var parts = key.Split('.');
        var tamperedBody = parts[1][..^4] + (parts[1][^4..] == "AAAA" ? "BBBB" : "AAAA");
        var tampered = $"{parts[0]}.{tamperedBody}.{parts[2]}";

        var parsed = LicenseKeyCodec.Parse(tampered, Keys.Public);

        Assert.False(parsed.Ok);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("не-ключ")]
    [InlineData("SHIFT1.only-two-parts")]
    [InlineData("SHIFT9.aaaa.bbbb")]
    public void Garbage_input_is_rejected_without_throwing(string input)
    {
        var parsed = LicenseKeyCodec.Parse(input, Keys.Public);

        Assert.False(parsed.Ok);
        Assert.NotNull(parsed.Error);
    }

    [Fact]
    public void Missing_public_key_is_rejected_rather_than_trusted()
    {
        var key = LicenseKeyCodec.Issue(Payload(), Keys.Private);

        var parsed = LicenseKeyCodec.Parse(key, "");

        Assert.False(parsed.Ok);
    }

    [Fact]
    public void State_is_active_before_expiry_grace_after_and_expired_past_grace()
    {
        var payload = Payload(days: 10, graceDays: 14);

        Assert.Equal(
            LicenseState.Active,
            LicenseKeyCodec.ResolveState(payload, payload.ExpiresAt.AddDays(-1)));
        Assert.Equal(
            LicenseState.Grace,
            LicenseKeyCodec.ResolveState(payload, payload.ExpiresAt.AddDays(1)));
        Assert.Equal(
            LicenseState.Grace,
            LicenseKeyCodec.ResolveState(payload, payload.ExpiresAt.AddDays(13)));
        Assert.Equal(
            LicenseState.Expired,
            LicenseKeyCodec.ResolveState(payload, payload.ExpiresAt.AddDays(15)));
    }

    [Fact]
    public void Zero_grace_days_expires_immediately_after_term()
    {
        var payload = Payload(days: 5, graceDays: 0);

        Assert.Equal(
            LicenseState.Expired,
            LicenseKeyCodec.ResolveState(payload, payload.ExpiresAt.AddMinutes(1)));
    }
}

public class LicensePolicyTests
{
    [Fact]
    public void Active_license_allows_everything()
    {
        Assert.True(LicensePolicy.CanStartSessions(LicenseState.Active));
        Assert.True(LicensePolicy.CanRegisterComputers(LicenseState.Active));
        Assert.True(LicensePolicy.CanSell(LicenseState.Active));
    }

    [Fact]
    public void Grace_stops_new_sessions_but_still_allows_selling()
    {
        // Смысл льготного периода: зал доигрывает и смена закрывается честно,
        // но новые сеансы уже не стартуют.
        Assert.False(LicensePolicy.CanStartSessions(LicenseState.Grace));
        Assert.False(LicensePolicy.CanRegisterComputers(LicenseState.Grace));
        Assert.True(LicensePolicy.CanSell(LicenseState.Grace));
    }

    [Fact]
    public void Expired_blocks_sessions_and_sales()
    {
        Assert.False(LicensePolicy.CanStartSessions(LicenseState.Expired));
        Assert.False(LicensePolicy.CanSell(LicenseState.Expired));
    }

    [Fact]
    public void Invalid_key_blocks_like_expired()
    {
        Assert.False(LicensePolicy.CanStartSessions(LicenseState.Invalid));
        Assert.False(LicensePolicy.CanSell(LicenseState.Invalid));
    }

    [Fact]
    public void Missing_license_does_not_block_so_existing_clubs_keep_working()
    {
        Assert.True(LicensePolicy.CanStartSessions(LicenseState.Missing));
        Assert.True(LicensePolicy.CanRegisterComputers(LicenseState.Missing));
        Assert.True(LicensePolicy.CanSell(LicenseState.Missing));
    }

    [Fact]
    public void Warning_appears_a_week_before_expiry()
    {
        Assert.NotNull(LicensePolicy.Warning(LicenseState.Active, LicensePolicy.WarnBeforeDays, 0));
        Assert.Null(LicensePolicy.Warning(LicenseState.Active, LicensePolicy.WarnBeforeDays + 1, 0));
    }

    [Fact]
    public void Block_message_names_the_action_so_cashier_understands()
    {
        var message = LicensePolicy.BlockMessage(LicenseState.Expired, "Запуск сеанса");

        Assert.Contains("Запуск сеанса", message);
        Assert.Contains("лицензии", message);
    }
}
