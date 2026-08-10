const dgram = require('dgram');
const server = dgram.createSocket('udp4');

const PORT = 5005; 
const rooms = {};  

const LOBBY_TIMEOUT_MS = 15000; 
const CLEANUP_INTERVAL_MS = 5000; 

function generateRoomCode() {
    let code;
    do {
        code = Math.floor(1000 + Math.random() * 9000).toString();
    } while (rooms[code]);
    return code;
}

setInterval(() => {
    const now = Date.now();
    for (const roomCode in rooms) {
        if (now - rooms[roomCode].lastPing > LOBBY_TIMEOUT_MS) {
            delete rooms[roomCode];
        }
    }
}, CLEANUP_INTERVAL_MS);

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
                    isHost: true 
                }
            ]
        };

        console.log(`[Host] Room ${roomCode} opened by ${publicIP}:${publicPort} (LAN: ${data.internalIp}:${data.internalPort})`);
        server.send(JSON.stringify({ status: 'ROOM_CREATED', code: roomCode }), rinfo.port, rinfo.address);
    }

    // --- COMMAND: PING ---
    else if (data.command === 'PING') {
        const roomCode = data.code;
        if (rooms[roomCode]) {
            rooms[roomCode].lastPing = Date.now();
            server.send(JSON.stringify({ status: 'PONG' }), rinfo.port, rinfo.address);
        } else {
            server.send(JSON.stringify({ status: 'ERROR', message: 'Room expired' }), rinfo.port, rinfo.address);
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
            isHost: false 
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
            delete rooms[roomCode];
        }
    }
});

server.bind(PORT);
