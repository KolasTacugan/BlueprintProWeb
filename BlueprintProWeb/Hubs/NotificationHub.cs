using Microsoft.AspNetCore.SignalR;

namespace BlueprintProWeb.Hubs
{

    public class NotificationHub : Hub
    {
        public override async Task OnConnectedAsync()
        {
            var userId = Context.User?.FindFirst("sub")?.Value;
            if (!string.IsNullOrEmpty(userId))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, userId);
            }

            await base.OnConnectedAsync();
        }
    }

}
