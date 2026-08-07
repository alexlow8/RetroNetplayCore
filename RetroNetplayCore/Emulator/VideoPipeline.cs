using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RetroNetplayCore.Emulator
{
    public unsafe class VideoPipeline
    {
        // Cache information about the active frame buffer for scaling or streaming
        private static byte* _latestFrameBufferPtr = null;
        private static uint _currentWidth = 0;
        private static uint _currentHeight = 0;
        private static nuint _currentPitch = UIntPtr.Zero;

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ProcessFrame(nint data, uint width, uint height, nuint pitch)
        {
            // Simple Software Approach: Core hands us a pointer to raw pixels
            if (data == nint.Zero) return;

            _latestFrameBufferPtr = (byte*)data.ToPointer();
            _currentWidth = width;
            _currentHeight = height;
            _currentPitch = pitch;

            // FUTURE UPGRADE HOOK: 
            // This is exactly where you will blit these raw pixels to your window canvas,
            // or decode the buffer to pipe raw video data over a network stream.
        }

        public static (IntPtr Pointer, uint Width, uint Height, int Pitch) GetLatestFrameContext()
        {
            return ((IntPtr)_latestFrameBufferPtr, _currentWidth, _currentHeight, (int)_currentPitch);
        }
    }
}
