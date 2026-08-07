#pragma warning disable CA1507 // Ignore nameof recommendations for JSON string literals
using System;
using System.IO;
using System.Text.Json;

namespace RetroNetplayCore.Configuration
{
    public class EngineConfig
    {
        public bool IsHost { get; set; } = true;
        public int LocalPort { get; set; } = 8000;
        public string HostAddress { get; set; } = "127.0.0.1";
        public int HostPort { get; set; } = 8000;
        public string CorePath { get; set; } = @"Emulators\MockLibretroCore.dll";
        public string RomPath { get; set; } = "dummy.z64";

        // Mapped slot table for 4 players (Slot 0 is always local host)
        public bool[] ActiveSlots { get; private set; } = [true, false, false, false];
        public string[] ClientIPs { get; private set; } = new string[4];
        public int[] ClientPorts { get; private set; } = new int[4];

        public static EngineConfig Load(string filePath)
        {
            EngineConfig config = new();

            if (!File.Exists(filePath))
            {
                Console.WriteLine("💡 No engine_config.json detected. Executing with default pipeline configurations.");
                return config;
            }

            try
            {
                string json = File.ReadAllText(filePath);
                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    JsonElement root = doc.RootElement;

                    config.IsHost = root.GetProperty("IsHost").GetBoolean();
                    config.LocalPort = root.GetProperty("LocalPort").GetInt32();
                    config.CorePath = root.GetProperty("CorePath").GetString() ?? config.CorePath;
                    config.RomPath = root.GetProperty("RomPath").GetString() ?? config.RomPath;

                    if (!config.IsHost)
                    {
                        config.HostAddress = root.GetProperty("HostAddress").GetString() ?? config.HostAddress;
                        config.HostPort = root.GetProperty("HostPort").GetInt32();
                    }
                    else if (root.TryGetProperty("Clients", out JsonElement clientsArray))
                    {
                        foreach (JsonElement client in clientsArray.EnumerateArray())
                        {
                            int slot = client.GetProperty("Slot").GetInt32();
                            if (slot >= 1 && slot <= 3)
                            {
                                config.ClientIPs[slot] = client.GetProperty("IP").GetString() ?? config.ClientIPs[slot];
                                config.ClientPorts[slot] = client.GetProperty("Port").GetInt32();
                                config.ActiveSlots[slot] = true;
                            }
                        }
                    }
                }
                Console.WriteLine("📥 Settings successfully loaded from engine_config.json");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Config parse error: {ex.Message}. Reverting to standard baseline configurations.");
            }

            return config;
        }
    }
}
