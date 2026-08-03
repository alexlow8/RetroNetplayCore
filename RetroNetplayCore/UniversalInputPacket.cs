using System.Runtime.InteropServices;

namespace RetroNetplayCore
{
    // Total Size: EXACTLY 16 BYTES. Safe for zero-allocation performance.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct UniversalInputPacket
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
}
