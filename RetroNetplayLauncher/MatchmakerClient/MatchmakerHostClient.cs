using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RetroNetplayLauncher.MatchmakerClient
{
    public class MatchmakerHostClient(string serverIpAddress) : MatchmakerClientBase(serverIpAddress)
    {
        public override bool IsHost => true;

        public async Task<string> HostSessionAsync(int maxPlayers, string selectedCore, string romPath)
        {
            var request = new HostRequest
            {
                Command = "HOST",
                MaxPlayers = maxPlayers,
                CoreName = selectedCore,
                RomPath = System.IO.Path.GetFileName(romPath),
                InternalIp = LocalIpAddress,
                InternalPort = LocalPort
            };

            byte[] data = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request));
            await Socket.SendToAsync(new ArraySegment<byte>(data), SocketFlags.None, ServerEndPoint);

            byte[] buffer = new byte[1024];
            var result = await Socket.ReceiveFromAsync(new ArraySegment<byte>(buffer), SocketFlags.None, new IPEndPoint(IPAddress.Any, 0));

            string json = Encoding.UTF8.GetString(buffer, 0, result.ReceivedBytes);
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
                try { await Socket.SendToAsync(new ArraySegment<byte>(pingPayload), SocketFlags.None, ServerEndPoint); } catch (SocketException) { }
                await Task.Delay(5000, token);
            }
        }

        public async Task SignalStartGameAsync()
        {
            var startRequest = new { command = "START_GAME", code = ActiveRoomCode };
            byte[] data = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(startRequest));
            await Socket.SendToAsync(new ArraySegment<byte>(data), SocketFlags.None, ServerEndPoint);
        }
    }
}
