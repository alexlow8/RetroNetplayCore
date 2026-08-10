using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace RetroNetplayLauncher.MatchmakerClient
{
    public class MatchmakerGuestClient(string serverIpAddress) : MatchmakerClientBase(serverIpAddress)
    {
        public override bool IsHost => false;

        public async Task JoinSessionAsync(string roomCode)
        {
            ActiveRoomCode = roomCode;

            var request = new JoinRequest
            {
                Command = "JOIN",
                Code = roomCode,
                InternalIp = LocalIpAddress,
                InternalPort = LocalPort
            };

            byte[] data = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request));
            await Socket.SendToAsync(new ArraySegment<byte>(data), SocketFlags.None, ServerEndPoint);
            StartListening();
        }
    }
}
