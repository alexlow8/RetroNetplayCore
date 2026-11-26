# Phase 4 Kickoff Summary

## What You Now Have

### Documentation (Complete)
1. **PHASE_4A_INPUT_PROTOCOL.md** (27 KB)
   - Complete binary message format specification
   - 15-byte header, 5 message types
   - Thread-safe ring buffer design
   - Examples: serialization/deserialization code
   - Testing checklist

2. **PHASE_4_IMPLEMENTATION_GUIDE.txt** (15 KB)
   - Step-by-step implementation roadmap
   - 9 phases with time estimates
   - Code structure and pseudocode
   - Testing strategy
   - Success criteria

## Architecture Decision: Dual-Mode

### Mode A: Rollback (Cores with serialization)
- **Detection:** Libretro functions exist (retro_serialize, retro_unserialize)
- **Capability:** Tolerates ±500ms network jitter
- **Mechanism:** State snapshots every 10 frames → rollback on late packet
- **Cores:** Snes9x, Genesis Plus GX, Mupen64Plus, Nestopia, most common cores

### Mode B: Frame-Sync Only (Fallback)
- **Detection:** No serialization functions found
- **Capability:** Requires <50ms latency
- **Mechanism:** Wait for all peers, timeout on slow peers
- **Cores:** Very old cores, experimental cores, anything without serialize
- **User Experience:** Still playable, but less tolerant of jitter

**Both modes share:**
- Handshake synchronization (pre-game)
- Input protocol (frame-tagged messages)
- Server-based dropout detection (Phase 4H)

---

## Phase 4B-4I Breakdown

| Phase | Name | Hours | Key Files | Complexity |
|-------|------|-------|-----------|------------|
| 4B | Capability Detection + Handshake | 2-3 | NetworkThreadManager.cs | Medium |
| 4C | State Snapshot Manager | 1-2 | NetworkThreadManager.cs | Medium |
| 4D | Rollback Frame Tracking | 1-2 | NetworkThreadManager.cs | Medium |
| 4E | PublishLocalInput() | 1-2 | NetworkThreadManager.cs | Low |
| 4F | ReceiveP2PMessages() + Rollback | 2-3 | NetworkThreadManager.cs | High |
| 4G | Frame Loop Integration | 2-3 | Program.cs | High |
| 4H | Dropout Handling | 1-2 | NetworkThreadManager.cs | Low |
| 4I | Integration Testing | 3-4 | Tests | High |
| **Total** | | **14-18** | | |

---

## Critical Design Constraints

### 1. Zero Allocation in Frame Loop
- ✅ All buffers pre-allocated at startup
- ✅ Ring buffer (256 slots)
- ✅ Outgoing queue (at capacity)
- ✅ Snapshot buffers (26 slots × state size)
- ⚠️ **Violation = GC pause = dropped frames = desync**

### 2. Single-Writer, Single-Reader Pattern
- **Network thread writes:** Ring buffer, snapshot manager
- **Frame thread reads:** Ring buffer, snapshot manager
- **Result:** No locks required (wait-free)
- **Thread-safe:** ✓ No race conditions

### 3. Handshake Before Gameplay
- Confirms all 4 peers connected
- Exchanges network information
- Prevents frame loop starting before peers ready
- Timeout: 2 seconds (then proceed anyway with warning)

### 4. Frame-Count in Every Message
- Enables rollback detection (did input change for frame N?)
- Enables out-of-order packet handling
- Enables late-arrival recovery
- Enables clear frame confirmation tracking

---

## Key Implementation Points

### 1. Capability Detection (Phase 4B)
```csharp
// In NetworkThreadManager.cs
public bool DetectSerializationSupport(IntPtr coreHandle)
{
	// Query: does core have retro_serialize?
	// Sets: SupportsRollback flag
	// Result: Conditionally enable state snapshots
}
```

### 2. Handshake Exchange (Phase 4B)
```
Core 1 → {HANDSHAKE, MAC, Public IP, Port} → Core 2,3,4
Core 2 → {HANDSHAKE, MAC, Public IP, Port} → Core 1,3,4
Core 3 → {HANDSHAKE, MAC, Public IP, Port} → Core 1,2,4
Core 4 → {HANDSHAKE, MAC, Public IP, Port} → Core 1,2,3

When all 4 handshakes received → Proceed to frame loop
```

### 3. Input Flow (Phase 4E-4F)
```
Frame Thread                Network Thread
─────────────────          ──────────────
PublishLocalInput()     →  Dequeue
  ↓                        ↓
OutgoingQueue          Serialize
						↓
						UDP Send to peers

UDP Receive ←          Frame Thread
  ↓
ReceiveP2PMessages()
  ↓
Ring Buffer ←          GetFrameInputs()
  ↓
Frame Thread
```

### 4. Rollback Decision (Phase 4F)
```
if (FrameNumber < ExecutedFrame AND InputData != Previous)
{
	// ROLLBACK TRIGGERED
	Load StateSnapshot(FrameNumber)
	Re-execute(FrameNumber → ExecutedFrame)
	Continue forward
}
else
{
	// Normal path
	Store input, continue
}
```

---

## Common Pitfalls to Avoid

### ❌ Blocking Network Operations in Frame Thread
- ✅ Frame thread only reads pre-filled buffers
- ✅ All UDP I/O happens on network thread

### ❌ Allocating Memory in Frame Loop
- ✅ Pre-allocate everything at startup
- ✅ Use only fixed-size buffers during gameplay

### ❌ Assuming All Peers Send on Time
- ✅ Handshake waits, but has timeout
- ✅ Frame-sync mode has 50ms timeout
- ✅ Rollback mode tolerates 500ms delays

### ❌ Ignoring Serialization Capability
- ✅ Detect at startup
- ✅ Fall back gracefully to frame-sync
- ✅ Warn user about network requirements

### ❌ Trusting Incoming Packets
- ✅ Validate checksum (CRC32)
- ✅ Validate frame number is reasonable
- ✅ Drop corrupted packets

---

## Testing Strategy

### Local Test (1-2 hours)
- 2 cores on same machine
- Verify P2P connection
- Verify inputs flow
- Verify zero allocation (run GC.Collect() diagnostics)

### LAN Test (1-2 hours)
- 2-4 PCs on same network
- Verify LAN routing (uses internal IPs)
- Measure latency (should be <10ms)
- Play 60+ seconds

### Network Jitter Test (1-2 hours)
- Use tool to simulate packet delays
- Inject 50-200ms random delays
- Verify rollback works (if supported)
- Verify frame-sync fallback (if unsupported)

### Dropout Test (30-60 min)
- 4 cores connected
- Kill one mid-game
- Verify PLAYER_DISCONNECTED signal
- Verify game continues

---

## Success Indicators

After Phase 4, you'll have:

✅ **Handshake System**
- All peers sync before gameplay
- Confident connections before frame loop starts

✅ **Rollback Netplay** (Rollback Cores)
- Tolerates 500ms+ network jitter
- Transparent to player (no visible desync)
- Automatic recovery from late packets

✅ **Frame-Sync Fallback** (All Cores)
- Works on cores without serialization
- Requires <50ms latency but functional
- Clear warning to user about network requirements

✅ **Zero Allocation Frame Loop**
- GC collections = 0 during gameplay
- 60 FPS maintained
- Frame time variance <1ms

✅ **Dropout Detection**
- Server detects player disconnect
- Game continues with remaining players
- Graceful degradation (not crash)

---

## Next Steps When Ready

1. **Review the docs** (Session complete - read in your own time)
   - PHASE_4A_INPUT_PROTOCOL.md (binary format)
   - PHASE_4_IMPLEMENTATION_GUIDE.txt (step-by-step)

2. **Choose starting point**
   - **Recommended:** Start with Phase 4B (handshake)
   - It's the foundation for everything else
   - Moderate complexity, high impact

3. **Establish local test**
   - 2 cores on same machine
   - Verify basics work before adding rollback

4. **Implement in order**
   - 4B → 4C → 4D → 4E → 4F → 4G → 4H → 4I
   - Each depends on previous phases
   - Estimated 14-18 hours total

---

## Resources

**You have everything you need:**
- ✅ Code architecture (Phase 3)
- ✅ Binary protocol spec (Phase 4A)
- ✅ Implementation roadmap (this guide)
- ✅ Pseudocode examples (Phase 4A)
- ✅ Success criteria (this guide)

**When you pick this back up:**
1. Re-read SESSION_SUMMARY.md (quick recap)
2. Dive into PHASE_4_IMPLEMENTATION_GUIDE.txt (Phase 4B first)
3. Reference PHASE_4A_INPUT_PROTOCOL.md for binary details
4. Code with comments as you go

---

## Questions?

If you have any questions when you resume:
- Check PHASE_4A_INPUT_PROTOCOL.md (message formats)
- Check PHASE_4_IMPLEMENTATION_GUIDE.txt (step-by-step)
- Review QUICK_START_GUIDE.txt (architecture refresh)

**You've got this!** 🎮✨

The architecture is sound, the protocol is defined, and the implementation path is clear. When you're ready, Phase 4 is a straight shot to a production-quality rollback netplay system.

