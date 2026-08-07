using RetroNetplayCore.Packets;
using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace RetroNetplayCore
{
    public class NetworkLink
    {
        private readonly Socket _socket;
        private readonly bool _isHost;
        private readonly IPEndPoint[] _clientEndPoints = new IPEndPoint[4]; // Slots 1, 2, 3
        private readonly IPEndPoint? _hostEndPoint;

        // Host Constructor
        public NetworkLink(int localPort)
        {
            _isHost = true;
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            // Overrides native kernel locks so the Core can instantly claim the port
            _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _socket.Bind(new IPEndPoint(IPAddress.Any, localPort));
            _socket.Blocking = false;
        }

        // Client Constructor
        public NetworkLink(int localPort, string hostAddress, int hostPort)
        {
            _isHost = false;
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            // Overrides native kernel locks so the Core can instantly claim the port
            _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _socket.Bind(new IPEndPoint(IPAddress.Any, localPort));
            _socket.Blocking = false;
            _hostEndPoint = new IPEndPoint(IPAddress.Parse(hostAddress), hostPort);
        }

        public void RegisterClient(int playerSlot, string ipAddress, int port)
        {
            if (!_isHost || playerSlot < 1 || playerSlot > 3) return;
            _clientEndPoints[playerSlot] = new IPEndPoint(IPAddress.Parse(ipAddress), port);
        }

        /// <summary>
        /// CLIENT METHOD: Sends local controller inputs up to the central host.
        /// </summary>
        public void ClientSendInput(ref ClientInputPacket packet)
        {
            ReadOnlySpan<byte> sendSpan = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref packet, 1));
            try
            {
                _socket.SendTo(sendSpan, SocketFlags.None, _hostEndPoint!);
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.WouldBlock) { }
        }

        /// <summary>
        /// HOST METHOD: Broadcasts the aggregated 4-player inputs to every connected client.
        /// </summary>
        public void HostBroadcastFrame(ref HostFramePacket masterPacket)
        {
            ReadOnlySpan<byte> sendSpan = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref masterPacket, 1));
            try
            {
                for (int i = 1; i < 4; i++)
                {
                    if (_clientEndPoints[i] != null)
                    {
                        _socket.SendTo(sendSpan, SocketFlags.None, _clientEndPoints[i]);
                    }
                }
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.WouldBlock) { }
        }

        /// <summary>
        /// HOST METHOD: Reads an incoming individual packet from a client and identifies their player slot.
        /// </summary>
        public bool HostReceiveInput(ref ClientInputPacket incomingPacket, out int outPlayerSlot)
        {
            outPlayerSlot = -1;
            Span<byte> recvSpan = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref incomingPacket, 1));
            EndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);

            try
            {
                int bytesRead = _socket.ReceiveFrom(recvSpan, SocketFlags.None, ref remoteEP);
                if (bytesRead != Marshal.SizeOf<ClientInputPacket>()) return false;

                IPEndPoint senderNet = (IPEndPoint)remoteEP;
                for (int i = 1; i < 4; i++)
                {
                    if (_clientEndPoints[i] != null && _clientEndPoints[i].Equals(senderNet))
                    {
                        outPlayerSlot = i;
                        return true;
                    }
                }
                return false;
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.WouldBlock || ex.SocketErrorCode == SocketError.ConnectionReset)
            {
                return false;
            }
        }

        /// <summary>
        /// CLIENT METHOD: Awaits the master 4-player synchronized frame packet from the host.
        /// </summary>
        public bool ClientReceiveFrame(ref HostFramePacket masterPacket)
        {
            Span<byte> recvSpan = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref masterPacket, 1));
            EndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);

            try
            {
                int bytesRead = _socket.ReceiveFrom(recvSpan, SocketFlags.None, ref remoteEP);
                return bytesRead == Marshal.SizeOf<HostFramePacket>();
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.WouldBlock || ex.SocketErrorCode == SocketError.ConnectionReset)
            {
                return false;
            }
        }

        public void Close() => _socket.Close();
    }
}
