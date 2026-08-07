using RetroNetplayCore.Packets;

namespace RetroNetplayCore.Synchronization
{
    public interface INetplaySyncEngine
    {
        // Executes the network sync phase and returns the ready 4-player master packet
        void ProcessFrameSync(uint frameId, ref ClientInputPacket localInput, ref HostFramePacket outMasterPacket);
    }
}
