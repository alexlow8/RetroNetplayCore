using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RetroNetplayLauncher
{
    public class MatchmakerClient
    {
        private readonly string _serverIP; // Dynamically assigned via constructor
        private readonly int _serverPort = 5000;
        private readonly Socket _socket;
        private readonly IPEndPoint _serverEndPoint;

        private CancellationTokenSource? _lobbyCts;
        private string _activeRoomCode = "";

        public int LocalPort { get; private set; }
        public bool IsHost { get; private set; } = false;


        // Event to notify our XAML UI when the player list changes
        public event Action<LobbyUpdateMessage>? OnLobbyUpdated;
        public event Action? OnGameStarting;

        // Update your constructor to receive the string from the UI textbox entry
        public MatchmakerClient(string serverIpAddress)
        {
            _serverIP = serverIpAddress; // Assign it straight to our tracking field

            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

            // Set ReuseAddress on the launcher socket as well to facilitate clean handoffs
            _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

            // FIX: Binding to port 0 tells the OS to assign a random, free port instantly
            _socket.Bind(new IPEndPoint(IPAddress.Any, 0));
            _socket.Blocking = false;

            _serverEndPoint = new IPEndPoint(IPAddress.Parse(_serverIP), _serverPort);

            // Capture the exact port number the OS allocated for this socket
            if (_socket.LocalEndPoint is IPEndPoint boundEndPoint)
            {
                LocalPort = boundEndPoint.Port;
            }
        }

        // --- HOST THE SESSION ---
        public string HostSession(int maxPlayers)
        {
            IsHost = true;
            var request = new HostRequest { MaxPlayers = maxPlayers };
            string payload = JsonSerializer.Serialize(request);
            byte[] data = Encoding.UTF8.GetBytes(payload);

            _socket.SendTo(data, _serverEndPoint);

            // Simple blocking wait for the initial room creation response
            byte[] buffer = new byte[1024];
            EndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);

            // Spin waiting for the response packet safely
            int received = 0;
            while (received == 0)
            {
                if (_socket.Available > 0)
                {
                    received = _socket.ReceiveFrom(buffer, ref remoteEP);
                }
                Thread.Sleep(10);
            }

            string json = Encoding.UTF8.GetString(buffer, 0, received);
            using var doc = JsonDocument.Parse(json);
            _activeRoomCode = doc.RootElement.GetProperty("code").GetString() ?? "";

            // Start background loops for heartbeats and listening for incoming players
            _lobbyCts = new CancellationTokenSource();
            Task.Run(() => StartHeartbeatLoop(_lobbyCts.Token));
            Task.Run(() => StartListeningLoop(_lobbyCts.Token));

            return _activeRoomCode;
        }

        // --- JOIN AN EXISTING SESSION ---
        public void JoinSession(string roomCode)
        {
            IsHost = false;
            _activeRoomCode = roomCode;
            var request = new { command = "JOIN", code = roomCode };
            byte[] data = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request));

            _socket.SendTo(data, _serverEndPoint);

            _lobbyCts = new CancellationTokenSource();
            Task.Run(() => StartListeningLoop(_lobbyCts.Token));
        }

        // --- BACKGROUND LOOP: HEARTBEATS (Every 5 seconds) ---
        private async Task StartHeartbeatLoop(CancellationToken token)
        {
            var pingPayload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { command = "PING", code = _activeRoomCode }));

            while (!token.IsCancellationRequested)
            {
                try
                {
                    _socket.SendTo(pingPayload, _serverEndPoint);
                }
                catch (SocketException) { /* Handle transient network glitches */ }

                await Task.Delay(5000, token);
            }
        }

        // --- BACKGROUND LOOP: LISTEN FOR SERVER UPDATES ---
        private async Task StartListeningLoop(CancellationToken token)
        {
            byte[] buffer = new byte[2048];
            EndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);

            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (_socket.Available > 0)
                    {
                        int bytesRead = _socket.ReceiveFrom(buffer, ref remoteEP);
                        string json = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                        using var doc = JsonDocument.Parse(json);
                        string status = doc.RootElement.GetProperty("status").GetString() ?? "";

                        if (status == "LOBBY_UPDATE")
                        {
                            var update = JsonSerializer.Deserialize<LobbyUpdateMessage>(json);
                            if (update != null)
                            {
                                // Fire the event to alert the XAML UI code-behind
                                OnLobbyUpdated?.Invoke(update);
                            }
                        }
                        else if (status == "GAME_STARTING")
                        {
                            OnGameStarting?.Invoke();
                            break;
                        }
                    }
                }
                catch (SocketException) { }

                await Task.Delay(50, token); // Keep the CPU resting between checks
            }
        }

        // --- TRIGGER THE HOLE PUNCH ---
        public void ExecuteHolePunch(List<PeerEndpoint> peers)
        {
            _lobbyCts?.Cancel(); // Stop the heartbeat and server listener loops

            byte[] punchToken = Encoding.UTF8.GetBytes("PUNCH");

            // Blast packets at every single player in the list to pierce their firewalls
            foreach (var peer in peers)
            {
                IPEndPoint peerEP = new(IPAddress.Parse(peer.Ip), peer.Port);

                for (int i = 0; i < 10; i++)
                {
                    _socket.SendTo(punchToken, peerEP);
                    Thread.Sleep(10);
                }
            }

            // CLEAN HANDOFF FIX: Tell the OS kernel we are shutting down our traffic 
            // streams on this socket before completely freeing it.
            try
            {
                _socket.Shutdown(SocketShutdown.Both);
            }
            catch (SocketException) { /* Swallow if socket was already unaligned */ }

            _socket.Close(); // Relinquish the port handle so the core can instantly reclaim it
        }

        // --- SEND THE START SIGNAL (Host Only) ---
        public void SignalStartGame()
        {
            var startRequest = new { command = "START_GAME", code = _activeRoomCode };
            byte[] data = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(startRequest));
            _socket.SendTo(data, _serverEndPoint);
        }
    }
}
