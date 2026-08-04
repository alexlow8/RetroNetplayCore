using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace RetroNetplayCore
{
    public class NetworkLink
    {
        private readonly Socket _socket;
        private readonly IPEndPoint _targetEndPoint;

        public NetworkLink(int localPort, int targetPort)
        {
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _socket.Bind(new IPEndPoint(IPAddress.Loopback, localPort));
            _socket.Blocking = false;

            _targetEndPoint = new IPEndPoint(IPAddress.Loopback, targetPort);
        }

        public void SendPacket(ref UniversalInputPacket packet)
        {
            // Turn our 16-byte struct directly into a network span without allocating heap objects
            ReadOnlySpan<byte> sendSpan = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref packet, 1));
            try
            {
                _socket.SendTo(sendSpan, SocketFlags.None, _targetEndPoint);
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.WouldBlock) { }
        }

        public bool ReceivePacket(ref UniversalInputPacket remotePacket)
        {
            Span<byte> recvSpan = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref remotePacket, 1));
            EndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);

            try
            {
                int bytesRead = _socket.ReceiveFrom(recvSpan, SocketFlags.None, ref remoteEP);
                return bytesRead == 16;
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.WouldBlock)
            {
                return false;
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset)
            {
                return false;
            }
        }

        public void Close()
        {
            _socket.Close();
        }
    }
}
