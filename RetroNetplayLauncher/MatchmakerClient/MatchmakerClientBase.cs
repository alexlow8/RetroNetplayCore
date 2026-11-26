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
    public abstract class MatchmakerClientBase : IDisposable
    {
        protected readonly string ServerIP;
        protected readonly int ServerPort = 5005;
        protected readonly Socket Socket;
        protected readonly IPEndPoint ServerEndPoint;

        protected CancellationTokenSource? LobbyCts;
        protected string ActiveRoomCode = "";
        private bool _isDisposed;

        public int LocalPort { get; protected set; }
        public string LocalIpAddress { get; private set; } = "127.0.0.1";
        public abstract bool IsHost { get; }
        public string TargetCoreName { get; protected set; } = "MockLibretroCore.dll";
        public string TargetRomPath { get; protected set; } = "";
        public string RoomCode => ActiveRoomCode;
        public string ServerAddress => ServerIP;
        public int ServerPortNumber => ServerPort;

        public event Action<LobbyUpdateMessage>? OnLobbyUpdated;
        public event Action? OnGameStarting;

        protected MatchmakerClientBase(string serverIpAddress)
        {
            ServerIP = serverIpAddress;

            Socket = SessionManager.CreateInheritableUdpSocket();
            Socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            Socket.Bind(new IPEndPoint(IPAddress.Any, 0));

            // Set blocking to true so it works flawlessly with modern async Task operations
            Socket.Blocking = true;

            if (Socket.LocalEndPoint is IPEndPoint localEP)
            {
                LocalPort = localEP.Port;
            }

            DiscoverLocalIp();

            if (!IPAddress.TryParse(ServerIP, out var targetAddress))
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

        /// <summary>
        /// Finds this computer's internal LAN IP address dynamically.
        /// </summary>
        private void DiscoverLocalIp()
        {
            try
            {
                using Socket tempSocket = new(AddressFamily.InterNetwork, SocketType.Dgram, 0);
                tempSocket.Connect("8.8.8.8", 65530);
                if (tempSocket.LocalEndPoint is IPEndPoint localEP)
                {
                    LocalIpAddress = localEP.Address.ToString();
                }
            }
            catch
            {
                LocalIpAddress = "127.0.0.1";
            }
        }

        /// <summary>
        /// Safely detaches and extracts the socket for the unmanaged application takeover.
        /// It completely halts background threads first to ensure no thread exceptions occur.
        /// </summary>
        public Socket ArrearsAndExtractSocket()
        {
            if (LobbyCts != null && !LobbyCts.IsCancellationRequested)
            {
                LobbyCts.Cancel();
            }

            // Severe event bindings to prevent UI manipulation from asynchronous backgrounds
            OnLobbyUpdated = null;
            OnGameStarting = null;

            return Socket;
        }

        protected void StartListening()
        {
            LobbyCts = new CancellationTokenSource();
            Task.Run(() => StartListeningLoop(LobbyCts.Token));
        }

        private async Task StartListeningLoop(CancellationToken token)
        {
            byte[] buffer = new byte[2048];

            while (!token.IsCancellationRequested)
            {
                try
                {
                    // Asynchronously listen on the socket without locking up thread pools
                    var result = await Socket.ReceiveFromAsync(new ArraySegment<byte>(buffer), SocketFlags.None, new IPEndPoint(IPAddress.Any, 0));

                    if (result.ReceivedBytes > 0)
                    {
                        string json = Encoding.UTF8.GetString(buffer, 0, result.ReceivedBytes);
                        using var doc = JsonDocument.Parse(json);

                        if (doc.RootElement.TryGetProperty("status", out JsonElement statusProp))
                        {
                            string status = statusProp.GetString() ?? "";

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
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.OperationAborted || ex.SocketErrorCode == SocketError.Interrupted)
                {
                    break;
                }
                catch
                {
                    // Fail silently or capture telemetry here
                }
            }
        }

        public async Task ExecuteHolePunchAsync(List<PeerEndpoint> peers)
        {
            if (LobbyCts != null) await LobbyCts.CancelAsync();
            byte[] punchToken = Encoding.UTF8.GetBytes("PUNCH");

            foreach (var peer in peers)
            {
                // Fire hole punches out to BOTH the public internet WAN footprint
                // and the internal local network footprint simultaneously.
                if (IPAddress.TryParse(peer.Ip, out var publicIp))
                {
                    try { await Socket.SendToAsync(new ArraySegment<byte>(punchToken), SocketFlags.None, new IPEndPoint(publicIp, peer.Port)); } catch { }
                }
                if (IPAddress.TryParse(peer.InternalIp, out var internalIp))
                {
                    try { await Socket.SendToAsync(new ArraySegment<byte>(punchToken), SocketFlags.None, new IPEndPoint(internalIp, peer.InternalPort)); } catch { }
                }
            }

            // Hold mapping state momentarily to allow handle migration sequence to settle
            await Task.Delay(500);
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            try
            {
                LobbyCts?.Cancel();
                if (Socket.Connected || Socket.IsBound)
                {
                    Socket.Close();
                }
            }
            catch { }
            GC.SuppressFinalize(this);
        }
    }
}
