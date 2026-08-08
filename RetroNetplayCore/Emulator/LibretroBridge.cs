using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RetroNetplayCore.Emulator
{
    public unsafe class LibretroBridge
    {
        private IntPtr _coreHandle;

        // Core Unmanaged Function Pointers
        private delegate* unmanaged[Cdecl]<IntPtr, void> _retroSetEnvironment;
        private delegate* unmanaged[Cdecl]<IntPtr, void> _retroSetVideoRefresh;
        private delegate* unmanaged[Cdecl]<IntPtr, void> _retroSetInputPoll;
        private delegate* unmanaged[Cdecl]<IntPtr, void> _retroSetInputState;
        private delegate* unmanaged[Cdecl]<void> _retroInit;
        private delegate* unmanaged[Cdecl]<void> _retroDeinit;
        private delegate* unmanaged[Cdecl]<IntPtr, void> _retroGetSystemInfo;
        private delegate* unmanaged[Cdecl]<IntPtr, bool> _retroLoadGame;
        private delegate* unmanaged[Cdecl]<void> _retroRun;
        private delegate* unmanaged[Cdecl]<IntPtr, void> _retroSetAudioSample;
        private delegate* unmanaged[Cdecl]<IntPtr, void> _retroSetAudioSampleBatch;


        public void LoadCoreLibrary(string dllPath)
        {
            _coreHandle = NativeLibrary.Load(dllPath);

            // Extract entry points directly to unmanaged pointers
            _retroSetEnvironment = (delegate* unmanaged[Cdecl]<IntPtr, void>)NativeLibrary.GetExport(_coreHandle, "retro_set_environment");
            _retroSetVideoRefresh = (delegate* unmanaged[Cdecl]<IntPtr, void>)NativeLibrary.GetExport(_coreHandle, "retro_set_video_refresh");
            _retroSetInputPoll = (delegate* unmanaged[Cdecl]<IntPtr, void>)NativeLibrary.GetExport(_coreHandle, "retro_set_input_poll");
            _retroSetInputState = (delegate* unmanaged[Cdecl]<IntPtr, void>)NativeLibrary.GetExport(_coreHandle, "retro_set_input_state");
            _retroInit = (delegate* unmanaged[Cdecl]<void>)NativeLibrary.GetExport(_coreHandle, "retro_init");
            _retroDeinit = (delegate* unmanaged[Cdecl]<void>)NativeLibrary.GetExport(_coreHandle, "retro_deinit");
            _retroGetSystemInfo = (delegate* unmanaged[Cdecl]<IntPtr, void>)NativeLibrary.GetExport(_coreHandle, "retro_get_system_info");
            _retroLoadGame = (delegate* unmanaged[Cdecl]<IntPtr, bool>)NativeLibrary.GetExport(_coreHandle, "retro_load_game");
            _retroRun = (delegate* unmanaged[Cdecl]<void>)NativeLibrary.GetExport(_coreHandle, "retro_run");
            _retroSetAudioSample = (delegate* unmanaged[Cdecl]<IntPtr, void>)NativeLibrary.GetExport(_coreHandle, "retro_set_audio_sample");
            _retroSetAudioSampleBatch = (delegate* unmanaged[Cdecl]<IntPtr, void>)NativeLibrary.GetExport(_coreHandle, "retro_set_audio_sample_batch");
        }

        public void InitializeCore(IntPtr environmentCallback, IntPtr videoCallback, IntPtr audioCallback, IntPtr audioBatchCallback, IntPtr pollCallback, IntPtr stateCallback)
        {
            _retroSetEnvironment(environmentCallback);
            _retroSetVideoRefresh(videoCallback);

            // Wire up our two new audio lines
            _retroSetAudioSample(audioCallback);
            _retroSetAudioSampleBatch(audioBatchCallback);

            _retroSetInputPoll(pollCallback);
            _retroSetInputState(stateCallback);

            _retroInit();
        }

        public bool LoadGame(IntPtr gameInfoPtr)
        {
            return _retroLoadGame(gameInfoPtr);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RunFrame()
        {
            _retroRun();
        }

        public void Unload()
        {
            _retroDeinit();
            NativeLibrary.Free(_coreHandle);
        }
    }
}
