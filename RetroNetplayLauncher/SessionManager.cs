using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace RetroNetplayLauncher
{
    // The class must be partial for LibraryImport source generation to function
    public static partial class SessionManager
    {
        // 1. Updated DuplicateHandle using modern compile-time source generation
        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool DuplicateHandle(
            IntPtr hSourceProcessHandle,
            IntPtr hSourceHandle,
            IntPtr hTargetProcessHandle,
            out IntPtr lpTargetHandle,
            uint dwDesiredAccess,
            [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle,
            uint dwOptions);

        // 2. FIXED: Changed 'extern' to 'partial' so the source generator processes it correctly
        [LibraryImport("kernel32.dll")]
        private static partial IntPtr GetCurrentProcess();

        private const uint DUPLICATE_SAME_ACCESS = 0x00000002;

        public static void BootUnmanagedCore(Socket launcherSocket)
        {
            Process coreProcess = new()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "RetroNetplayCore.exe",
                    WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = false
                }
            };

            coreProcess.Start();

            // Give the operating system a moment to register the new process handle 
            // space before duplicating the socket descriptors.
            System.Threading.Thread.Sleep(100);

            IntPtr targetSocketHandle = IntPtr.Zero;
            try
            {
                IntPtr sourceSocketHandle = launcherSocket.Handle;
                IntPtr currentProcessHandle = GetCurrentProcess();

                bool success = DuplicateHandle(
                    currentProcessHandle,
                    sourceSocketHandle,
                    coreProcess.Handle,
                    out targetSocketHandle,
                    0,
                    true,
                    DUPLICATE_SAME_ACCESS);

                if (!success)
                {
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Critical: Handle inheritance failed: {ex.Message}");
            }

            coreProcess.StartInfo.Arguments = $"--inherited-socket {targetSocketHandle.ToInt64()}";
        }
    }
}
