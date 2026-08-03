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
            if (device != LibretroConstants.RETRO_DEVICE_JOYPAD) return 0;

            UniversalInputPacket activePacket = (port == 0) ? _currentLocalInput : _currentRemoteInput;

            ushort checkMask = id switch
            {
                LibretroConstants.RETRO_DEVICE_ID_JOYPAD_A => (ushort)XInputButtonFlags.A,
                LibretroConstants.RETRO_DEVICE_ID_JOYPAD_B => (ushort)XInputButtonFlags.B,
                LibretroConstants.RETRO_DEVICE_ID_JOYPAD_START => (ushort)XInputButtonFlags.Start,
                LibretroConstants.RETRO_DEVICE_ID_JOYPAD_SELECT => (ushort)XInputButtonFlags.Back,
                LibretroConstants.RETRO_DEVICE_ID_JOYPAD_UP => (ushort)XInputButtonFlags.DPadUp,
                LibretroConstants.RETRO_DEVICE_ID_JOYPAD_DOWN => (ushort)XInputButtonFlags.DPadDown,
                LibretroConstants.RETRO_DEVICE_ID_JOYPAD_LEFT => (ushort)XInputButtonFlags.DPadLeft,
                LibretroConstants.RETRO_DEVICE_ID_JOYPAD_RIGHT => (ushort)XInputButtonFlags.DPadRight,
                _ => 0
            };

            return (short)((activePacket.Buttons & checkMask) != 0 ? 1 : 0);
        }
    }
}
