using RetroNetplayCore.Packets;
using System.Runtime.CompilerServices;

namespace RetroNetplayCore.Synchronization
{
    // The constructor parameters are now declared directly on the class line!
    public class ClientSyncEngine(NetworkLink network) : INetplaySyncEngine
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ProcessFrameSync(uint frameId, ref ClientInputPacket localInput, ref HostFramePacket outMasterPacket)
        {
            // Ship local hardware state up to the host
            network.ClientSendInput(ref localInput);

            // Poll until the master payload matching this frame index returns
            while (true)
            {
                if (network.ClientReceiveFrame(ref outMasterPacket))
                {
                    if (outMasterPacket.FrameId == frameId)
                    {
                        break;
                    }
                }
                System.Threading.Thread.Yield();
            }
        }
    }
}
