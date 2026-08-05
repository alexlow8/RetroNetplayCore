using System.Runtime.CompilerServices;

namespace RetroNetplayCore.Synchronization
{
    // Primary constructor declaration strips away old private variable assignments
    public class HostSyncEngine(NetworkLink network, bool[] activeSlots) : INetplaySyncEngine
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ProcessFrameSync(uint frameId, ref ClientInputPacket localInput, ref HostFramePacket outMasterPacket)
        {
            outMasterPacket.FrameId = frameId;

            // Map Host input directly into slot 0
            outMasterPacket.Player0.Buttons = localInput.Buttons;
            outMasterPacket.Player0.LeftStickX = localInput.LeftStickX;
            outMasterPacket.Player0.LeftStickY = localInput.LeftStickY;
            outMasterPacket.Player0.RightStickX = localInput.RightStickX;
            outMasterPacket.Player0.RightStickY = localInput.RightStickY;
            outMasterPacket.Player0.LeftTrigger = localInput.LeftTrigger;
            outMasterPacket.Player0.RightTrigger = localInput.RightTrigger;

            ushort arrivedSlotsMask = 1; // Bit 0 is the Host, always arrived
            ushort expectedSlotsMask = 0;

            for (int i = 0; i < 4; i++)
            {
                if (activeSlots[i]) expectedSlotsMask |= (ushort)(1 << i);
            }

            // Block-poll the unmanaged network stack until all active clients report in
            ClientInputPacket incoming = default;
            while (arrivedSlotsMask != expectedSlotsMask)
            {
                if (network.HostReceiveInput(ref incoming, out int slot))
                {
                    if (incoming.FrameId == frameId && slot >= 1 && slot <= 3)
                    {
                        AssignSlot(ref outMasterPacket, slot, ref incoming);
                        arrivedSlotsMask |= (ushort)(1 << slot);
                    }
                }
                System.Threading.Thread.Yield();
            }

            // Fire the complete 52-byte master state out to everyone
            network.HostBroadcastFrame(ref outMasterPacket);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void AssignSlot(ref HostFramePacket master, int slot, ref ClientInputPacket incoming)
        {
            switch (slot)
            {
                case 1:
                    master.Player1.Buttons = incoming.Buttons;
                    master.Player1.LeftStickX = incoming.LeftStickX;
                    master.Player1.LeftStickY = incoming.LeftStickY;
                    master.Player1.RightStickX = incoming.RightStickX;
                    master.Player1.RightStickY = incoming.RightStickY;
                    master.Player1.LeftTrigger = incoming.LeftTrigger;
                    master.Player1.RightTrigger = incoming.RightTrigger;
                    break;
                case 2:
                    master.Player2.Buttons = incoming.Buttons;
                    master.Player2.LeftStickX = incoming.LeftStickX;
                    master.Player2.LeftStickY = incoming.LeftStickY;
                    master.Player2.RightStickX = incoming.RightStickX;
                    master.Player2.RightStickY = incoming.RightStickY;
                    master.Player2.LeftTrigger = incoming.LeftTrigger;
                    master.Player2.RightTrigger = incoming.RightTrigger;
                    break;
                case 3:
                    master.Player3.Buttons = incoming.Buttons;
                    master.Player3.LeftStickX = incoming.LeftStickX;
                    master.Player3.LeftStickY = incoming.LeftStickY;
                    master.Player3.RightStickX = incoming.RightStickX;
                    master.Player3.RightStickY = incoming.RightStickY;
                    master.Player3.LeftTrigger = incoming.LeftTrigger;
                    master.Player3.RightTrigger = incoming.RightTrigger;
                    break;
            }
        }
    }
}
