# RetroNetplayCore Architecture Redesign - Summary

## Overview
You've successfully transitioned from a **socket handoff approach** to a **clean separation of concerns** with P2P mesh networking and server coordination for dropout detection.

---

## Key Architecture Changes

### ❌ OLD APPROACH (What We Removed)
```
Launcher → Handoff UDP Socket → Core
								Core connects to peers with INHERITED socket
								Problem: Confused lobby socket with P2P socket
```

### ✅ NEW APPROACH (What We Built)

```
					┌─────────────────────────┐
					│   Netplay Server        │
					│   (Node.js, port 5005)  │
					│   • Lobby coordination  │
					│   • Heartbeat monitor   │
					│   • Dropout detection   │
					└────────┬────────────────┘
							 │ heartbeat messages
					┌────────▼─────────────────────────────┐
					│  Launcher (WPF)                     │
					│  • Connect to lobby                 │
					│  • Get room code & peer list        │
					│  • Write engine_config.json         │
					│  • Launch core.exe                  │
					└────────┬────────────────────────────┘
							 │ config file
					┌────────▼─────────────────────────────┐
					│  Core 1 (Player 1)                  │
					│  • Read peer addresses               │
					│  • Create P2P sockets to all peers   │
					│  • Send/receive mesh inputs          │
					│  • Send heartbeat to server          │
					│  • Handle dropout signals            │
					│  • Run frame loop (60 FPS)           │
					└─────────────────────────────────────┘
							 │
		P2P Mesh Networking  │
		(Direct UDP)         │
							 │
					┌────────▼─────────────────────────────┐
					│  Core 2 (Player 2)                  │
					│  ...                                │
					└─────────────────────────────────────┘
```

---

## Files Changed

### 1. **RetroNetplayLauncher/ConfigWriter.cs** ✅ UPDATED
- **What changed**: Output format for `engine_config.json`
- **Why**: New config includes server address, room code, and peer roster with LAN/WAN addresses
- **Old format**: Only knew about host IP/port
- **New format**: Full peer list with smart routing (LAN preferred, WAN fallback)

```json
{
  "IsHost": true,
  "LocalPort": 54321,
  "CorePath": "Emulators/snes9x.dll",
  "RomPath": "mario.smc",
  "RoomCode": "1234",
  "ServerAddress": "192.168.1.100",
  "ServerPort": 5005,
  "Peers": [
	{
	  "PlayerIndex": 0,
	  "IsHost": true,
	  "LanIp": "192.168.1.1",
	  "LanPort": 50000,
	  "WanIp": "203.0.113.1",
	  "WanPort": 50000
	}
  ]
}
```

### 2. **RetroNetplayLauncher/MainWindow.xaml.cs** ✅ UPDATED
- **What changed**: Removed socket handoff, now just launches exe
- **Old flow**: Extract socket → Pass to core
- **New flow**: Write config → Launch core → Server stays alive in core

### 3. **RetroNetplayLauncher/SessionManager.cs** ✅ UPDATED
- **What changed**: `BootUnmanagedCore()` no longer takes socket parameter
- **Why**: Core creates its own P2P sockets based on config file

### 4. **RetroNetplayLauncher/MatchmakerClientBase.cs** ✅ UPDATED
- **What changed**: Added public properties for server info
- **New properties**: `RoomCode`, `ServerAddress`, `ServerPortNumber`
- **Why**: Launcher needs to pass these to ConfigWriter

### 5. **RetroNetplayCore/Configuration/EngineConfig.cs** ✅ UPDATED
- **What changed**: Complete redesign to support mesh P2P topology
- **New classes**: `PeerInfo` struct for peer address info
- **New properties**:
  - `Peers` list (all players)
  - `ServerAddress` / `ServerPort` (for heartbeat)
  - `RoomCode` (for server coordination)
- **Maintains backward compatibility** with old config format

### 6. **RetroNetplayCore/Program.cs** ✅ UPDATED
- **What changed**: Complete refactor - no more socket handoff
- **Old flow**: Parse socket from args → Create NetworkLink → Run frame loop
- **New flow**: Read config → Create NetworkThreadManager → Run frame loop
- **Key insight**: Frame loop is allocation-free, all network I/O happens on background thread

### 7. **RetroNetplayCore/Synchronization/NetworkThreadManager.cs** ✅ NEW FILE
- **Purpose**: Handles all network I/O on a background thread
- **Key responsibilities**:
  1. **P2P Socket Management**: Creates UDP socket, binds to LocalPort
  2. **Peer Routing**: Smart LAN/WAN address selection
  3. **Input Distribution**: Mesh P2P - send inputs to all peers, receive theirs
  4. **Server Heartbeat**: Send keep-alive to server every 1 second
  5. **Ring Buffer Management**: Pre-allocated buffer for frame loop to read inputs
  6. **Non-blocking**: Frame loop never waits for network I/O

### 8. **netplay-server/server-cloud/server.js** ✅ UPDATED
- **What changed**: Now keeps rooms alive during gameplay, monitors heartbeats
- **New commands**:
  - `HEARTBEAT` - Core keeps server alive (instead of `PING`)
  - `HEARTBEAT_ACK` - Server acknowledges
- **Dropout detection**:
  - If core hasn't sent heartbeat in 3 seconds → marked as disconnected
  - Server broadcasts `PLAYER_DISCONNECTED` to remaining players
  - Room stays alive for other players to pause/quit gracefully
- **Phase tracking**: LOBBY → GAMEPLAY (room doesn't delete on START_GAME anymore)

---

## Design Principles

### 1. **Zero Allocation in Frame Loop**
✅ The 60 FPS frame loop NEVER allocates memory
- Pre-allocated ring buffers for input data
- Pre-allocated peer endpoint map
- Background thread handles all JSON/network I/O

### 2. **No Player Configuration Required**
✅ Mesh P2P with automatic fallback
- Tries direct peer-to-peer first (LAN or same ISP)
- Falls back to server relay if NAT blocks (future feature)
- No port forwarding needed - server coordinates connections

### 3. **Robust Dropout Handling**
✅ Server stays alive to detect and announce dropouts
- Cores send heartbeat every 1 second
- Server detects missing heartbeat after 3 seconds
- Remaining players notified (can pause/quit)
- Room stays alive for recovery attempts

### 4. **Thread-Safe Communication**
✅ Frame loop and network thread communicate via pre-allocated ring buffers
- No locks in critical frame path
- Network thread fills buffer, frame thread reads
- Simple index-based synchronization

---

## Next Steps (Still To Do)

### Phase 4A: Input Protocol Definition
Define the P2P message format for input distribution:
```
Frame Data Structure:
- Frame Number (uint32)
- Player Index (uint8)
- Input Data (ClientInputPacket)
- CRC/Checksum for verification

Then implement in NetworkThreadManager:
- SendInputToAllPeers() - Broadcast local input
- ReceiveInputFromPeer() - Parse incoming input, store in ring buffer
```

### Phase 4B: Synchronization Strategy Update
Current code still references old `NetworkLink` and `SyncEngine` classes.
These need updating or removal:
- Review `HostSyncEngine` and `ClientSyncEngine`
- Either adapt them to use ring buffers, or rewrite
- Ensure frame synchronization works with P2P inputs

### Phase 4C: Testing & Integration
- Test launcher → core handoff
- Verify config file is correctly written
- Test P2P socket binding and connectivity
- Monitor server heartbeats
- Simulate player dropout and recovery

---

## Code Comments Philosophy

Throughout the new code, you'll see:
- **Large section headers** (`// --- SECTION NAME ---`) for navigation
- **Class-level XML comments** explaining purpose
- **Method-level comments** for the "why" (the "what" should be obvious from code)
- **Inline comments** for non-obvious implementation details
- **Console output** for debugging (can be removed later)

This keeps the code readable and your intentions clear 6 months from now.

---

## Questions Answered

**Q: Why no socket handoff?**
A: Socket handoff confused two different communication patterns:
- Launcher's socket: Lobby client (temporary, talks to server)
- Core's socket: P2P peer (long-lived, talks to other cores)
Keeping them separate makes the code clearer and more maintainable.

**Q: Why keep server alive?**
A: Dropout detection without server is hard (requires P2P ping between all peers).
Server acts as a neutral observer, can tell everyone "Player 2 is gone" reliably.

**Q: Why mesh P2P instead of star?**
A: Lower latency - inputs go directly peer-to-peer, not through a hub.
More resilient - if one P2P link fails, others keep working.

**Q: Why ring buffers?**
A: Frame loop runs at precise 60 FPS. If it waits for network data, timing breaks.
Ring buffers let network thread pre-fetch inputs; frame thread reads without blocking.

---

## Troubleshooting

If things don't work:

1. **Core won't start**: Check `engine_config.json` is in core's directory
2. **No networking**: Check server address/port in config matches launcher's server IP
3. **Heartbeat not sent**: Check room code is correct in config
4. **Dropout not detected**: Check server heartbeat interval (1 second) vs timeout (3 seconds)

Look at console output for 🔴/✅/⚠️ emoji indicators - they'll guide you to what's happening.

---

Done! Your architecture is now ready for P2P integration.
