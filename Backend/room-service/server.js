// Fart Online room service: hands out multiplayer rooms on this machine.
//
//   POST /rooms               (signed in)  create a room: starts one dedicated game-server process for it
//   POST /rooms/join {code}   (signed in)  join an existing room
//   GET  /health                           {ok, rooms, players}
//
// "Signed in" = the request carries the player's Supabase access token (Authorization: Bearer <jwt>). The service
// asks Supabase who that is (GET /auth/v1/user) and reads the username from public.profiles, so only real accounts
// can play online.
//
// Joining a room returns a short-lived TICKET (HMAC-SHA256 signed with TICKET_SECRET, shared with the game server
// through its environment). The game client sends the ticket as its connection payload; the game server checks
// the signature, the room code and the expiry, and takes the player's name from it. Clients never talk to the game
// server without a ticket.
//
// Rooms: one Unity dedicated-server process per room (up to 4 players), on port INTERNAL_PORT_BASE + slot.
// With TLS=1 (production) Caddy terminates TLS on PUBLIC_PORT_BASE + slot and forwards to the internal port, so
// browsers and phones connect with wss://PUBLIC_HOST:port. With TLS=0 (local testing) the game server listens on
// the public port directly (plain ws). The game server prints "[FOROOM] players=N" lines and quits by itself after
// it has been empty for a while; the service frees the slot when the process exits.
//
// No dependencies: Node 18+ (fetch, crypto). Configuration comes from environment variables (see .env.example).

'use strict';
const http = require('http');
const crypto = require('crypto');
const { spawn } = require('child_process');
const path = require('path');

const env = (k, d) => (process.env[k] !== undefined && process.env[k] !== '' ? process.env[k] : d);
const CONFIG = {
  port: parseInt(env('PORT', '8080'), 10),
  bind: env('BIND', '127.0.0.1'),
  supabaseUrl: env('SUPABASE_URL', '').replace(/\/+$/, ''),
  supabaseAnonKey: env('SUPABASE_ANON_KEY', ''),
  ticketSecret: env('TICKET_SECRET', ''),
  gameServerBin: env('GAME_SERVER_BIN', ''),
  gameServerArgs: env('GAME_SERVER_ARGS', '-batchmode -nographics').split(/\s+/).filter(Boolean),
  publicHost: env('PUBLIC_HOST', '127.0.0.1'),
  publicPortBase: parseInt(env('PUBLIC_PORT_BASE', '7800'), 10),
  internalPortBase: parseInt(env('INTERNAL_PORT_BASE', '17800'), 10),
  maxRooms: parseInt(env('MAX_ROOMS', '3'), 10),
  tls: env('TLS', '0') === '1',
  ticketSeconds: parseInt(env('TICKET_SECONDS', '120'), 10),
  startTimeoutMs: parseInt(env('START_TIMEOUT_MS', '45000'), 10),
  logDir: env('LOG_DIR', ''),
  // LOCAL TESTS ONLY: accept "Authorization: Bearer dev:<username>" without Supabase. Refused together with TLS.
  devAuth: env('DEV_AUTH', '0') === '1',
};

function fail(msg) { console.error('[rooms] ' + msg); process.exit(1); }
if (CONFIG.devAuth && CONFIG.tls) fail('DEV_AUTH is for local tests only and cannot be used with TLS=1');
if (!CONFIG.devAuth && (!CONFIG.supabaseUrl || !CONFIG.supabaseAnonKey)) fail('SUPABASE_URL and SUPABASE_ANON_KEY are required');
if (!CONFIG.ticketSecret || CONFIG.ticketSecret.length < 32) fail('TICKET_SECRET must be at least 32 characters');
if (!CONFIG.gameServerBin) fail('GAME_SERVER_BIN is required (path to the Unity dedicated server executable)');

const log = (...a) => console.log(new Date().toISOString(), '[rooms]', ...a);

// ------------------------------------------------------------------ auth (Supabase)
const authCache = new Map();   // token -> {user, until}

async function whoIs(token) {
  if (CONFIG.devAuth && token.startsWith('dev:')) {
    const name = token.slice(4).replace(/[^A-Za-z0-9_]/g, '').slice(0, 16);
    return name.length >= 3 ? { id: 'dev-' + name, username: name } : null;
  }
  const hit = authCache.get(token);
  if (hit && hit.until > Date.now()) return hit.user;
  const headers = { apikey: CONFIG.supabaseAnonKey, Authorization: 'Bearer ' + token };
  const r = await fetch(CONFIG.supabaseUrl + '/auth/v1/user', { headers });
  if (!r.ok) return null;
  const u = await r.json();
  if (!u || !u.id) return null;
  const pr = await fetch(CONFIG.supabaseUrl + '/rest/v1/profiles?select=username&id=eq.' + encodeURIComponent(u.id), { headers });
  if (!pr.ok) return null;
  const rows = await pr.json();
  if (!Array.isArray(rows) || rows.length === 0) return null;
  const user = { id: u.id, username: String(rows[0].username) };
  authCache.set(token, { user, until: Date.now() + 60_000 });
  if (authCache.size > 5000) authCache.clear();
  return user;
}

// ------------------------------------------------------------------ tickets
const b64url = (buf) => Buffer.from(buf).toString('base64').replace(/=+$/, '').replace(/\+/g, '-').replace(/\//g, '_');

function makeTicket(room, user, owner) {
  const body = b64url(JSON.stringify({
    r: room.code, u: user.id, n: user.username, o: owner ? 1 : 0,
    e: Math.floor(Date.now() / 1000) + CONFIG.ticketSeconds,
  }));
  const sig = b64url(crypto.createHmac('sha256', CONFIG.ticketSecret).update(body).digest());
  return body + '.' + sig;
}

// ------------------------------------------------------------------ rooms
const rooms = new Map();         // code -> room
const ALPHABET = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';   // no 0/O/1/I

function newCode() {
  for (;;) {
    let c = '';
    const bytes = crypto.randomBytes(6);
    for (let i = 0; i < 6; i++) c += ALPHABET[bytes[i] % ALPHABET.length];
    if (!rooms.has(c)) return c;
  }
}

function freeSlot() {
  const used = new Set([...rooms.values()].map((r) => r.slot));
  for (let i = 0; i < CONFIG.maxRooms; i++) if (!used.has(i)) return i;
  return -1;
}

function publicInfo(room) {
  return { code: room.code, host: CONFIG.publicHost, port: room.publicPort, tls: CONFIG.tls, players: room.players };
}

function startRoom(owner) {
  return new Promise((resolve, reject) => {
    const slot = freeSlot();
    if (slot < 0) return reject(Object.assign(new Error('All rooms are busy, try again in a minute'), { status: 503 }));
    const code = newCode();
    const publicPort = CONFIG.publicPortBase + slot;
    const listenPort = CONFIG.tls ? CONFIG.internalPortBase + slot : publicPort;
    const listenAddress = CONFIG.tls ? '127.0.0.1' : '0.0.0.0';
    const args = [...CONFIG.gameServerArgs, '-fo-dedicated', '-fo-port', String(listenPort), '-fo-listen', listenAddress, '-fo-room', code];
    if (CONFIG.logDir) args.push('-logFile', path.join(CONFIG.logDir, `room-${code}.log`));
    else args.push('-logFile', '-');
    const child = spawn(CONFIG.gameServerBin, args, {
      env: { ...process.env, FO_TICKET_SECRET: CONFIG.ticketSecret },
      stdio: ['ignore', 'pipe', 'pipe'],
    });
    const room = { code, slot, publicPort, listenPort, child, owner: owner.id, players: 0, ready: false, created: Date.now() };
    rooms.set(code, room);
    log(`room ${code}: starting on port ${listenPort} (public ${publicPort}) for ${owner.username}`);

    const timer = setTimeout(() => {
      if (!room.ready) { log(`room ${code}: did not start in time`); child.kill('SIGKILL'); reject(new Error('The game server did not start')); }
    }, CONFIG.startTimeoutMs);

    let buffer = '';
    const onData = (chunk) => {
      buffer += chunk.toString();
      let nl;
      while ((nl = buffer.indexOf('\n')) >= 0) {
        const line = buffer.slice(0, nl).trim();
        buffer = buffer.slice(nl + 1);
        const m = /\[FOROOM\] (\w+)(?:=(\S+))?/.exec(line);
        if (!m) continue;
        if (m[1] === 'ready' && !room.ready) { room.ready = true; clearTimeout(timer); log(`room ${code}: ready`); resolve(room); }
        else if (m[1] === 'players') { room.players = parseInt(m[2], 10) || 0; log(`room ${code}: ${room.players} player(s)`); }
      }
    };
    child.stdout.on('data', onData);
    child.stderr.on('data', onData);
    // when the game writes its log to a file, readiness is reported on stdout only if the file is "-":
    // the game server also prints [FOROOM] lines with Console.WriteLine, which always reach stdout
    child.on('exit', (codeOrNull, signal) => {
      clearTimeout(timer);
      rooms.delete(code);
      log(`room ${code}: server exited (${codeOrNull ?? signal})`);
      if (!room.ready) reject(new Error('The game server stopped while starting'));
    });
    child.on('error', (e) => { log(`room ${code}: spawn failed: ${e.message}`); rooms.delete(code); clearTimeout(timer); reject(e); });
  });
}

// ------------------------------------------------------------------ HTTP
function send(res, status, obj) {
  const body = JSON.stringify(obj);
  res.writeHead(status, {
    'Content-Type': 'application/json',
    'Content-Length': Buffer.byteLength(body),
    'Access-Control-Allow-Origin': '*',
    'Access-Control-Allow-Headers': 'Authorization, Content-Type',
    'Access-Control-Allow-Methods': 'GET, POST, OPTIONS',
    'Cache-Control': 'no-store',
  });
  res.end(body);
}

function readJson(req) {
  return new Promise((resolve) => {
    let data = '';
    req.on('data', (c) => { data += c; if (data.length > 4096) req.destroy(); });
    req.on('end', () => { try { resolve(data ? JSON.parse(data) : {}); } catch { resolve({}); } });
  });
}

// a tiny per-user rate limit: 20 room requests a minute
const hits = new Map();
function limited(userId) {
  const now = Date.now();
  const h = (hits.get(userId) || []).filter((t) => now - t < 60_000);
  h.push(now);
  hits.set(userId, h);
  return h.length > 20;
}

const server = http.createServer(async (req, res) => {
  try {
    const url = new URL(req.url, 'http://x');
    const route = url.pathname.replace(/^\/api/, '').replace(/\/+$/, '') || '/';
    if (req.method === 'OPTIONS') return send(res, 204, {});
    if (req.method === 'GET' && route === '/health') {
      let players = 0;
      for (const r of rooms.values()) players += r.players;
      return send(res, 200, { ok: true, rooms: rooms.size, maxRooms: CONFIG.maxRooms, players });
    }
    if (req.method !== 'POST' || (route !== '/rooms' && route !== '/rooms/join')) return send(res, 404, { error: 'Not found' });

    const auth = req.headers.authorization || '';
    const token = auth.startsWith('Bearer ') ? auth.slice(7) : '';
    const user = token ? await whoIs(token) : null;
    if (!user) return send(res, 401, { error: 'Please log in to play online' });
    if (limited(user.id)) return send(res, 429, { error: 'Too many requests, slow down' });

    if (route === '/rooms') {
      // one room per owner: reuse a live room the player already owns
      for (const r of rooms.values()) if (r.owner === user.id && r.ready && r.players < 4) return send(res, 200, { ...publicInfo(r), ticket: makeTicket(r, user, true) });
      const room = await startRoom(user);
      return send(res, 200, { ...publicInfo(room), ticket: makeTicket(room, user, true) });
    }
    const body = await readJson(req);
    const code = String(body.code || '').trim().toUpperCase();
    const room = rooms.get(code);
    if (!room || !room.ready) return send(res, 404, { error: 'No room with code ' + code + ' (it may have closed)' });
    if (room.players >= 4) return send(res, 409, { error: 'That raft is full (4 players)' });
    return send(res, 200, { ...publicInfo(room), ticket: makeTicket(room, user, room.owner === user.id) });
  } catch (e) {
    log('error', e && e.stack || e);
    return send(res, e.status || 500, { error: e.status ? e.message : 'Server error: ' + (e.message || e) });
  }
});

server.listen(CONFIG.port, CONFIG.bind, () => log(`listening on ${CONFIG.bind}:${CONFIG.port} | host ${CONFIG.publicHost} | ports ${CONFIG.publicPortBase}+ | tls ${CONFIG.tls} | max ${CONFIG.maxRooms} rooms${CONFIG.devAuth ? ' | DEV_AUTH ON (local tests only)' : ''}`));

function shutdown() {
  log('shutting down');
  for (const r of rooms.values()) r.child.kill('SIGTERM');
  server.close(() => process.exit(0));
  setTimeout(() => process.exit(0), 3000);
}
process.on('SIGTERM', shutdown);
process.on('SIGINT', shutdown);
