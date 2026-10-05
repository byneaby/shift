using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using ShiftClub.Shared.SignalR;

namespace ShiftClub.Infrastructure.SignalR;

[Authorize]
public sealed class StaffHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, HubGroups.Staff());
        await base.OnConnectedAsync();
    }
}
