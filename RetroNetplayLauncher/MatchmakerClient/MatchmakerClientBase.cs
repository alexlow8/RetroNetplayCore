using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace RetroNetplayLauncher.MatchmakerClient
{
    public abstract class MatchmakerClientBase
    {
        protected readonly string ServerIP;
        protected readonly int ServerPort = 5005; // Using our updated host port!
        protected readonly Socket Socket;
        protected readonly IPEndPoint ServerEndPoint;

        protected CancellationTokenSource? LobbyCts;
        protected string ActiveRoomCode = "";

        public int LocalPort { get; protected set; }
        public abstract bool IsHost { get; }
        public string TargetCoreName { get; protected set; } = "MockLibretroCore.dll";
        public string TargetRomPath { get; protected set; } = "";

        public event Action<LobbyUpdateMessage>? OnLobbyUpdated;
        public event Action? OnGameStarting;

        protected MatchmakerClientBase(string serverIpAddress)
        {
            ServerIP = serverIpAddress;

            Socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            Socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            Socket.Bind(new IPEndPoint(IPAddress.Any, 0));
            Socket.Blocking = false;

            if (Socket.LocalEndPoint is IPEndPoint localEP)
            {
                LocalPort = localEP.Port;
            }

            IPAddress targetAddress;
            if (IPAddress.TryParse(ServerIP, out var parsedIP))
            {
                targetAddress = parsedIP;
            }
            else
            {
                try
                {
                    IPAddress[] addresses = Dns.GetHostAddresses(ServerIP);
                    targetAddress = addresses[0];
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Could not resolve server address: {ex.Message}", ex);
                }
            }

            ServerEndPoint = new IPEndPoint(targetAddress, ServerPort);
        }

        protected void StartListening()
        {
            LobbyCts = new CancellationTokenSource();
            Task.Run(() => StartListeningLoop(LobbyCts.Token));
        }

        private async Task StartListeningLoop(CancellationToken token)
        {
            byte[] buffer = new byte[2048];
            EndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);

            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (Socket.Available > 0)
                    {
                        int bytesRead = Socket.ReceiveFrom(buffer, ref remoteEP);
                        string json = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                        using var doc = JsonDocument.Parse(json);
                        string status = doc.RootElement.GetProperty("status").GetString() ?? "";

                        if (status == "LOBBY_UPDATE" || status == "GAME_STARTING")
                        {
                            if (doc.RootElement.TryGetProperty("coreName", out JsonElement coreProp))
                                TargetCoreName = coreProp.GetString() ?? TargetCoreName;

                            if (doc.RootElement.TryGetProperty("romPath", out JsonElement romProp))
                                TargetRomPath = romProp.GetString() ?? TargetRomPath;

                            if (status == "LOBBY_UPDATE")
                            {
                                var update = JsonSerializer.Deserialize<LobbyUpdateMessage>(json);
                                if (update != null) OnLobbyUpdated?.Invoke(update);
                            }
                            else if (status == "GAME_STARTING")
                            {
                                OnGameStarting?.Invoke();
                                break;
                            }
                        }
                    }
                }
                catch (SocketException) { }

                await Task.Delay(50, token);
            }
        }

        public void ExecuteHolePunch(List<PeerEndpoint> peers)
        {
            LobbyCts?.Cancel();
            byte[] punchToken = Encoding.UTF8.GetBytes("PUNCH");

            foreach (var peer in peers)
            {
                IPEndPoint peerEP = new(IPAddress.Parse(peer.Ip), peer.Port);
                for (int i = 0; i < 10; i++)
                {
                    Socket.SendTo(punchToken, peerEP);
                    Thread.Sleep(10);
                }
            }

            try { Socket.Shutdown(SocketShutdown.Both); } catch (SocketException) { }
            Socket.Close();
        }
    }
}
