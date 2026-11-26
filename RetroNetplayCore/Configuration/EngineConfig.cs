#pragma warning disable CA1507 // Ignore nameof recommendations for JSON string literals
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace RetroNetplayCore.Configuration
{
    /// <summary>
    /// Represents a single peer in the netplay session (another player's core).
    /// Each peer has both LAN (internal) and WAN (public) addresses for smart routing.
    /// </summary>
    public class PeerInfo
    {
        // The player's index in the game (0 = first player, 1 = second, etc.)
        public int PlayerIndex { get; set; }

        // Whether this peer is running as the host
        public bool IsHost { get; set; }

        // Local network address (use this if both cores are on same LAN)
        public string LanIp { get; set; } = "127.0.0.1";
        public int LanPort { get; set; }

        // Internet address (use this if peer is on different network)
        public string WanIp { get; set; } = "127.0.0.1";
        public int WanPort { get; set; }
    }

    /// <summary>
    /// Main configuration loaded from engine_config.json.
    /// Tells the core: who it is, where the game is, and how to reach other players.
    /// </summary>
    public class EngineConfig
    {
        // --- LOCAL INSTANCE INFO ---
        /// <summary>Whether this core is the host (synchronization leader) or guest (follows host)</summary>
        public bool IsHost { get; set; } = true;

        /// <summary>UDP port this core will bind to for peer-to-peer connections</summary>
        public int LocalPort { get; set; } = 8000;

        // --- GAME INFO ---
        /// <summary>Path to the libretro core DLL to load (e.g., Emulators/snes9x.dll)</summary>
        public string CorePath { get; set; } = @"Emulators\MockLibretroCore.dll";

        /// <summary>Path to the ROM file to load (e.g., games/mario.smc)</summary>
        public string RomPath { get; set; } = "dummy.z64";

        // --- SERVER COORDINATION (for dropouts and relay fallback) ---
        /// <summary>Address of the netplay server (keeps room alive during gameplay)</summary>
        public string ServerAddress { get; set; } = "127.0.0.1";

        /// <summary>Port of the netplay server</summary>
        public int ServerPort { get; set; } = 5005;

        /// <summary>Room code for this session (used in heartbeat messages)</summary>
        public string RoomCode { get; set; } = "";

        // --- PEER ROSTER ---
        /// <summary>All players in this session (index 0 is always the host, then guests by join order)</summary>
        public List<PeerInfo> Peers { get; set; } = [];

        // --- LEGACY COMPATIBILITY (for old config format) ---
        // These are kept for backward compatibility with old configs
        public string HostAddress { get; set; } = "127.0.0.1";
        public int HostPort { get; set; } = 8000;
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

                    // Load basic core information
                    config.IsHost = root.GetProperty("IsHost").GetBoolean();
                    config.LocalPort = root.GetProperty("LocalPort").GetInt32();
                    config.CorePath = root.GetProperty("CorePath").GetString() ?? config.CorePath;
                    config.RomPath = root.GetProperty("RomPath").GetString() ?? config.RomPath;

                    // Load server coordination info (for heartbeat/dropout detection)
                    if (root.TryGetProperty("ServerAddress", out JsonElement serverAddr))
                        config.ServerAddress = serverAddr.GetString() ?? config.ServerAddress;

                    if (root.TryGetProperty("ServerPort", out JsonElement serverPort))
                        config.ServerPort = serverPort.GetInt32();

                    if (root.TryGetProperty("RoomCode", out JsonElement roomCode))
                        config.RoomCode = roomCode.GetString() ?? config.RoomCode;

                    // Load peer roster (all players in this session, both LAN and WAN addresses)
                    if (root.TryGetProperty("Peers", out JsonElement peersArray))
                    {
                        foreach (JsonElement peerElem in peersArray.EnumerateArray())
                        {
                            var peer = new PeerInfo
                            {
                                PlayerIndex = peerElem.GetProperty("PlayerIndex").GetInt32(),
                                IsHost = peerElem.GetProperty("IsHost").GetBoolean(),
                                LanIp = peerElem.GetProperty("LanIp").GetString() ?? "127.0.0.1",
                                LanPort = peerElem.GetProperty("LanPort").GetInt32(),
                                WanIp = peerElem.GetProperty("WanIp").GetString() ?? "127.0.0.1",
                                WanPort = peerElem.GetProperty("WanPort").GetInt32()
                            };
                            config.Peers.Add(peer);
                        }
                    }
                    // If no Peers array, load legacy format (backward compatibility)
                    else if (!config.IsHost && root.TryGetProperty("HostAddress", out JsonElement legacyHost))
                    {
                        config.HostAddress = legacyHost.GetString() ?? config.HostAddress;
                        config.HostPort = root.GetProperty("HostPort").GetInt32();
                    }
                    else if (config.IsHost && root.TryGetProperty("Clients", out JsonElement clientsArray))
                    {
                        // Legacy host format
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
                Console.WriteLine($"📥 Settings successfully loaded from {filePath}");
                Console.WriteLine($"   👤 Peers: {config.Peers.Count} | 🌐 Server: {config.ServerAddress}:{config.ServerPort} | 📍 Room: {config.RoomCode}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Config parse error: {ex.Message}. Reverting to standard baseline configurations.");
            }

            return config;
        }
    }
}
