using RetroNetplayLauncher.MatchmakerClient;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RetroNetplayLauncher.MatchmakerClient
{
    public class MatchmakerHostClient : MatchmakerClientBase
    {
        public override bool IsHost => true;

        public MatchmakerHostClient(string serverIpAddress) : base(serverIpAddress) { }

        public string HostSession(int maxPlayers, string selectedCore, string romPath)
        {
            var request = new
            {
                command = "HOST",
                maxPlayers,
                selectedCore,
                romPath = System.IO.Path.GetFileName(romPath)
            };

            byte[] data = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request));
            Socket.SendTo(data, ServerEndPoint);

            byte[] buffer = new byte[1024];
            EndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);

            int received = 0;
            while (received == 0)
            {
                if (Socket.Available > 0) received = Socket.ReceiveFrom(buffer, ref remoteEP);
                Thread.Sleep(10);
            }

            string json = Encoding.UTF8.GetString(buffer, 0, received);
            using var doc = JsonDocument.Parse(json);
            ActiveRoomCode = doc.RootElement.GetProperty("code").GetString() ?? "";

            StartListening();
            _ = Task.Run(() => StartHeartbeatLoop(LobbyCts!.Token));

            return ActiveRoomCode;
        }

        private async Task StartHeartbeatLoop(CancellationToken token)
        {
            var pingPayload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { command = "PING", code = ActiveRoomCode }));

            while (!token.IsCancellationRequested)
            {
                try { Socket.SendTo(pingPayload, ServerEndPoint); } catch (SocketException) { }
                await Task.Delay(5000, token);
            }
        }

        public void SignalStartGame()
        {
            var startRequest = new { command = "START_GAME", code = ActiveRoomCode };
            byte[] data = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(startRequest));
            Socket.SendTo(data, ServerEndPoint);
        }
    }
}
