using System.Runtime.InteropServices;

namespace RetroNetplayCore.Packets
{
    // Sent from individual clients to the Host.
    // Total Size: EXACTLY 16 BYTES.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ClientInputPacket
    {
        public uint FrameId;         // 4 bytes
        public ushort Buttons;       // 2 bytes
        public short LeftStickX;     // 2 bytes
        public short LeftStickY;     // 2 bytes
        public short RightStickX;    // 2 bytes
        public short RightStickY;    // 2 bytes
        public byte LeftTrigger;     // 1 byte
        public byte RightTrigger;    // 1 byte
    }

    // A lightweight container stripped of FrameId to save network overhead.
    // Total Size: 12 BYTES.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct PlayerInputState
    {
        public ushort Buttons;
        public short LeftStickX;
        public short LeftStickY;
        public short RightStickX;
        public short RightStickY;
        public byte LeftTrigger;
        public byte RightTrigger;
    }

    // Broadcast from the Host to ALL clients.
    // Total Size: 4 + (12 * 4) = EXACTLY 52 BYTES.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct HostFramePacket
    {
        public uint FrameId; // 4 bytes

        // Unmanaged inline structures matching player slots 0, 1, 2, and 3
        public PlayerInputState Player0; // Host
        public PlayerInputState Player1; // Client 1
        public PlayerInputState Player2; // Client 2
        public PlayerInputState Player3; // Client 3
    }
}
