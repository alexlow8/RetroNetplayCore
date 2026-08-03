using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RetroNetplayCore.Emulator
{
    public unsafe class EmulatorManager
    {
        private readonly LibretroBridge _bridge;
        private readonly RomLoader _romLoader;

        private static UniversalInputPacket _currentLocalInput;
        private static UniversalInputPacket _currentRemoteInput;

        public EmulatorManager()
        {
            _bridge = new LibretroBridge();
            _romLoader = new RomLoader();
        }

        public void Start(string corePath, string romPath)
        {
            _bridge.LoadCoreLibrary(corePath);

            // Fetch the static video callback pointer straight from our VideoPipeline class
            IntPtr vPtr = (IntPtr)(delegate* unmanaged[Cdecl]<nint, uint, uint, nuint, void>)&VideoPipeline.ProcessFrame;
            IntPtr pPtr = (IntPtr)(delegate* unmanaged[Cdecl]<void>)&OnInputPoll;
            IntPtr sPtr = (IntPtr)(delegate* unmanaged[Cdecl]<uint, uint, uint, uint, short>)&OnInputState;

            _bridge.InitializeCore(vPtr, pPtr, sPtr);

            // Delegate ROM structural design out to the RomLoader class
            RetroGameInfo gameInfo = _romLoader.PrepareGameInfo(romPath);
            _bridge.LoadGame((IntPtr)(&gameInfo));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void UpdateFrameStates(UniversalInputPacket local, UniversalInputPacket remote)
        {
            _currentLocalInput = local;
            _currentRemoteInput = remote;

            _bridge.RunFrame();
        }

        public void Shutdown()
        {
            _bridge.Unload();
            _romLoader.CleanUp();
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static void OnInputPoll()
        {
            // Loop handles timing updates natively
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        private static short OnInputState(uint port, uint device, uint index, uint id)
        {
            // Route between player 1 (local) and player 2 (remote over NetworkLink)
            UniversalInputPacket activePacket = (port == 0) ? _currentLocalInput : _currentRemoteInput;

            // --- 1. HANDLE DIGITAL JOYPAD METRICS ---
            if (device == LibretroConstants.RETRO_DEVICE_JOYPAD)
            {
                ushort checkMask = id switch
                {
                    LibretroConstants.RETRO_DEVICE_ID_JOYPAD_B => (ushort)XInputButtonFlags.A,      // Libretro B maps to Xbox A
                    LibretroConstants.RETRO_DEVICE_ID_JOYPAD_A => (ushort)XInputButtonFlags.B,      // Libretro A maps to Xbox B
                    LibretroConstants.RETRO_DEVICE_ID_JOYPAD_Y => (ushort)XInputButtonFlags.X,      // Libretro Y maps to Xbox X
                    LibretroConstants.RETRO_DEVICE_ID_JOYPAD_X => (ushort)XInputButtonFlags.Y,      // Libretro X maps to Xbox Y

                    LibretroConstants.RETRO_DEVICE_ID_JOYPAD_START => (ushort)XInputButtonFlags.Start,
                    LibretroConstants.RETRO_DEVICE_ID_JOYPAD_SELECT => (ushort)XInputButtonFlags.Back,

                    LibretroConstants.RETRO_DEVICE_ID_JOYPAD_UP => (ushort)XInputButtonFlags.DPadUp,
                    LibretroConstants.RETRO_DEVICE_ID_JOYPAD_DOWN => (ushort)XInputButtonFlags.DPadDown,
                    LibretroConstants.RETRO_DEVICE_ID_JOYPAD_LEFT => (ushort)XInputButtonFlags.DPadLeft,
                    LibretroConstants.RETRO_DEVICE_ID_JOYPAD_RIGHT => (ushort)XInputButtonFlags.DPadRight,

                    10 => (ushort)XInputButtonFlags.LeftShoulder,  // Libretro L1 (ID 10)
                    11 => (ushort)XInputButtonFlags.RightShoulder, // Libretro R1 (ID 11)
                    14 => (ushort)XInputButtonFlags.LeftThumb,     // Libretro L3 (ID 14)
                    15 => (ushort)XInputButtonFlags.RightThumb,    // Libretro R3 (ID 15)
                    _ => 0
                };

                if (checkMask != 0)
                {
                    return (short)((activePacket.Buttons & checkMask) != 0 ? 1 : 0);
                }

                // --- DYNAMIC SLIDING TRIGGER MAPPING (0 to 255) ---
                // If Libretro asks for L2 (ID 12) or R2 (ID 13) within the Joypad device spectrum,
                // we pass back your universal 16-byte raw trigger byte values directly!
                if (id == 12) return activePacket.LeftTrigger;
                if (id == 13) return activePacket.RightTrigger;
            }

            // --- 2. HANDLE ANALOGUE THUMBSTICKS ---
            if (device == 2) // RETRO_DEVICE_ANALOG
            {
                if (index == 0) // Left Stick
                {
                    return id == 0 ? activePacket.LeftStickX : activePacket.LeftStickY;
                }
                if (index == 1) // Right Stick
                {
                    return id == 0 ? activePacket.RightStickX : activePacket.RightStickY;
                }
            }

            return 0;
        }
    }
}
