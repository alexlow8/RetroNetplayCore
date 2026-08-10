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

        public static void WriteLaunchConfig(MatchmakerClientBase matchmaker, List<PeerEndpoint> peerRoster, string selectedCore, string romPath)
        {
            string relativeCorePath = Path.Combine("Emulators", selectedCore);
            object launchConfig;

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

            if (matchmaker.IsHost)
            {
                var clientsList = new List<object>();
                int assignedSlot = 1;

                foreach (var peer in peerRoster)
                {
                    if (peer.IsHost) continue;

                    // If the guest shares your public IP, they are on your local network
                    bool isLocalNetworkPeer = peer.Ip == myPublicIp || myPublicIp == "127.0.0.1";

                    string targetIp = isLocalNetworkPeer ? peer.InternalIp : peer.Ip;
                    int targetPort = isLocalNetworkPeer ? peer.InternalPort : peer.Port;

                    clientsList.Add(new { Slot = assignedSlot, IP = targetIp, Port = targetPort });
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
                    var hostPeer = peerRoster[0]; // Host is always index 0

                    // If you share the host's public IP, use their internal LAN details
                    bool isLocalNetworkPeer = hostPeer.Ip == myPublicIp || myPublicIp == "127.0.0.1";

                    hostIp = isLocalNetworkPeer ? hostPeer.InternalIp : hostPeer.Ip;
                    hostPort = isLocalNetworkPeer ? hostPeer.InternalPort : hostPeer.Port;
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

            string jsonString = JsonSerializer.Serialize(launchConfig, SerializerOptions);
            string targetConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "engine_config.json");
            File.WriteAllText(targetConfigPath, jsonString);
        }
    }
}
