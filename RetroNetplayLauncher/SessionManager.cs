using System;
using System.Diagnostics;

namespace RetroNetplayLauncher
{
    public static class SessionManager
    {
        public static void BootUnmanagedCore()
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "RetroNetplayCore.exe",
                WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory,
                UseShellExecute = true
            });
        }
    }
}
