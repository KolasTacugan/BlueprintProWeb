using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;

namespace BlueprintProWeb.Hubs
{
    public class ChatHub : Hub
    {
        public async Task SendMessage(string clientId, string message, string senderName, string senderPhoto)
        {
            await Clients.Group(clientId)
                .SendAsync("ReceiveMessage", senderName, message, senderPhoto, DateTime.Now.ToString("HH:mm"));

            // NEW: Trigger unread message update for the user
            await Clients.Group(clientId)
                .SendAsync("UpdateUnreadMessagesCount");
        }

        public override async Task OnConnectedAsync()
        {
            var clientId = Context.GetHttpContext()?.Request.Query["clientId"];
            if (!string.IsNullOrEmpty(clientId))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, clientId);
            }
            await base.OnConnectedAsync();
        }
    }
}
