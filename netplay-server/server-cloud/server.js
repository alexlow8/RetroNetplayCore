const dgram = require('dgram');
const server = dgram.createSocket('udp4');

const PORT = 5005; 
const rooms = {};  

const LOBBY_TIMEOUT_MS = 15000;
const HEARTBEAT_TIMEOUT_MS = 3000; // Core must heartbeat every 3 seconds
const HEARTBEAT_CHECK_INTERVAL_MS = 1000; // Check dropouts every second
const CLEANUP_INTERVAL_MS = 5000; 

function generateRoomCode() {
    let code;
    do {
        code = Math.floor(1000 + Math.random() * 9000).toString();
    } while (rooms[code]);
    return code;
}

// Cleanup lobbies that expired before gameplay started
setInterval(() => {
    const now = Date.now();
    for (const roomCode in rooms) {
        const room = rooms[roomCode];
        // Only delete if still in LOBBY phase and expired
        if (room.phase === 'LOBBY' && now - room.lastPing > LOBBY_TIMEOUT_MS) {
            console.log(`[Cleanup] Room ${roomCode} expired in LOBBY phase`);
            delete rooms[roomCode];
        }
    }
}, CLEANUP_INTERVAL_MS);

// Monitor heartbeats during gameplay for dropouts
setInterval(() => {
    const now = Date.now();
    for (const roomCode in rooms) {
        const room = rooms[roomCode];
        if (room.phase !== 'GAMEPLAY') continue;

        // Check each player's heartbeat
        for (let i = 0; i < room.players.length; i++) {
            const player = room.players[i];
            if (player.disconnected) continue; // Already marked as dropped

            const timeSinceHeartbeat = now - player.lastHeartbeat;
            if (timeSinceHeartbeat > HEARTBEAT_TIMEOUT_MS) {
                console.log(`[Dropout] Player ${i} in room ${roomCode} dropped after ${timeSinceHeartbeat}ms`);
                player.disconnected = true;
                player.disconnectTime = now;

                // Notify all remaining players of the dropout
                const dropoutPayload = JSON.stringify({
                    status: 'PLAYER_DISCONNECTED',
                    playerIndex: i,
                    remainingPlayers: room.players.filter((p, idx) => !p.disconnected || idx === i).length - 1
                });

                for (let j = 0; j < room.players.length; j++) {
                    if (i === j) continue; // Don't notify the dropped player
                    if (room.players[j].disconnected) continue; // Don't notify other dropped players

                    const target = room.players[j];
                    server.send(dropoutPayload, target.port, target.ip);
                }
            }
        }

        // Check if all players dropped - clean up room after delay
        const activePlayers = room.players.filter(p => !p.disconnected).length;
        if (activePlayers === 0) {
            console.log(`[Cleanup] Room ${roomCode} has no active players, marking for deletion`);
            if (!room.emptyStartTime) {
                room.emptyStartTime = now;
            }
            if (now - room.emptyStartTime > 10000) { // 10 seconds grace period
                delete rooms[roomCode];
            }
        } else if (room.emptyStartTime) {
            room.emptyStartTime = null; // Reset if players return
        }
    }
}, HEARTBEAT_CHECK_INTERVAL_MS);

server.on('message', (msg, rinfo) => {
    let data;
    try { data = JSON.parse(msg.toString()); } catch (e) { return; }

    // Server-side discovery of the public-facing transport endpoint
    const publicIP = rinfo.address;
    const publicPort = rinfo.port;

    // --- COMMAND: HOST ---
    if (data.command === 'HOST') {
        const roomCode = generateRoomCode();
        const allowedPlayers = parseInt(data.maxPlayers) || 2; 

        rooms[roomCode] = {
            phase: 'LOBBY', // Can be LOBBY or GAMEPLAY
            lastPing: Date.now(),
            maxPlayers: allowedPlayers, 
            coreName: data.coreName || "MockLibretroCore.dll", 
            romPath: data.romPath || "",                       
            players: [
                { 
                    ip: publicIP, 
                    port: publicPort, 
                    internalIp: data.internalIp || publicIP, 
                    internalPort: parseInt(data.internalPort) || publicPort, 
                    isHost: true,
                    disconnected: false,
                    lastHeartbeat: Date.now()
                }
            ]
        };

        console.log(`[Host] Room ${roomCode} opened by ${publicIP}:${publicPort} (LAN: ${data.internalIp}:${data.internalPort})`);
        server.send(JSON.stringify({ status: 'ROOM_CREATED', code: roomCode }), rinfo.port, rinfo.address);
    }

    // --- COMMAND: PING (Lobby phase keep-alive) ---
    else if (data.command === 'PING') {
        const roomCode = data.code;
        if (rooms[roomCode]) {
            rooms[roomCode].lastPing = Date.now();
            server.send(JSON.stringify({ status: 'PONG' }), rinfo.port, rinfo.address);
        } else {
            server.send(JSON.stringify({ status: 'ERROR', message: 'Room expired' }), rinfo.port, rinfo.address);
        }
    }

    // --- COMMAND: HEARTBEAT (Gameplay phase keep-alive) ---
    else if (data.command === 'HEARTBEAT') {
        const roomCode = data.code;
        if (rooms[roomCode]) {
            const room = rooms[roomCode];
            // Find player and update heartbeat
            for (let i = 0; i < room.players.length; i++) {
                if (room.players[i].ip === publicIP && room.players[i].port === publicPort) {
                    room.players[i].lastHeartbeat = Date.now();
                    room.players[i].disconnected = false;
                    break;
                }
            }
            server.send(JSON.stringify({ status: 'HEARTBEAT_ACK' }), rinfo.port, rinfo.address);
        }
    }

    // --- COMMAND: JOIN ---
    else if (data.command === 'JOIN') {
        const roomCode = data.code;
        const room = rooms[roomCode];

        if (!room) {
            server.send(JSON.stringify({ status: 'ERROR', message: 'Room not found' }), rinfo.port, rinfo.address);
            return;
        }

        if (room.players.length >= room.maxPlayers) {
            server.send(JSON.stringify({ status: 'ERROR', message: 'Room is full' }), rinfo.port, rinfo.address);
            return;
        }

        const newPlayer = { 
            ip: publicIP, 
            port: publicPort, 
            internalIp: data.internalIp || publicIP, 
            internalPort: parseInt(data.internalPort) || publicPort, 
            isHost: false,
            disconnected: false,
            lastHeartbeat: Date.now()
        };
        room.players.push(newPlayer);

        console.log(`[Join] Room ${roomCode} joined by ${publicIP}:${publicPort} (LAN: ${data.internalIp}:${data.internalPort})`);
        room.lastPing = Date.now();

        const updatePayload = JSON.stringify({
            status: 'LOBBY_UPDATE',
            playerIndex: room.players.length - 1, 
            coreName: room.coreName,
            romPath: room.romPath,
            peers: room.players
        });

        for (const player of room.players) {
            // Send back to the immediate rinfo channel if managing the active sender context
            if (player.ip === publicIP && player.port === publicPort) {
                server.send(updatePayload, rinfo.port, rinfo.address);
            } else {
                server.send(updatePayload, player.port, player.ip);
            }
        }
    }

    // --- COMMAND: START ---
    else if (data.command === 'START_GAME') {
        const roomCode = data.code;
        const room = rooms[roomCode];

        if (room) {
            room.phase = 'GAMEPLAY'; // Transition to gameplay phase (keep room alive)

            const startPayload = JSON.stringify({ 
                status: 'GAME_STARTING',
                coreName: room.coreName,
                romPath: room.romPath
            });

            for (const player of room.players) {
                if (player.ip === publicIP && player.port === publicPort) {
                    server.send(startPayload, rinfo.port, rinfo.address);
                } else {
                    server.send(startPayload, player.port, player.ip);
                }
            }
            console.log(`[Gameplay] Room ${roomCode} started, monitoring for dropouts...`);
        }
    }
});

server.bind(PORT);
console.log(`Netplay Server listening on port ${PORT}`);
