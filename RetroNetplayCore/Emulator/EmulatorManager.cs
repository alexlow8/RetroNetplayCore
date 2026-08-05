using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RetroNetplayCore.Emulator
{
    public unsafe class EmulatorManager
    {
        private readonly LibretroBridge _bridge;
        private readonly RomLoader _romLoader;

        // Unified 52-byte master frame layout accessible from the static unmanaged P/Invoke callback
        private static HostFramePacket _activeMasterPacket;

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

        /// <summary>
        /// Feeds the unified 4-player master synchronization frame directly into the static tracker and triggers the emulation step.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void UpdateFrameStates(ref HostFramePacket masterPacket)
        {
            // Direct structural block copy over memory - zero allocations
            _activeMasterPacket = masterPacket;

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
            // Bounds check to ensure we only read inside our 4 controller ports
            if (port > 3) return 0;

            // Route dynamically by Port ID straight to our inner network sub-structures
            PlayerInputState playerState = port switch
            {
                0 => _activeMasterPacket.Player0,
                1 => _activeMasterPacket.Player1,
                2 => _activeMasterPacket.Player2,
                3 => _activeMasterPacket.Player3,
                _ => default
            };

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

                    // Libretro ID 16 is explicitly reserved for the Menu/Guide/Home button interface
                    16 => 0x0400, // 0x0400 matches your XINPUT_GAMEPAD_GUIDE bitmask
                    _ => 0
                };

                if (checkMask != 0)
                {
                    return (short)((playerState.Buttons & checkMask) != 0 ? 1 : 0);
                }

                // --- DYNAMIC SLIDING TRIGGER MAPPING (0 to 255) ---
                if (id == 12) return playerState.LeftTrigger;
                if (id == 13) return playerState.RightTrigger;
            }

            // --- 2. HANDLE ANALOGUE THUMBSTICKS ---
            if (device == 2) // RETRO_DEVICE_ANALOG
            {
                if (index == 0) // Left Stick
                {
                    return id == 0 ? playerState.LeftStickX : playerState.LeftStickY;
                }
                if (index == 1) // Right Stick
                {
                    return id == 0 ? playerState.RightStickX : playerState.RightStickY;
                }
            }

            return 0;
        }
    }
}
