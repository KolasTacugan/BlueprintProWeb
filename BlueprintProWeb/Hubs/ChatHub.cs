using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;

namespace BlueprintProWeb.Hubs
{
    public class ChatHub : Hub
    {
        public async Task SendMessage(string clientId, string message, string senderName)
        {
            await Clients.Group(clientId)
                .SendAsync("ReceiveMessage", senderName, message, DateTime.Now.ToString("HH:mm"));
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
