using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace RetroNetplayLauncher
{
    public class PeerEndpoint
    {
        // Public internet footprint
        [JsonPropertyName("ip")] public string Ip { get; set; } = "";
        [JsonPropertyName("port")] public int Port { get; set; }

        // Private local network footprint
        [JsonPropertyName("internalIp")] public string InternalIp { get; set; } = "";
        [JsonPropertyName("internalPort")] public int InternalPort { get; set; }

        [JsonPropertyName("isHost")] public bool IsHost { get; set; }
    }

    public class HostRequest
    {
        [JsonPropertyName("command")] public string Command { get; set; } = "HOST";
        [JsonPropertyName("maxPlayers")] public int MaxPlayers { get; set; }
        [JsonPropertyName("coreName")] public string CoreName { get; set; } = "";
        [JsonPropertyName("romPath")] public string RomPath { get; set; } = "";
        [JsonPropertyName("internalIp")] public string InternalIp { get; set; } = "";
        [JsonPropertyName("internalPort")] public int InternalPort { get; set; }
    }

    public class JoinRequest
    {
        [JsonPropertyName("command")] public string Command { get; set; } = "JOIN";
        [JsonPropertyName("code")] public string Code { get; set; } = "";
        [JsonPropertyName("internalIp")] public string InternalIp { get; set; } = "";
        [JsonPropertyName("internalPort")] public int InternalPort { get; set; }
    }

    public class LobbyUpdateMessage
    {
        [JsonPropertyName("status")] public string Status { get; set; } = "";
        [JsonPropertyName("playerIndex")] public int PlayerIndex { get; set; }
        [JsonPropertyName("coreName")] public string CoreName { get; set; } = "";
        [JsonPropertyName("romPath")] public string RomPath { get; set; } = "";
        [JsonPropertyName("peers")] public List<PeerEndpoint> Peers { get; set; } = [];
    }
}
