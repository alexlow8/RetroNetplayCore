using System;
using System.Runtime.InteropServices;

namespace RetroNetplayCore.Emulator
{
    // 1. Controller and Input specific constants
    public static class LibretroInputConstants
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

    // 2. Environment intercom and system settings constants
    public static class LibretroEnvConstants
    {
        // Intercom Command IDs
        public const uint RETRO_ENVIRONMENT_SET_PIXEL_FORMAT = 1;
        public const uint RETRO_ENVIRONMENT_GET_SYSTEM_DIRECTORY = 2;
        public const uint RETRO_ENVIRONMENT_GET_CAN_DUPLICATE_FRAMES = 3;
        public const uint RETRO_ENVIRONMENT_GET_LOG_INTERFACE = 15;

        // Video Pixel Color Formats
        public const uint RETRO_PIXEL_FORMAT_0RGB1555 = 0;
        public const uint RETRO_PIXEL_FORMAT_XRGB8888 = 1;
        public const uint RETRO_PIXEL_FORMAT_RGB565 = 2;
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

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct RetroLogCallback
    {
        // A direct pointer to an unmanaged C-style printf logging function
        public delegate* unmanaged[Cdecl]<uint, byte*, void> Log;
    }
}
