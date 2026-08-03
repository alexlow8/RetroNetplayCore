using System;
using System.Runtime.InteropServices;

namespace RetroNetplayCore.Emulator
{
    public unsafe class RomLoader
    {
        private IntPtr _allocatedPathPtr = IntPtr.Zero;

        public RetroGameInfo PrepareGameInfo(string romPath)
        {
            _allocatedPathPtr = Marshal.StringToHGlobalAnsi(romPath);

            // Simply pass the path string straight through.
            // Libretro cores expecting a file path will handle the OS file streaming internally.
            return new RetroGameInfo
            {
                Path = (byte*)_allocatedPathPtr.ToPointer(),
                Data = nint.Zero,
                Size = 0,
                Meta = null!
            };
        }

        public void CleanUp()
        {
            // Free the memory allocation handle when the game session shuts down
            if (_allocatedPathPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_allocatedPathPtr);
                _allocatedPathPtr = IntPtr.Zero;
            }
        }
    }
}
