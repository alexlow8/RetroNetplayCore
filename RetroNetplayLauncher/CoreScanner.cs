using System;
using System.IO;
using System.Collections.Generic;

namespace RetroNetplayLauncher
{
    public static class CoreScanner
    {
        public static readonly string EmulatorsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "emulators");

        public static List<string> ScanForCores()
        {
            if (!Directory.Exists(EmulatorsPath))
            {
                Directory.CreateDirectory(EmulatorsPath);
            }

            string[] files = Directory.GetFiles(EmulatorsPath, "*Core*.dll");
            if (files.Length == 0)
            {
                files = Directory.GetFiles(EmulatorsPath, "*.dll");
            }

            List<string> coreNames = [];
            foreach (string file in files)
            {
                coreNames.Add(Path.GetFileName(file));
            }
            return coreNames;
        }
    }
}
