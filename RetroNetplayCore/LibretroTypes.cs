using System;
using System.Runtime.InteropServices;

namespace RetroNetplayCore.Emulator
{
    // Clear segregation of standard Libretro constants
    public static class LibretroConstants
    {
        public const uint RETRO_DEVICE_JOYPAD = 1;
        public const uint RETRO_DEVICE_ID_JOYPAD_B = 0;
        public const uint RETRO_DEVICE_ID_JOYPAD_Y = 1;
        public const uint RETRO_DEVICE_ID_JOYPAD_SELECT = 2;
        public const uint RETRO_DEVICE_ID_JOYPAD_START = 3;
        public const uint RETRO_DEVICE_ID_JOYPAD_UP = 4;
        public const uint RETRO_DEVICE_ID_JOYPAD_DOWN = 5;
        public const uint RETRO_DEVICE_ID_JOYPAD_LEFT = 6;
        public const uint RETRO_DEVICE_ID_JOYPAD_RIGHT = 7;
        public const uint RETRO_DEVICE_ID_JOYPAD_A = 8;
        public const uint RETRO_DEVICE_ID_JOYPAD_X = 9;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct RetroGameInfo
    {
        public byte* Path;       // Pass raw string path directly
        public nint Data;       // Shared pointer (NULL when using path string)
        public nuint Size;      // Memory sizing data (0 when using path string)
        public byte* Meta;       // Key-value metadata allocations
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct RetroSystemInfo
    {
        public byte* LibraryName;
        public byte* LibraryVersion;
        public byte* ValidExtensions;
        public bool NeedFullpath;
        public bool BlockExtract;
    }
}
