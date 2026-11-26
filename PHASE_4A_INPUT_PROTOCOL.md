# Phase 4A: Input Protocol Specification

## Overview

This document defines the **binary protocol for peer-to-peer (P2P) input exchange** between netplay cores. The protocol supports:

- ✅ **Handshake phase** (pre-game synchronization)
- ✅ **Input frames** with explicit frame numbering for rollback detection
- ✅ **State snapshots** (optional, only on cores supporting serialization)
- ✅ **Confirmation tracking** (which peers have confirmed which frames)
- ✅ **Graceful fallback** to frame-sync-only mode for cores without serialization

---

## Part 1: Message Types & Binary Layout

### Message Header (All Messages)
```
Bytes  Type      Field              Purpose
-----  --------  -----------------  ----------------------------------------
0      byte      MessageType        Identifies packet type (see below)
1-4    uint32    SenderIndex        Who sent this? (0-3 for 4 players)
5-8    uint32    FrameNumber        Frame # this message relates to
9-12   uint32    Checksum           CRC32 of payload (for corruption detection)
13-14  uint16    PayloadLength      Bytes of payload following header
---    ---       PAYLOAD            Varies by MessageType
```

**Total Header: 15 bytes**

### Message Type Enum

```csharp
public enum P2PMessageType : byte
{
	HANDSHAKE = 1,              // Pre-game: "I'm ready to play"
	INPUT_FRAME = 2,            // Gameplay: "Here's my input for frame N"
	STATE_SNAPSHOT = 3,         // Rollback: "Here's my emulator state at frame N"
	FRAME_CONFIRMATION = 4,     // Acknowledgment: "I received your frame N"
	ACK = 5,                    // Generic acknowledgment
	ERROR = 255                 // Error condition
}
```

---

## Part 2: Message Formats (Detailed)

### 2.1 HANDSHAKE (MessageType = 1)

**When:** Before frame loop starts (Phase 4B)  
**Who sends:** Every core sends to every other core  
**Purpose:** Confirm all peers are present and have P2P connectivity

```
Byte Offset  Type        Field                Description
-----------  ---------   ----------------     ----------------------------------------
0            byte        MessageType = 1      
1-4          uint32      SenderIndex          0-3
5-8          uint32      FrameNumber = 0      (not used in handshake, set to 0)
9-12         uint32      Checksum             CRC32({payload})
13-14        uint16      PayloadLength        = 32 (fixed)
15-30        byte[16]    LocalMACAddress      MAC address of sender (for LAN detection)
31-34        uint32      LocalPublicIP        Sender's public IP (network byte order)
35-36        uint16      LocalPublicPort      Sender's P2P port on public network
37-40        uint32      LocalLANIP           Sender's LAN IP (if applicable)
41-42        uint16      LocalLANPort         Sender's LAN port (if applicable)
43-46        uint32      CoreVersion          Version of netplay code (optional, for compat check)
```

**Payload Size: 32 bytes**  
**Typical round-trip:** 100-200ms for all 4 cores to exchange handshakes  

**Reception logic:**
```
if (received[frame#0]) {
	confirmationMask |= (1 << senderIndex);  // Mark this peer as "connected"
}
when (confirmationMask == 0b1111) {
	// All 4 peers connected! Proceed to frame loop.
}
```

---

### 2.2 INPUT_FRAME (MessageType = 2)

**When:** Every frame during gameplay  
**Who sends:** Every core sends to every other core  
**Purpose:** Share local input state; enables rollback detection  
**Frequency:** 60 times per second (1 per frame @ 60 FPS)

```
Byte Offset  Type        Field                Description
-----------  ---------   ----------------     ----------------------------------------
0            byte        MessageType = 2      
1-4          uint32      SenderIndex          0-3
5-8          uint32      FrameNumber          Frame # this input is for (e.g., 1523)
9-12         uint32      Checksum             CRC32({payload})
13-14        uint16      PayloadLength        Variable; typically 8-32 bytes
15-N         byte[]      InputData            Button presses, analog sticks, triggers
											  Format depends on core (see Part 3)
```

**InputData Layout (Core-Independent, 8 bytes minimum):**
```
Byte Offset  Field                     Description
-----------  -----------------------   ----------------------------------------
0-1          uint16 ButtonMask         Bitfield: A, B, X, Y, L, R, Start, Select, etc.
2            sbyte  AnalogLStickX      -128 to +127
3            sbyte  AnalogLStickY      -128 to +127
4            sbyte  AnalogRStickX      -128 to +127
5            sbyte  AnalogRStickY      -128 to +127
6            byte   LeftTrigger        0-255
7            byte   RightTrigger       0-255
```

**InputData Extension (Cores with more buttons):**
```
8-9          uint16 ExtendedButtons   For cores with >16 standard buttons
10-N         byte[] CoreSpecific      Additional core-specific inputs
```

**Reception logic:**
```csharp
// Pseudo-code
var input = DeserializeInputFrame(packet);

if (input.FrameNumber < executedFrame)
{
	// Late arrival for a frame we already executed!
	if (input.InputData != ringBuffer[input.FrameNumber][input.SenderIndex])
	{
		// INPUT CHANGED! This requires rollback.
		TriggerRollback(input.FrameNumber);
	}
}
else
{
	// Normal case: input for a future frame or current frame
	ringBuffer[input.FrameNumber][input.SenderIndex] = input.InputData;
	confirmationMask[input.FrameNumber] |= (1 << input.SenderIndex);
}
```

---

### 2.3 STATE_SNAPSHOT (MessageType = 3)

**When:** Only on rollback-capable cores  
**Who sends:** Every core sends snapshots (every 10 frames by default)  
**Purpose:** Enable re-execution after rollback detection  
**Frequency:** 6 times per second (every 10 frames @ 60 FPS)

```
Byte Offset  Type        Field                Description
-----------  ---------   ----------------     ----------------------------------------
0            byte        MessageType = 3      
1-4          uint32      SenderIndex          0-3
5-8          uint32      FrameNumber          Frame # when snapshot was taken
9-12         uint32      Checksum             CRC32({payload})
13-14        uint16      PayloadLength        Size of serialized state (variable)
15-18        uint32      StateSize            Redundant with PayloadLength (for safety)
19-N         byte[]      SerializedState      Output of retro_serialize()
```

**StateSize Examples:**
- SNES (Snes9x): ~20-50 KB
- Genesis (Genesis Plus GX): ~30-60 KB
- N64 (Mupen64Plus): ~1-2 MB (may need compression)
- NES (Nestopia): ~5-10 KB

**Reception logic:**
```csharp
// Pseudo-code
var snapshot = DeserializeStateSnapshot(packet);

// Store in state manager (pre-allocated ring buffer)
stateManager.StoreSnapshot(snapshot.FrameNumber, snapshot.SerializedState);

// Now if rollback is needed:
//   RollbackToFrame(targetFrame) → 
//   Load state from frame (targetFrame - targetFrame % 10) →
//   Re-execute 1-10 frames with corrected inputs
```

---

### 2.4 FRAME_CONFIRMATION (MessageType = 4)

**When:** Optional, only if explicit acknowledgment is desired  
**Who sends:** Every core (can send bulk confirmations)  
**Purpose:** Acknowledge receipt of frame inputs from all peers  
**Frequency:** Less frequent than INPUT_FRAME (e.g., every 10 frames)

```
Byte Offset  Type        Field                Description
-----------  ---------   ----------------     ----------------------------------------
0            byte        MessageType = 4      
1-4          uint32      SenderIndex          0-3
5-8          uint32      FrameNumber          Highest frame confirmed by sender
9-12         uint32      Checksum             CRC32({payload})
13-14        uint16      PayloadLength        = 0 (no additional payload)
```

**Purpose:** When frame N is "confirmed," it means the sender has received input from ALL OTHER peers for frame N. After confirmation, we know frame N can never change (no retroactive corrections).

**Reception logic:**
```csharp
// Pseudo-code
var confirm = DeserializeFrameConfirmation(packet);

// Mark frame as confirmed in state tracker
ringBuffer[confirm.FrameNumber].PeerConfirmations |= (1 << confirm.SenderIndex);

// Check if ALL peers have confirmed this frame
if (ringBuffer[confirm.FrameNumber].AllPeersConfirmed)
{
	highestConfirmedFrame = Math.Max(highestConfirmedFrame, confirm.FrameNumber);
}
```

**Usage:** Primarily for diagnostics and debugging. Normal frame-sync logic doesn't strictly need this.

---

### 2.5 ACK (MessageType = 5)

**When:** Generic acknowledgment  
**Purpose:** Handshake response or error recovery  

```
Byte Offset  Type        Field                Description
-----------  ---------   ----------------     ----------------------------------------
0            byte        MessageType = 5      
1-4          uint32      SenderIndex          0-3
5-8          uint32      FrameNumber          Frame being acknowledged
9-12         uint32      Checksum             CRC32({payload})
13-14        uint16      PayloadLength        Variable
15-N         byte[]      Payload              ACK-specific data (if any)
```

---

## Part 3: Input Data Mapping (Core-Independent)

### Standard Input Format (Bytes 15-22 of INPUT_FRAME)

```csharp
public struct StandardInput
{
	// Button mask (2 bytes)
	public ushort Buttons { get; set; }  // Bitfield

	// Analog sticks (-128 to +127)
	public sbyte LeftStickX { get; set; }
	public sbyte LeftStickY { get; set; }
	public sbyte RightStickX { get; set; }
	public sbyte RightStickY { get; set; }

	// Triggers (0 to 255)
	public byte LeftTrigger { get; set; }
	public byte RightTrigger { get; set; }
}

// Button bitfield layout (ushort, little-endian)
[Flags]
public enum StandardButtons : ushort
{
	B = 0x0001,          // 1
	Y = 0x0002,          // 2
	Select = 0x0004,     // 4
	Start = 0x0008,      // 8
	DPadUp = 0x0010,     // 16
	DPadDown = 0x0020,   // 32
	DPadLeft = 0x0040,   // 64
	DPadRight = 0x0080,  // 128
	A = 0x0100,          // 256
	X = 0x0200,          // 512
	L = 0x0400,          // 1024
	R = 0x0800,          // 2048
	L2 = 0x1000,         // 4096
	R2 = 0x2000,         // 8192
	L3 = 0x4000,         // 16384 (stick click)
	R3 = 0x8000,         // 32768 (stick click)
}
```

**Core-Specific Extensions (Bytes 23+ if needed):**
- SNES: No extensions needed
- Genesis: No extensions needed
- N64: Rumble pak status (1 byte)
- PlayStation: Extended button set (2 more bytes)

---

## Part 4: Capability Detection & Mode Selection

### 4.1 Detecting Serialization Support

**At core load time (Program.cs):**

```csharp
/// <summary>
/// Detects if the loaded Libretro core supports save-state serialization.
/// This is required for rollback netplay.
/// </summary>
public bool DetectSerializationSupport(IntPtr coreHandle)
{
	// Query for required Libretro functions
	IntPtr serializeSizePtr = GetProcAddress(coreHandle, "retro_serialize_size");
	IntPtr serializePtr = GetProcAddress(coreHandle, "retro_serialize");
	IntPtr unserializePtr = GetProcAddress(coreHandle, "retro_unserialize");

	bool supportsRollback = (serializeSizePtr != IntPtr.Zero && 
							 serializePtr != IntPtr.Zero && 
							 unserializePtr != IntPtr.Zero);

	if (supportsRollback)
	{
		Console.WriteLine("✓ Rollback enabled (core supports serialization)");
		return true;
	}
	else
	{
		Console.WriteLine("⚠ Rollback disabled (core doesn't support serialization)");
		Console.WriteLine("  Using frame-sync-only mode. Network must be <50ms latency.");
		return false;
	}
}
```

---

### 4.2 Rollback Mode (Full Implementation)

**Prerequisites:**
- Core supports `retro_serialize`, `retro_unserialize`, `retro_serialize_size`
- Phase 4C: State Snapshots are available
- Phase 4D: RollbackFrameInfo tracking is active

**Behavior:**
1. **Handshake (Phase 4B):** Confirm all peers connected ✓
2. **Frame loop:** Execute frames speculatively (don't wait for all peers)
3. **Incoming late packet:** Trigger rollback if input differs
4. **Re-execution:** Load snapshot, replay frames with corrected input
5. **Continue:** Forward from new execution point

**Timeline:**
```
Frame #100 (executedFrame=100):
  Peers 1,2,3 input → Execute frame #100

Frame #101 (executedFrame=101):
  Peers 1,2,3 input → Execute frame #101

Frame #101 + 50ms:
  Peer 4's packet FINALLY arrives for frame #100
  Input is DIFFERENT!

  → ROLLBACK TRIGGERED:
	  Load snapshot from frame #90 (10-frame boundary)
	  Re-execute frames 91-100 with correct peer-4 input for #100
	  Re-execute frame #101 with peer-4 input
	  executedFrame = 101 (continue forward)
```

**Latency tolerance:** ±500ms (worst case)  
**User experience:** Invisible if rollback <50ms (which is typical)

---

### 4.3 Frame-Sync-Only Mode (Fallback)

**Prerequisites:**
- Core does NOT support serialization
- No state snapshots available

**Behavior:**
1. **Handshake (Phase 4B):** Confirm all peers connected ✓
2. **Frame loop:** Collect inputs from ALL peers
3. **Timeout (50ms):** If any peer hasn't sent input, use their last known input
4. **Execute:** Only after all inputs collected (or timeout)
5. **No rollback:** Once frame is executed, it's permanent

**Timeline:**
```
Frame #100 (waiting for all inputs):
  Peer 1 input @ T=0ms ✓
  Peer 2 input @ T=5ms ✓
  Peer 3 input @ T=10ms ✓
  Peer 4 input @ T=45ms ✓ (slow but within timeout)

  → ALL INPUTS READY
  → Execute frame #100

Frame #101 (waiting for all inputs):
  Peer 1 input @ T=0ms ✓
  Peer 2 input @ T=5ms ✓
  Peer 3 input @ T=10ms ✓
  Peer 4 input @ T=55ms ✗ (TIMEOUT at 50ms)

  → TIMEOUT REACHED
  → Use peer 4's last known input from frame #100
  → Execute frame #101 with partial data
```

**Latency tolerance:** ±50ms (packet loss = jank)  
**User experience:** Noticeable if network jitter >50ms (feels sluggish, but playable)

---

## Part 5: Thread Safety & Allocation Strategy

### 5.1 Ring Buffer Access

**Assumption:** Single writer (network thread), single reader (frame thread)

```csharp
// Thread-safe WITHOUT locks (single-writer, single-reader pattern)
public class RingBuffer
{
	private FrameData[] buffer = new FrameData[256];  // Pre-allocated

	// Network thread writes (safe, it's the only writer)
	public void WriteFrameInput(uint frameNumber, byte playerIndex, byte[] input)
	{
		int index = (int)(frameNumber & 0xFF);  // Modulo 256
		buffer[index].InputData[playerIndex] = input;  // No lock needed!
	}

	// Frame thread reads (safe, network thread doesn't read)
	public byte[] ReadFrameInput(uint frameNumber, byte playerIndex)
	{
		int index = (int)(frameNumber & 0xFF);  // Same modulo
		return buffer[index].InputData[playerIndex];  // No lock needed!
	}
}
```

**Guarantees:**
- ✅ No locks required (lock-free)
- ✅ No allocations in hot path
- ✅ Cache-friendly (sequential array access)
- ⚠️ Frame number must monotonically increase (can't execute frame #200 then #100)

---

### 5.2 State Snapshots (Pre-Allocated)

```csharp
public class StateSnapshotManager
{
	// Pre-allocate for 256 frames of history (at 60 FPS = 4.3 seconds)
	// Snapshot every 10 frames → 26 snapshot slots (256 / 10)
	private byte[][] snapshots = new byte[26][];

	// Allocate at startup (not during gameplay!)
	public void Initialize(ulong stateSize)
	{
		for (int i = 0; i < snapshots.Length; i++)
		{
			snapshots[i] = new byte[stateSize];  // Once at startup
		}
		Console.WriteLine($"Pre-allocated {stateSize * 26 / 1024} KB for snapshots");
	}

	// Network thread saves snapshot (every 10 frames)
	public void SaveSnapshot(uint frameNumber, IntPtr statePtr, ulong stateSize)
	{
		int index = (int)((frameNumber / 10) % snapshots.Length);  // Which slot?
		Marshal.Copy(statePtr, snapshots[index], 0, (int)stateSize);  // No new allocation
	}

	// Frame thread loads snapshot for rollback
	public IntPtr LoadSnapshot(uint frameNumber)
	{
		int index = (int)((frameNumber / 10) % snapshots.Length);  // Which slot?
		return Marshal.AllocHGlobal(snapshots[index].Length);  // Frame thread only!
	}
}
```

**Guarantees:**
- ✅ All allocations happen at startup (Initialize)
- ✅ No allocations during gameplay or rollback
- ✅ Frame thread can safely allocate for rollback (it's not 60 FPS critical)

---

## Part 6: Error Handling

### Checksum Validation

```csharp
// When serializing a message:
byte[] payload = SerializePayload(message);
uint checksum = CRC32(payload);  // Using standard CRC32 polynomial
message.Checksum = checksum;

// When deserializing:
byte[] payload = packet.GetPayload();
uint expectedChecksum = packet.Checksum;
uint calculatedChecksum = CRC32(payload);

if (calculatedChecksum != expectedChecksum)
{
	Console.WriteLine($"Checksum mismatch! Dropping packet (corruption detected)");
	return false;  // Discard packet
}
```

### Out-of-Order Packets

```csharp
// Peer sends: Frame #100, Frame #101, Frame #102
// We receive: Frame #101, Frame #102, Frame #100 (reordering!)

// No problem! We store by frame number, not receive order:
ringBuffer[100].InputData = ...  // Stored regardless of arrival order
ringBuffer[101].InputData = ...
ringBuffer[102].InputData = ...
```

### Late Handshake (Peer Joins Late)

```csharp
// If peer sends HANDSHAKE after frame loop started:
if (incomingMessage.Type == HANDSHAKE && currentFrame > 0)
{
	// Decide: Accept late joiner or reject?
	// For now: REJECT (don't support mid-game joins)
	Console.WriteLine($"Peer joined after game started. Closing connection.");
	// Could extend this in Phase 4E for join-in-progress
}
```

---

## Part 7: Message Serialization Examples

### Example 1: Serialize INPUT_FRAME

```csharp
public byte[] SerializeInputFrame(uint frameNumber, byte playerIndex, 
								   ushort buttons, sbyte lx, sbyte ly, 
								   sbyte rx, sbyte ry, byte lt, byte rt)
{
	byte[] packet = new byte[31];  // Header(15) + Input(8) + padding

	// Header
	packet[0] = (byte)P2PMessageType.INPUT_FRAME;
	BitConverter.GetBytes(playerIndex).CopyTo(packet, 1);
	BitConverter.GetBytes(frameNumber).CopyTo(packet, 5);

	// Payload (8 bytes of input)
	BitConverter.GetBytes(buttons).CopyTo(packet, 15);
	packet[17] = (byte)lx;
	packet[18] = (byte)ly;
	packet[19] = (byte)rx;
	packet[20] = (byte)ry;
	packet[21] = lt;
	packet[22] = rt;

	// Checksum (payload only: bytes 15-22)
	uint checksum = CRC32(packet, 15, 8);
	BitConverter.GetBytes(checksum).CopyTo(packet, 9);

	// Length
	packet[13] = 8;
	packet[14] = 0;

	return packet;
}
```

### Example 2: Deserialize STATE_SNAPSHOT

```csharp
public StateSnapshot DeserializeStateSnapshot(byte[] packet)
{
	var snapshot = new StateSnapshot
	{
		MessageType = packet[0],
		SenderIndex = BitConverter.ToUInt32(packet, 1),
		FrameNumber = BitConverter.ToUInt32(packet, 5),
		Checksum = BitConverter.ToUInt32(packet, 9),
		PayloadLength = BitConverter.ToUInt16(packet, 13),
		StateSize = BitConverter.ToUInt32(packet, 15),
	};

	// Extract serialized state (starts at byte 19)
	snapshot.SerializedState = new byte[snapshot.StateSize];
	Array.Copy(packet, 19, snapshot.SerializedState, 0, snapshot.StateSize);

	return snapshot;
}
```

---

## Part 8: Testing Checklist

- [ ] **Handshake:** 4 cores exchange and confirm connection
- [ ] **Input serialization:** Buttons/sticks round-trip correctly
- [ ] **Checksum:** Corrupted packets detected
- [ ] **Frame numbering:** Frames arrive out of order, still processed correctly
- [ ] **Rollback capability detection:** Works with Snes9x, fails gracefully on cores without serialize
- [ ] **Rollback mode:** Late packet triggers rollback correctly
- [ ] **Frame-sync mode:** Timeout triggers frame execution without all peers
- [ ] **State snapshots:** Captured every 10 frames, can be restored
- [ ] **Re-execution after rollback:** Frames 1-10 replayed with corrected input
- [ ] **Performance:** 60 FPS maintained (measure GC collections, verify zero allocation in frame loop)

---

## Part 9: Configuration

**In engine_config.json:**

```json
{
  "NetworkConfig": {
	"EnableRollback": true,              // Auto-detected, can override for testing
	"SnapshotFrequency": 10,             // Save state every N frames
	"RollbackWindow": 30,                // Allow rollback up to N frames
	"ConfirmationTimeout": 50,           // ms to wait for all peers before timeout
	"MessageBufferSize": 1024,           // Bytes for incoming packet buffer
	"StateSnapshotSizeLimit": 10485760   // Max state size (10 MB) for safety
  }
}
```

---

## Summary

| Aspect | Rollback Mode | Frame-Sync Mode |
|--------|---------------|-----------------|
| **Prerequisite** | Core has serialize | No requirement |
| **Handshake** | ✓ Required | ✓ Required |
| **Input frequency** | Every frame | Every frame |
| **State snapshots** | Every 10 frames | None |
| **Late packet handling** | Automatic rollback | Timeout + continue |
| **Latency tolerance** | ±500ms | ±50ms |
| **Implementation complexity** | High | Medium |
| **User experience** | Excellent | Good (on fast networks) |
| **Compatibility** | Snes9x, Genesis, most | All cores |

This protocol supports both paths seamlessly. Phase 4B-4H will implement them conditionally based on core capability.

