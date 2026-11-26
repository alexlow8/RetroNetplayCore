using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace RetroNetplayLauncher
{
    public static partial class SessionManager
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct STARTUPINFO
        {
            public uint cb;
            public string lpReserved;
            public string lpDesktop;
            public string lpTitle;
            public uint dwX;
            public uint dwY;
            public uint dwXSize;
            public uint dwYSize;
            public uint dwXCountChars;
            public uint dwYCountChars;
            public uint dwFillAttribute;
            public uint dwFlags;
            public ushort wShowWindow;
            public ushort cbReserved2;
            public IntPtr lpReserved2;
            public IntPtr hStdInput;
            public IntPtr hStdOutput;
            public IntPtr hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_INFORMATION
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public uint dwProcessId;
            public uint dwThreadId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WSADATA
        {
            public ushort wVersion;
            public ushort wHighVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)]
            public string szDescription;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 129)]
            public string szSystemStatus;
            public ushort iMaxSockets;
            public ushort iMaxUdpDg;
            public IntPtr lpVendorInfo;
        }

        [DllImport("ws2_32.dll", SetLastError = true, EntryPoint = "WSAStartup")]
        private static extern int WSAStartup(ushort wVersionRequested, out WSADATA lpWSAData);

        [DllImport("ws2_32.dll", SetLastError = true, EntryPoint = "WSASocketW")]
        private static extern IntPtr WSASocketW(int af, int type, int protocol, IntPtr lpProtocolInfo, uint g, uint dwFlags);

        [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "SetHandleInformation")]
        private static extern bool SetHandleInformation(IntPtr hObject, uint dwMask, uint dwFlags);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CreateProcess(
            string lpApplicationName,
            string lpCommandLine,
            IntPtr lpProcessAttributes,
            IntPtr lpThreadAttributes,
            bool bInheritHandles,
            uint dwCreationFlags,
            IntPtr lpEnvironment,
            string lpCurrentDirectory,
            ref STARTUPINFO lpStartupInfo,
            out PROCESS_INFORMATION lpProcessInformation);

        private const int AF_INET = 2;
        private const int SOCK_DGRAM = 2;
        private const int IPPROTO_UDP = 17;

        // CRITICAL WINSOCK STRUCT FLAGS
        private const uint WSA_FLAG_OVERLAPPED = 0x01; // Tells Winsock to allow async Completion Ports
        private const uint WSA_FLAG_INHERIT = 0x80;    // Tells Winsock to allow process handle handover
        private const uint HANDLE_FLAG_INHERIT = 0x00000001;

        public static Socket CreateInheritableUdpSocket()
        {
            int wsaResult = WSAStartup(0x0202, out _);
            if (wsaResult != 0)
            {
                throw new System.ComponentModel.Win32Exception(wsaResult, "Failed to initialize Winsock infrastructure.");
            }

            // FIXED: Bitwise combine BOTH the Overlapped and Inherit flags on creation.
            // This satisfies the .NET async engine while maintaining unmanaged process duplication rights!
            uint creationFlags = WSA_FLAG_OVERLAPPED | WSA_FLAG_INHERIT;

            IntPtr rawSocket = WSASocketW(AF_INET, SOCK_DGRAM, IPPROTO_UDP, IntPtr.Zero, 0, creationFlags);
            if (rawSocket == IntPtr.Zero || rawSocket.ToInt64() == -1)
            {
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }

            if (!SetHandleInformation(rawSocket, HANDLE_FLAG_INHERIT, HANDLE_FLAG_INHERIT))
            {
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }

            // Modern .NET core runtime requires targeting SafeSocketHandle rather than SafeFileHandle
            return new Socket(new SafeSocketHandle(rawSocket, ownsHandle: true));
        }

        public static void BootUnmanagedCore()
        {
            string appName = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RetroNetplayCore.exe");
            string commandLine = $"\"{appName}\"";

            STARTUPINFO si = new();
            si.cb = (uint)Marshal.SizeOf(si);
            PROCESS_INFORMATION pi = new();

            bool created = CreateProcess(
                appName,
                commandLine,
                IntPtr.Zero,
                IntPtr.Zero,
                false, // No handle inheritance needed
                0,
                IntPtr.Zero,
                AppDomain.CurrentDomain.BaseDirectory,
                ref si,
                out pi);

            if (!created)
            {
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }
        }
    }
}
