using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;

namespace RetroNetplayLauncher
{
    public static class ConfigWriter
    {
        // PERFORMANCE CACHE FIX: Reuse single immutable settings instance across the runtime lifecycle
        private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

        public static void WriteLaunchConfig(MatchmakerClient matchmaker, List<PeerEndpoint> peerRoster, string selectedCore, string romPath)
        {
            string relativeCorePath = Path.Combine("Emulators", selectedCore);
            object launchConfig;

            if (matchmaker.IsHost)
            {
                var clientsList = new List<object>();
                int assignedSlot = 1;

                foreach (var peer in peerRoster)
                {
                    if (peer.IsHost) continue;
                    clientsList.Add(new { Slot = assignedSlot, IP = peer.Ip, peer.Port });
                    assignedSlot++;
                }

                launchConfig = new
                {
                    IsHost = true,
                    matchmaker.LocalPort,
                    CorePath = relativeCorePath,
                    RomPath = romPath,
                    Clients = clientsList
                };
            }
            else
            {
                string hostIp = "127.0.0.1";
                int hostPort = 8000;

                if (peerRoster.Count > 0)
                {
                    hostIp = peerRoster[0].Ip;
                    hostPort = peerRoster[0].Port;
                }

                launchConfig = new
                {
                    IsHost = false,
                    matchmaker.LocalPort,
                    HostAddress = hostIp,
                    HostPort = hostPort,
                    CorePath = relativeCorePath,
                    RomPath = romPath
                };
            }

            // Reference our statically cached serialization options here
            string jsonString = JsonSerializer.Serialize(launchConfig, SerializerOptions);
            string targetConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "engine_config.json");

            File.WriteAllText(targetConfigPath, jsonString);
        }
    }
}
