using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace MNBEMART.Hubs
{
    public class NotificationHub : Hub
    {
        public async Task JoinUserGroup(int userId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{userId}");
        }

        public async Task LeaveUserGroup(int userId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"user_{userId}");
        }

        public override async Task OnConnectedAsync()
        {
            var userIdClaim = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (int.TryParse(userIdClaim, out int userId))
            {
                await JoinUserGroup(userId);
            }
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var userIdClaim = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (int.TryParse(userIdClaim, out int userId))
            {
                await LeaveUserGroup(userId);
            }
            await base.OnDisconnectedAsync(exception);
        }
    }
}



