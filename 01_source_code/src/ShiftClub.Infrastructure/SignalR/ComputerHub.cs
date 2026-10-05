using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using ShiftClub.Application.Abstractions;
using ShiftClub.Shared.SignalR;

namespace ShiftClub.Infrastructure.SignalR;

[Authorize(AuthenticationSchemes = DeviceAuthDefaults.SchemeName)]
public sealed class ComputerHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var computerId = Context.User?.FindFirst(DeviceAuthDefaults.ComputerIdClaim)?.Value;
        if (Guid.TryParse(computerId, out var id))
            await Groups.AddToGroupAsync(Context.ConnectionId, HubGroups.Computer(id));

        await base.OnConnectedAsync();
    }
}

public static class DeviceAuthDefaults
{
    public const string SchemeName = "Device";
    public const string ComputerIdClaim = "computer_id";
}
