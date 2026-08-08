using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RetroNetplayCore.Emulator
{
    public unsafe class AudioPipeline
    {
        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ProcessSample(short left, short right)
        {
            // STUB: Intentionally ignore single samples to maintain absolute maximum performance.
            // Real cores rely almost exclusively on the batch runner below.
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static nuint ProcessSampleBatch(short* data, nuint frames)
        {
            // STUB HOOK: This is exactly where you will eventually read the raw memory train 
            // from the data pointer to feed your local speakers or compress sound for netplay.

            // Instantly return the total frames count so the core stays running at full speed.
            return frames;
        }
    }
}
