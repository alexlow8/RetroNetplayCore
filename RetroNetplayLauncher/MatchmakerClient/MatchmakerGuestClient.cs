using RetroNetplayLauncher.MatchmakerClient;
using System;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace RetroNetplayLauncher.MatchmakerClient
{
    public class MatchmakerGuestClient : MatchmakerClientBase
    {
        public override bool IsHost => false;

        public MatchmakerGuestClient(string serverIpAddress) : base(serverIpAddress) { }

        public void JoinSession(string roomCode)
        {
            ActiveRoomCode = roomCode;
            var request = new { command = "JOIN", code = roomCode };
            byte[] data = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request));

            Socket.SendTo(data, ServerEndPoint);
            StartListening();
        }
    }
}
