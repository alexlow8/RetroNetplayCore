using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace RetroNetplayLauncher
{
    // The payload sent to the server to request a lobby
    public class HostRequest
    {
        [JsonPropertyName("command")] public string Command { get; set; } = "HOST";
        [JsonPropertyName("maxPlayers")] public int MaxPlayers { get; set; }
    }

    // Individual player information returned by the server
    public class PeerEndpoint
    {
        [JsonPropertyName("ip")] public string Ip { get; set; } = "";
        [JsonPropertyName("port")] public int Port { get; set; }
        [JsonPropertyName("isHost")] public bool IsHost { get; set; }
    }

    // The unified update message the server broadcasts to all connected players
    public class LobbyUpdateMessage
    {
        [JsonPropertyName("status")] public string Status { get; set; } = "";
        [JsonPropertyName("playerIndex")] public int PlayerIndex { get; set; }
        [JsonPropertyName("peers")] public List<PeerEndpoint> Peers { get; set; } = [];
    }
}
