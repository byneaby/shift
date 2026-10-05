using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShiftClub.Application.Abstractions;

namespace ShiftClub.Infrastructure.SignalR;

public sealed class DeviceAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly IComputerService _computerService;

    public DeviceAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IComputerService computerService)
        : base(options, logger, encoder)
    {
        _computerService = computerService;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? token = null;

        if (Request.Headers.TryGetValue("X-Device-Token", out var headerValues))
            token = headerValues.FirstOrDefault();

        if (string.IsNullOrWhiteSpace(token)
            && Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            var value = authHeader.FirstOrDefault();
            if (value is not null && value.StartsWith("Device ", StringComparison.OrdinalIgnoreCase))
                token = value["Device ".Length..].Trim();
        }

        if (string.IsNullOrWhiteSpace(token)
            && Request.Query.TryGetValue("device_token", out var queryToken))
        {
            token = queryToken.FirstOrDefault();
        }

        if (string.IsNullOrWhiteSpace(token))
            return AuthenticateResult.NoResult();

        var computerId = await _computerService.ResolveComputerIdByDeviceTokenAsync(token);
        if (computerId is null)
            return AuthenticateResult.Fail("Invalid device token.");

        var claims = new[]
        {
            new Claim(DeviceAuthDefaults.ComputerIdClaim, computerId.Value.ToString()),
            new Claim(ClaimTypes.Name, computerId.Value.ToString())
        };
        var identity = new ClaimsIdentity(claims, DeviceAuthDefaults.SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, DeviceAuthDefaults.SchemeName);
        return AuthenticateResult.Success(ticket);
    }
}
