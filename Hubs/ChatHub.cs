using Microsoft.AspNet.SignalR;

public class ChatHub : Hub
{
    public void Send(string user, string message)
    {
        Clients.All.broadcastMessage(user, message);
    }
}