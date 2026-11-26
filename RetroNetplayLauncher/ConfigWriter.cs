using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;
using RetroNetplayLauncher.MatchmakerClient;

namespace RetroNetplayLauncher
{
    public static class ConfigWriter
    {
        private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

        public static void WriteLaunchConfig(MatchmakerClientBase matchmaker, List<PeerEndpoint> peerRoster, string selectedCore, string romPath, string serverAddress, int serverPort, string roomCode)
        {
            string relativeCorePath = Path.Combine("Emulators", selectedCore);

            // Find your own public footprint dynamically from the roster mapping
            string myPublicIp = "127.0.0.1";
            foreach (var peer in peerRoster)
            {
                // Identify your own entry based on port signatures
                if (peer.InternalPort == matchmaker.LocalPort && peer.InternalIp == matchmaker.LocalIpAddress)
                {
                    myPublicIp = peer.Ip;
                    break;
                }
            }

            // Build peer list with both LAN and WAN addresses
            var peersList = new List<object>();
            int playerIndex = 0;

            foreach (var peer in peerRoster)
            {
                peersList.Add(new
                {
                    PlayerIndex = playerIndex,
                    IsHost = peer.IsHost,
                    LanIp = peer.InternalIp,
                    LanPort = peer.InternalPort,
                    WanIp = peer.Ip,
                    WanPort = peer.Port
                });
                playerIndex++;
            }

            // Create unified config for both host and guest
            var launchConfig = new
            {
                IsHost = matchmaker.IsHost,
                LocalPort = matchmaker.LocalPort,
                CorePath = relativeCorePath,
                RomPath = romPath,
                RoomCode = roomCode,
                ServerAddress = serverAddress,
                ServerPort = serverPort,
                Peers = peersList
            };

            string jsonString = JsonSerializer.Serialize(launchConfig, SerializerOptions);
            string targetConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "engine_config.json");
            File.WriteAllText(targetConfigPath, jsonString);
        }
    }
}
