using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RetroNetplayCore.Configuration;
using RetroNetplayCore.Packets;

namespace RetroNetplayCore.Synchronization
{
    /// <summary>
    /// Manages all network I/O on a background thread.
    /// 
    /// Design principle: Keep the frame loop (60 FPS) completely allocation-free.
    /// All network operations (socket I/O, JSON serialization, event handling) happen here,
    /// leaving only pre-allocated buffer reads/writes for the frame thread.
    /// 
    /// Two responsibilities:
    /// 1. P2P Input Distribution (mesh): Send our inputs to all peers, receive theirs
    /// 2. Server Heartbeat: Keep server alive to detect dropouts
    /// </summary>
    public class NetworkThreadManager : IDisposable
    {
        // --- CONFIGURATION ---
        private readonly EngineConfig _config;
        private readonly int _localPlayerIndex;

        // --- SOCKETS ---
        /// <summary>Local UDP socket for P2P peer-to-peer connections</summary>
        private Socket? _p2pSocket;

        /// <summary>Server socket for heartbeat/coordination messages</summary>
        private Socket? _serverSocket;

        // --- RING BUFFERS (Thread-safe communication with frame loop) ---
        /// <summary>
        /// Circular buffer of peer inputs, indexed by frame counter.
        /// Frame loop reads from here without blocking on network I/O.
        /// Network thread writes here as inputs arrive.
        /// Pre-allocated to avoid GC pauses during gameplay.
        /// </summary>
        private readonly HostFramePacket[] _inputRingBuffer;
        private readonly int _ringBufferMask;

        // --- THREAD CONTROL ---
        private CancellationTokenSource? _cancellation;
        private Thread? _networkThread;
        private volatile bool _isRunning;

        // --- HEARTBEAT TIMING ---
        private const int HEARTBEAT_INTERVAL_MS = 1000; // Send server heartbeat every 1 second
        private long _lastHeartbeatTime;

        // --- PEER ENDPOINTS ---
        /// <summary>Map of PlayerIndex -> IPEndPoint for P2P connections (tries LAN first, falls back to WAN)</summary>
        private readonly Dictionary<int, IPEndPoint> _peerEndpoints = [];

        /// <summary>Server endpoint for heartbeat messages</summary>
        private IPEndPoint? _serverEndpoint;

        public NetworkThreadManager(EngineConfig config, int localPlayerIndex)
        {
            _config = config;
            _localPlayerIndex = localPlayerIndex;

            // Ring buffer size: 60 frames * 2 seconds = 120 slots (handles 2 seconds of frame buffering)
            // Must be power of 2 for efficient modulo arithmetic
            _inputRingBuffer = new HostFramePacket[256];
            _ringBufferMask = 255; // 256 - 1

            Console.WriteLine($"🌐 Network Manager initializing for player {localPlayerIndex}...");
        }

        /// <summary>
        /// Start the background network thread.
        /// Call this after emulator is loaded but before frame loop starts.
        /// </summary>
        public void Start()
        {
            if (_isRunning) return;

            try
            {
                // Initialize P2P socket
                InitializeP2PSocket();

                // Initialize server socket and endpoint
                InitializeServerSocket();

                // Build peer endpoint map (LAN IPs preferred for local networks)
                BuildPeerEndpoints();

                // Start background network thread
                _cancellation = new CancellationTokenSource();
                _networkThread = new Thread(() => NetworkThreadLoop(_cancellation.Token))
                {
                    Name = "RetroNetplayNetworkThread",
                    IsBackground = true,
                    Priority = ThreadPriority.AboveNormal
                };
                _networkThread.Start();

                _isRunning = true;
                Console.WriteLine($"✅ Network thread started. Listening on port {_config.LocalPort}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Failed to start network manager: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Create and bind the P2P UDP socket.
        /// All cores on the same LAN or internet will connect to this socket.
        /// </summary>
        private void InitializeP2PSocket()
        {
            _p2pSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
            {
                ReceiveTimeout = 100,  // Don't block forever if no data
                SendTimeout = 100,
                Blocking = false       // Non-blocking for async network I/O
            };

            // Bind to the port specified in config (matches what launcher advertised to peers)
            var localEndpoint = new IPEndPoint(IPAddress.Any, _config.LocalPort);
            _p2pSocket.Bind(localEndpoint);

            Console.WriteLine($"   P2P Socket: Bound to 0.0.0.0:{_config.LocalPort}");
        }

        /// <summary>
        /// Create socket for server heartbeat messages.
        /// Keeps the netplay server informed we're still alive and playing.
        /// </summary>
        private void InitializeServerSocket()
        {
            _serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
            {
                ReceiveTimeout = 100,
                SendTimeout = 100,
                Blocking = false
            };

            // Parse server address
            if (!IPAddress.TryParse(_config.ServerAddress, out var serverIp))
            {
                // If server address is a hostname, resolve it
                try
                {
                    var addresses = Dns.GetHostAddresses(_config.ServerAddress);
                    if (addresses.Length > 0) serverIp = addresses[0];
                    else throw new InvalidOperationException($"Could not resolve server: {_config.ServerAddress}");
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to resolve server address '{_config.ServerAddress}': {ex.Message}", ex);
                }
            }

            _serverEndpoint = new IPEndPoint(serverIp, _config.ServerPort);
            Console.WriteLine($"   Server Socket: {_config.ServerAddress}:{_config.ServerPort}");
        }

        /// <summary>
        /// Build the map of peer addresses.
        /// For each peer, prefer LAN address if we're on the same network,
        /// otherwise use their public WAN address.
        /// </summary>
        private void BuildPeerEndpoints()
        {
            Console.WriteLine($"   Building peer routing table ({_config.Peers.Count} peers):");

            foreach (var peer in _config.Peers)
            {
                if (peer.PlayerIndex == _localPlayerIndex)
                    continue; // Skip ourselves

                // Smart routing: use LAN if both have same public IP, otherwise use WAN
                // (This assumes if two peers share a public IP, they're on same LAN)
                string targetIp = peer.LanIp;
                int targetPort = peer.LanPort;

                // If WAN IPs differ significantly, they're not on same LAN
                if (peer.WanIp != _config.Peers[_localPlayerIndex].WanIp)
                {
                    targetIp = peer.WanIp;
                    targetPort = peer.WanPort;
                }

                if (IPAddress.TryParse(targetIp, out var ipAddr))
                {
                    var endpoint = new IPEndPoint(ipAddr, targetPort);
                    _peerEndpoints[peer.PlayerIndex] = endpoint;
                    Console.WriteLine($"      Player {peer.PlayerIndex}: {targetIp}:{targetPort}");
                }
                else
                {
                    Console.WriteLine($"      ⚠️ Player {peer.PlayerIndex}: Invalid IP {targetIp}");
                }
            }
        }

        /// <summary>
        /// Main network thread loop.
        /// Runs continuously, processing incoming messages and sending heartbeats.
        /// Does NOT touch the emulator - purely I/O and buffer management.
        /// </summary>
        private void NetworkThreadLoop(CancellationToken cancellation)
        {
            byte[] receiveBuffer = new byte[2048];
            _lastHeartbeatTime = DateTime.UtcNow.Ticks;

            Console.WriteLine("🔄 Network thread loop active");

            while (!cancellation.IsCancellationRequested && _isRunning)
            {
                try
                {
                    // --- 1. RECEIVE INCOMING P2P MESSAGES ---
                    ReceiveP2PMessages(receiveBuffer);

                    // --- 2. SEND HEARTBEAT TO SERVER (every 1 second) ---
                    SendServerHeartbeat();

                    // --- 3. SMALL SLEEP to prevent CPU spinning ---
                    Thread.Sleep(10);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Network loop error: {ex.Message}");
                }
            }

            Console.WriteLine("🛑 Network thread stopping...");
        }

        /// <summary>
        /// Receive incoming messages from peers and populate the input ring buffer.
        /// Frame loop will read from this buffer without blocking.
        /// </summary>
        private void ReceiveP2PMessages(byte[] buffer)
        {
            if (_p2pSocket == null) return;

            try
            {
                // Try to receive a message (non-blocking)
                while (_p2pSocket.Poll(0, SelectMode.SelectRead))
                {
                    EndPoint remoteEndpoint = new IPEndPoint(IPAddress.Any, 0);
                    int bytesReceived = _p2pSocket.ReceiveFrom(buffer, ref remoteEndpoint);

                    if (bytesReceived > 0)
                    {
                        // TODO: Parse message and write to ring buffer
                        // For now, we're just receiving. Parsing depends on message format.
                        // This will be expanded when we define the P2P protocol.
                    }
                }
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.WouldBlock)
            {
                // This is normal - no data available, non-blocking socket
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Error receiving P2P message: {ex.Message}");
            }
        }

        /// <summary>
        /// Send heartbeat to server every 1 second.
        /// Tells server we're still alive and playing.
        /// Server uses this to detect dropouts.
        /// </summary>
        private void SendServerHeartbeat()
        {
            if (_serverSocket == null || _serverEndpoint == null) return;

            long now = DateTime.UtcNow.Ticks;
            long elapsed = now - _lastHeartbeatTime;

            // Convert ticks to milliseconds: 10,000 ticks = 1 ms
            if (elapsed >= HEARTBEAT_INTERVAL_MS * 10_000)
            {
                try
                {
                    // Build heartbeat message
                    var heartbeat = new
                    {
                        command = "HEARTBEAT",
                        code = _config.RoomCode,
                        playerIndex = _localPlayerIndex
                    };

                    string json = JsonSerializer.Serialize(heartbeat);
                    byte[] data = Encoding.UTF8.GetBytes(json);

                    _serverSocket.SendTo(data, _serverEndpoint);
                    _lastHeartbeatTime = now;

                    // Console.WriteLine($"💓 Heartbeat sent to server");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Failed to send heartbeat: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Frame loop calls this to get the latest inputs for a given frame.
        /// Returns pre-buffered inputs without blocking on network I/O.
        /// All network traffic is handled on background thread.
        /// </summary>
        public HostFramePacket GetFrameInputs(uint frameNumber)
        {
            int bufferIndex = (int)(frameNumber & _ringBufferMask);
            return _inputRingBuffer[bufferIndex];
        }

        /// <summary>
        /// Frame loop calls this to publish our local inputs to all peers.
        /// Non-blocking: queues for background thread to send.
        /// </summary>
        public void PublishLocalInput(uint frameNumber, ref ClientInputPacket localInput)
        {
            // TODO: Queue local input for broadcast to all peers
            // For now, placeholder. Will be expanded when protocol defined.
        }

        /// <summary>
        /// Cleanly shut down the network thread.
        /// </summary>
        public void Stop()
        {
            _isRunning = false;
            _cancellation?.Cancel();
            _networkThread?.Join(TimeSpan.FromSeconds(2));
        }

        public void Dispose()
        {
            Stop();
            _p2pSocket?.Close();
            _serverSocket?.Close();
            _cancellation?.Dispose();
        }
    }
}
