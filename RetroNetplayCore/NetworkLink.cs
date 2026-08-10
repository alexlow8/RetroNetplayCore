using RetroNetplayCore.Packets;
using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace RetroNetplayCore
{
    public class NetworkLink
    {
        private readonly Socket _socket;
        private readonly bool _isHost;
        private readonly IPEndPoint[] _clientEndPoints = new IPEndPoint[4]; // Slots 1, 2, 3
        private readonly IPEndPoint? _hostEndPoint;

        /// <summary>
        /// INHERITED HANDEOVER CONSTRUCTOR: Uses public SafeSocketHandle to absorb 
        /// the unmanaged socket handle cleanly.
        /// </summary>
        public NetworkLink(SafeSocketHandle inheritedSocketHandle, bool isHost, string hostAddress = "127.0.0.1", int hostPort = 8000)
        {
            _isHost = isHost;

            // Reclaim the handle into .NET using a public constructor signature
            _socket = new Socket(inheritedSocketHandle)
            {
                // Keep frame updates free of synchronous delays
                Blocking = false
            };

            if (!_isHost)
            {
                _hostEndPoint = new IPEndPoint(IPAddress.Parse(hostAddress), hostPort);
            }
        }

        public void RegisterClient(int playerSlot, string ipAddress, int port)
        {
            if (!_isHost || playerSlot < 1 || playerSlot > 3) return;
            _clientEndPoints[playerSlot] = new IPEndPoint(IPAddress.Parse(ipAddress), port);
        }

        public void ClientSendInput(ref ClientInputPacket packet)
        {
            ReadOnlySpan<byte> sendSpan = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref packet, 1));
            try
            {
                _socket.SendTo(sendSpan, SocketFlags.None, _hostEndPoint!);
            }
            catch (SocketException ex) when (
                ex.SocketErrorCode == SocketError.WouldBlock ||
                ex.SocketErrorCode == SocketError.ConnectionReset ||
                ex.SocketErrorCode == SocketError.ConnectionRefused)
            {
                // Silently drop errors to maintain continuous frame replication
            }
            catch
            {
                // Protect loop thread stability
            }
        }

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
            catch (SocketException ex) when (
                ex.SocketErrorCode == SocketError.WouldBlock ||
                ex.SocketErrorCode == SocketError.ConnectionReset ||
                ex.SocketErrorCode == SocketError.ConnectionRefused)
            {
                // Ignore dropped clients so they don't break the simulation for everyone else
            }
            catch
            {
                // Protect loop execution path consistency
            }
        }

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
