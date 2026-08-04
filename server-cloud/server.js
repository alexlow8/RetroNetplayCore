const dgram = require('dgram');
const server = dgram.createSocket('udp4');

const PORT = 5000; 
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

// Background Cleanup Loop
setInterval(() => {
    const now = Date.now();
    for (const roomCode in rooms) {
        if (now - rooms[roomCode].lastPing > LOBBY_TIMEOUT_MS) {
            console.log(`[Timeout] Room ${roomCode} closed. Host disconnected.`);
            delete rooms[roomCode];
        }
    }
}, CLEANUP_INTERVAL_MS);

server.on('message', (msg, rinfo) => {
    let data;
    try {
        data = JSON.parse(msg.toString());
    } catch (e) {
        return; 
    }

    const senderIP = rinfo.address;
    const senderPort = rinfo.port;

    // --- COMMAND: HOST ---
    if (data.command === 'HOST') {
        const roomCode = generateRoomCode();
        
        // Read dynamic player limit from the XAML launcher (default to 2 if not sent)
        const allowedPlayers = parseInt(data.maxPlayers) || 2; 

        rooms[roomCode] = {
            lastPing: Date.now(),
            maxPlayers: allowedPlayers, // Store the specific core's player limit
            players: [
                { ip: senderIP, port: senderPort, isHost: true }
            ]
        };

        console.log(`[Host] Room ${roomCode} created (${allowedPlayers} Players Max) by Host at ${senderIP}:${senderPort}`);
        
        const response = JSON.stringify({ status: 'ROOM_CREATED', code: roomCode });
        server.send(response, senderPort, senderIP);
    }

    // --- COMMAND: PING ---
    else if (data.command === 'PING') {
        const roomCode = data.code;
        if (rooms[roomCode]) {
            rooms[roomCode].lastPing = Date.now();
            server.send(JSON.stringify({ status: 'PONG' }), senderPort, senderIP);
        } else {
            server.send(JSON.stringify({ status: 'ERROR', message: 'Room expired' }), senderPort, senderIP);
        }
    }

    // --- COMMAND: JOIN ---
    else if (data.command === 'JOIN') {
        const roomCode = data.code;
        const room = rooms[roomCode];

        if (!room) {
            server.send(JSON.stringify({ status: 'ERROR', message: 'Room not found' }), senderPort, senderIP);
            return;
        }

        // DYNAMIC CHECK: Evaluate against the room's specific limit instead of a hardcoded 4
        if (room.players.length >= room.maxPlayers) {
            console.log(`[Join Denied] Player tried to join Room ${roomCode}, but it is full (${room.maxPlayers}/${room.maxPlayers})`);
            server.send(JSON.stringify({ status: 'ERROR', message: 'Room is full' }), senderPort, senderIP);
            return;
        }

        const newPlayer = { ip: senderIP, port: senderPort, isHost: false };
        room.players.push(newPlayer);
        
        console.log(`[Join] Player ${room.players.length}/${room.maxPlayers} joined Room ${roomCode}`);
        room.lastPing = Date.now();

        const updatePayload = JSON.stringify({
            status: 'LOBBY_UPDATE',
            playerIndex: room.players.length - 1, 
            peers: room.players
        });

        for (const player of room.players) {
            server.send(updatePayload, player.port, player.ip);
        }
    }

    // --- COMMAND: START ---
    else if (data.command === 'START_GAME') {
        const roomCode = data.code;
        const room = rooms[roomCode];

        if (room && room.players[0].ip === senderIP && room.players[0].port === senderPort) {
            console.log(`[Start] Host started game for Room ${roomCode}. Closing lobby.`);
            
            const startPayload = JSON.stringify({ status: 'GAME_STARTING' });
            for (const player of room.players) {
                server.send(startPayload, player.port, player.ip);
            }

            delete rooms[roomCode];
        }
    }
});

server.on('listening', () => {
    console.log(`[Ready] Dynamic Matchmaking Server live on port ${PORT}`);
});

server.bind(PORT);
