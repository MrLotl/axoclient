// AxoClient-Dienst (Cloudflare Worker + D1), Einrichtung siehe ANLEITUNG.md
//
// Bindungen und Variablen des Workers:
//   DB          D1-Datenbank
//   OWNER_UUID  Minecraft-UUID des Besitzers (mehrere mit Komma getrennt); er ernennt die Admins

const DAY = 24 * 60 * 60 * 1000;
const HOUR = 60 * 60 * 1000;
const MINUTE = 60 * 1000;

const ACTIVE_DAYS = 30;               // wer so lange nicht spielt, verliert das Symbol
const TOKEN_DAYS = 60;                // so lange gilt ein Anmelde-Token
const MAX_CLOCK_SKEW = 5 * MINUTE;    // so weit darf die Uhr des Spielers abweichen
const ONLINE_MILLIS = 150 * 1000;     // der Launcher meldet sich mindestens jede Minute
const SEEN_WRITE_MILLIS = MINUTE;     // "zuletzt gesehen" höchstens so oft schreiben
const MAX_UUIDS = 90;                 // D1 erlaubt höchstens 100 Parameter pro Abfrage
const MAX_FRIENDS = 200;

const SESSION_GAP = 3 * MINUTE;       // längere Pause = neue Spielsitzung
const SESSION_DAYS = 90;              // so lange bleiben Spielsitzungen für Freundesprofile

const NOTIFICATION_DAYS = 30;
const MAX_INVITES_PER_HOUR = 20;
const ONLINE_NOTICE_AFTER = 30 * MINUTE;  // wer so lange weg war, meldet sich bei Freunden als "online"
const ONLINE_NOTICE_REPEAT = 6 * HOUR;    // höchstens so oft je Freund

const SHARE_KINDS = ["instance", "overlay", "content", "server"];
const SHARE_DAYS = 14;
const MAX_SHARE_CHARS = 250_000;      // der Launcher kennt dasselbe Limit
const MAX_INBOX = 40;
const MAX_PENDING_PER_PAIR = 8;
const MAX_SENDS_PER_HOUR = 30;

const MAX_CAPE_BYTES = 1_500_000;     // D1 speichert höchstens 2 MB pro Zeile
const MAX_CAPE_WIDTH = 2048;
const MAX_PERSONAL_CAPES = 5;

const UUID = /^[0-9a-f]{32}$/;
const TOKEN = /^[0-9a-f]{64}$/;
const PLAYER_NAME = /^[A-Za-z0-9_]{1,16}$/;
const SERVER_ADDRESS = /^[A-Za-z0-9.\-_:\[\]]{1,255}$/;
const CAPE_ID = /^[a-z0-9_-]{1,32}$/;
const CAPE_IMAGE_PATH = /^\/capes\/([a-z0-9_-]{1,32})\.png$/;

// Mojangs Schlüssel für Spieler-Zertifikate (https://api.minecraftservices.com/publickeys, "playerCertificateKeys")
const MOJANG_KEYS = [
  "MIICIjANBgkqhkiG9w0BAQEFAAOCAg8AMIICCgKCAgEAylB4B6m5lz7jwrcFz6Fd/fnfUhcvlxsTSn5kIK/2aGG1C3kMy4VjhwlxF6BFUSnfxhNswPjh3ZitkBxEAFY25uzkJFRwHwVA9mdwjashXILtR6OqdLXXFVyUPIURLOSWqGNBtb08EN5fMnG8iFLgEJIBMxs9BvF3s3/FhuHyPKiVTZmXY0WY4ZyYqvoKR+XjaTRPPvBsDa4WI2u1zxXMeHlodT3lnCzVvyOYBLXL6CJgByuOxccJ8hnXfF9yY4F0aeL080Jz/3+EBNG8RO4ByhtBf4Ny8NQ6stWsjfeUIvH7bU/4zCYcYOq4WrInXHqS8qruDmIl7P5XXGcabuzQstPf/h2CRAUpP/PlHXcMlvewjmGU6MfDK+lifScNYwjPxRo4nKTGFZf/0aqHCh/EAsQyLKrOIYRE0lDG3bzBh8ogIMLAugsAfBb6M3mqCqKaTMAf/VAjh5FFJnjS+7bE+bZEV0qwax1CEoPPJL1fIQjOS8zj086gjpGRCtSy9+bTPTfTR/SJ+VUB5G2IeCItkNHpJX2ygojFZ9n5Fnj7R9ZnOM+L8nyIjPu3aePvtcrXlyLhH/hvOfIOjPxOlqW+O5QwSFP4OEcyLAUgDdUgyW36Z5mB285uKW/ighzZsOTevVUG2QwDItObIV6i8RCxFbN2oDHyPaO5j1tTaBNyVt8CAwEAAQ==",
  "MIICIjANBgkqhkiG9w0BAQEFAAOCAg8AMIICCgKCAgEAt4t9NPuu7cktclnaH7eZj0omkLcJHeLz5MKsyJEntHZ0INtuBjSSul3Pp3pBeJN8k3ADdcdBLUN90bcAi7WsQqTx3Ft363q3W7TbM8j2iTEdp/0uVspoRt/DP1tkaWFs/w2WwUv9jbVoBUzfUc4pSTIxRwdjmqjZQfvjwKNDbOx3IhP2H0WXodbISejPi1wBZqNW4m1rnZAXp/EpUguxA8mobCa4vUCBkyFDyXdl69/wUSJHyCPmgcMJ364OlAhIqtwVPShBZObvrK/f0BYk6ShJD3N7TFDatSYsIIdcTKRknaIm91s+EsMrdB9U4Yw+ZJ/pyCB4S3vk8zfDCnb0DWIxYH3/EMzaxl77djmTmMzi/JDITup5z3jfWtRZmrAhU2/+W5IO5hEpo3/bCS9PXIY5xb41Lmp2ZO8dXKtyD66Chchy0W129n8vPl2GIruOdrxsjZAHnneyAb9jm0uaGaphwnEnuecX/qgHY6ZMtayvLLsPst8PO6R1vufMy8WqjK+j7LnC1krL7CPDg0NEhyQTmw5l+NCNjSlvB1juM9V4PARg0bYCOkGXm7ydRCjSSH8CJXZpwnd5cBB5WKAX3KPzutRgMi/LFwNSMZzFuUyXaYOZPpD259yqph1LmGqegEdDriACVU+dVEONFMm8eIuBofe7ljmsAFKW9BINwK0CAwEAAQ=="
];

// ========== Routing ==========
// Zugriff: "public" ohne Anmeldung, "user" mit Token aus /register, "admin"/"owner" zusätzlich mit dieser Rolle.

const ROUTES = {
  "GET /": ["public", () => json({ ok: true, service: "mclauncher-badge" })],
  "GET /capes/capes.json": ["public", capeList],
  "POST /register": ["public", register],
  "POST /check": ["public", check],
  "POST /status": ["user", setStatus],
  "POST /friends": ["user", listFriends],
  "POST /friends/add": ["user", addFriend],
  "POST /friends/remove": ["user", removeFriend],
  "POST /friends/profile": ["user", friendProfile],
  "POST /invite": ["user", invite],
  "POST /notifications": ["user", listNotifications],
  "POST /notifications/read": ["user", readNotifications],
  "POST /cape": ["user", cape],
  "POST /capes/upload": ["user", uploadCape],
  "POST /capes/delete": ["user", deleteCape],
  "POST /admins": ["owner", listAdmins],
  "POST /admins/add": ["owner", addAdmin],
  "POST /admins/remove": ["owner", removeAdmin],
  "POST /share/send": ["user", sendShare],
  "POST /share/inbox": ["user", shareInbox],
  "POST /share/get": ["user", getShare],
  "POST /share/delete": ["user", deleteShare]
};

export default {
  async fetch(request, env) {
    try {
      return await handle(request, env);
    } catch (e) {
      return e instanceof HttpError
        ? json({ error: e.message }, e.status)
        : json({ error: "Interner Fehler: " + e.message }, 500);
    }
  }
};

async function handle(request, env) {
  const { pathname } = new URL(request.url);
  const imageId = request.method === "GET" ? pathname.match(CAPE_IMAGE_PATH)?.[1] : null;
  const route = imageId ? ["public", ctx => capeImage(ctx, imageId)] : ROUTES[`${request.method} ${pathname}`];
  if (!route)
    throw new HttpError(404, "Nicht gefunden");

  await ensureSchema(env.DB);
  const parsed = request.method === "POST" ? await request.json().catch(() => null) : null;
  const ctx = { env, db: env.DB, body: parsed && typeof parsed === "object" ? parsed : {}, me: null };

  const [access, handler] = route;
  if (access !== "public") {
    ctx.me = await authenticate(ctx.db, ctx.body);
    if (access !== "user")
      await requireRole(ctx, access);
  }
  return handler(ctx);
}

class HttpError extends Error {
  constructor(status, message) {
    super(message);
    this.status = status;
  }
}

function expect(condition, message = "Ungültige Anfrage", status = 400) {
  if (!condition)
    throw new HttpError(status, message);
}

// ========== Datenbank ==========

const SCHEMA = [
  "CREATE TABLE IF NOT EXISTS users (uuid TEXT PRIMARY KEY, name TEXT NOT NULL, last_seen INTEGER NOT NULL)",
  "CREATE INDEX IF NOT EXISTS users_last_seen ON users (last_seen)",
  "CREATE TABLE IF NOT EXISTS tokens (hash TEXT PRIMARY KEY, uuid TEXT NOT NULL, created INTEGER NOT NULL)",
  "CREATE TABLE IF NOT EXISTS status (uuid TEXT PRIMARY KEY, server TEXT, version TEXT, updated INTEGER NOT NULL)",
  "CREATE TABLE IF NOT EXISTS friends (owner TEXT NOT NULL, friend TEXT NOT NULL, created INTEGER NOT NULL, " +
  "PRIMARY KEY (owner, friend))",
  "CREATE INDEX IF NOT EXISTS friends_friend ON friends (friend)",
  "CREATE TABLE IF NOT EXISTS sessions (id INTEGER PRIMARY KEY AUTOINCREMENT, uuid TEXT NOT NULL, server TEXT, " +
  "instance TEXT, started INTEGER NOT NULL, ended INTEGER NOT NULL)",
  "CREATE INDEX IF NOT EXISTS sessions_uuid ON sessions (uuid, ended)",
  "CREATE INDEX IF NOT EXISTS sessions_server ON sessions (server, ended)",
  "CREATE TABLE IF NOT EXISTS notifications (id INTEGER PRIMARY KEY AUTOINCREMENT, recipient TEXT NOT NULL, " +
  "kind TEXT NOT NULL, sender TEXT, text TEXT, server TEXT, version TEXT, created INTEGER NOT NULL, " +
  "read INTEGER NOT NULL DEFAULT 0)",
  "CREATE INDEX IF NOT EXISTS notifications_recipient ON notifications (recipient, created)",
  "CREATE TABLE IF NOT EXISTS admins (uuid TEXT PRIMARY KEY, name TEXT NOT NULL, added_by TEXT NOT NULL, " +
  "created INTEGER NOT NULL)",
  "CREATE TABLE IF NOT EXISTS cape_images (id TEXT PRIMARY KEY, name TEXT NOT NULL, png BLOB NOT NULL, " +
  "uploader TEXT NOT NULL, created INTEGER NOT NULL)",
  "CREATE TABLE IF NOT EXISTS capes (uuid TEXT PRIMARY KEY, cape TEXT NOT NULL)",
  "CREATE TABLE IF NOT EXISTS shares (id INTEGER PRIMARY KEY AUTOINCREMENT, sender TEXT NOT NULL, " +
  "recipient TEXT NOT NULL, kind TEXT NOT NULL, title TEXT NOT NULL, payload TEXT NOT NULL, created INTEGER NOT NULL)",
  "CREATE INDEX IF NOT EXISTS shares_recipient ON shares (recipient, created)",
  "CREATE INDEX IF NOT EXISTS shares_sender ON shares (sender, created)"
];

// Spalten, die später dazugekommen sind; ALTER TABLE scheitert, wenn es sie schon gibt.
const ADDED_COLUMNS = [
  ["status", "instance TEXT"],
  ["cape_images", "owner TEXT"]
];

const CLEANUP = [
  "DELETE FROM capes WHERE cape NOT IN (SELECT id FROM cape_images)"
];

let schemaReady = null;

/** Einmal je Worker-Instanz: Tabellen anlegen, neue Spalten ergänzen und Umhänge ablegen, die es nicht mehr gibt. */
function ensureSchema(db) {
  schemaReady ??= createSchema(db).catch(e => { schemaReady = null; throw e; });
  return schemaReady;
}

async function createSchema(db) {
  await db.batch(SCHEMA.map(sql => db.prepare(sql)));
  for (const [table, column] of ADDED_COLUMNS) {
    try {
      await db.prepare(`ALTER TABLE ${table} ADD COLUMN ${column}`).run();
    } catch (e) {
      if (!/duplicate column/i.test(e.message))
        throw e;
    }
  }
  await db.batch(CLEANUP.map(sql => db.prepare(sql)));
}

// ========== Anmeldung und Rollen ==========

/** Jede Anfrage mit Token zählt als "gesehen" – daraus ergibt sich der Online-Status für Freunde. */
async function authenticate(db, body) {
  expect(isString(body.token, TOKEN), "Nicht angemeldet", 401);
  const row = await db.prepare(
    "SELECT t.uuid, u.last_seen FROM tokens t LEFT JOIN users u ON u.uuid = t.uuid WHERE t.hash = ?1 AND t.created > ?2"
  ).bind(await sha256(body.token), Date.now() - TOKEN_DAYS * DAY).first();
  expect(row, "Nicht angemeldet", 401);
  await markSeen(db, row.uuid, row.last_seen ?? 0);
  return row.uuid;
}

/** Wer länger weg war, meldet sich bei seinen gegenseitigen Freunden als online. */
async function markSeen(db, me, lastSeen) {
  const now = Date.now();
  if (now - lastSeen < SEEN_WRITE_MILLIS)
    return;
  const statements = [db.prepare("UPDATE users SET last_seen = ?1 WHERE uuid = ?2").bind(now, me)];
  if (now - lastSeen >= ONLINE_NOTICE_AFTER)
    statements.push(db.prepare(
      "INSERT INTO notifications (recipient, kind, sender, created) " +
      "SELECT f.owner, 'friend-online', ?1, ?2 FROM friends f " +
      "WHERE f.friend = ?1 AND EXISTS (SELECT 1 FROM friends g WHERE g.owner = ?1 AND g.friend = f.owner) " +
      "AND NOT EXISTS (SELECT 1 FROM notifications n WHERE n.recipient = f.owner AND n.sender = ?1 " +
      "AND n.kind = 'friend-online' AND n.created > ?3)"
    ).bind(me, now, now - ONLINE_NOTICE_REPEAT));
  await db.batch(statements);
}

function ownerUuids(env) {
  return String(env.OWNER_UUID ?? "").toLowerCase().replace(/-/g, "").split(/[\s,;]+/).filter(u => UUID.test(u));
}

/** "owner", "admin" oder null */
async function roleOf({ env, db }, uuid) {
  if (ownerUuids(env).includes(uuid))
    return "owner";
  return await db.prepare("SELECT 1 FROM admins WHERE uuid = ?1").bind(uuid).first() ? "admin" : null;
}

async function requireRole(ctx, needed) {
  const role = await roleOf(ctx, ctx.me);
  if (needed === "owner")
    expect(role === "owner", "Das darf nur der Besitzer des Dienstes.", 403);
  else
    expect(role !== null, "Das dürfen nur Admins.", 403);
}

// ========== Registrierung ==========
// Nachweis über das Spieler-Zertifikat, das Minecraft auch für signierte Chatnachrichten nutzt: Mojang hat den
// Schlüssel für diese UUID unterschrieben, und der Launcher hat mit dem privaten Schlüssel eine Nachricht mit
// Zeitstempel signiert. Dafür braucht es keine Verbindung zu Mojang, und das Minecraft-Token kommt nie hier an.

async function register({ db, body: b }) {
  expect(isString(b.uuid, UUID) && isString(b.name, PLAYER_NAME)
    && typeof b.publicKey === "string" && typeof b.keySignature === "string" && typeof b.signature === "string"
    && Number.isSafeInteger(b.expiresAt) && Number.isSafeInteger(b.timestamp));

  const now = Date.now();
  expect(Math.abs(now - b.timestamp) <= MAX_CLOCK_SKEW, "Zeitstempel ungültig – stimmt die Uhr des PCs?", 403);
  expect(b.expiresAt >= now, "Spieler-Zertifikat ist abgelaufen", 403);
  await verifyCertificate(b);

  const token = randomHex(32);
  await db.batch([
    db.prepare(
      "INSERT INTO users (uuid, name, last_seen) VALUES (?1, ?2, ?3) " +
      "ON CONFLICT(uuid) DO UPDATE SET name = excluded.name, last_seen = excluded.last_seen"
    ).bind(b.uuid, b.name, now),
    db.prepare("INSERT INTO tokens (hash, uuid, created) VALUES (?1, ?2, ?3)").bind(await sha256(token), b.uuid, now),
    db.prepare("DELETE FROM tokens WHERE created < ?1").bind(now - TOKEN_DAYS * DAY)
  ]);
  return json({ ok: true, uuid: b.uuid, name: b.name, token });
}

async function verifyCertificate(b) {
  const publicKey = base64ToBytes(b.publicKey);

  const certified = new Uint8Array(24 + publicKey.length);
  certified.set(hexToBytes(b.uuid), 0);
  new DataView(certified.buffer).setBigUint64(16, BigInt(b.expiresAt));
  certified.set(publicKey, 24);
  let issuedByMojang = false;
  for (const key of MOJANG_KEYS)
    issuedByMojang ||= await verifyRsa(base64ToBytes(key), "SHA-1", b.keySignature, certified);
  expect(issuedByMojang, "Spieler-Zertifikat wurde nicht von Mojang ausgestellt", 403);

  const message = new TextEncoder().encode(`mclauncher-badge:register:${b.uuid}:${b.timestamp}`);
  expect(await verifyRsa(publicKey, "SHA-256", b.signature, message), "Signatur ungültig", 403);
}

async function verifyRsa(spki, hash, signatureBase64, data) {
  const algorithm = { name: "RSASSA-PKCS1-v1_5", hash };
  const key = await crypto.subtle.importKey("spki", spki, algorithm, false, ["verify"]);
  return crypto.subtle.verify(algorithm, key, base64ToBytes(signatureBase64), data);
}

// ========== Symbol im Spiel ==========

/** Welche dieser Spieler nutzen AxoClient, und welchen Umhang tragen sie? (fragt die Mod ab) */
async function check({ db, body }) {
  const uuids = Array.isArray(body.uuids)
    ? [...new Set(body.uuids.filter(u => isString(u, UUID)))].slice(0, MAX_UUIDS)
    : [];
  if (uuids.length === 0)
    return json({ users: [], capes: {} });

  const placeholders = uuids.map((_, i) => "?" + (i + 2)).join(",");
  const { results } = await db.prepare(
    "SELECT u.uuid, i.id AS cape FROM users u LEFT JOIN capes c ON c.uuid = u.uuid " +
    `LEFT JOIN cape_images i ON i.id = c.cape AND ${WEARABLE_BY("c.uuid")} ` +
    `WHERE u.last_seen > ?1 AND u.uuid IN (${placeholders})`
  ).bind(Date.now() - ACTIVE_DAYS * DAY, ...uuids).all();

  const capes = Object.fromEntries(results.filter(r => r.cape).map(r => [r.uuid, r.cape]));
  return json({ users: results.map(r => r.uuid), capes });
}

// ========== Status und Spielsitzungen ==========
// Der Launcher meldet sich jede Minute, solange gespielt wird. Daraus entstehen Sitzungen (Server, Instanz, Beginn,
// Ende), aus denen das Freundesprofil gemeinsame Spielzeit und gemeinsame Server berechnet.

async function setStatus({ db, body, me }) {
  if (body.playing === false) {
    await db.prepare("DELETE FROM status WHERE uuid = ?1").bind(me).run();
    return json({ ok: true });
  }

  const server = isString(body.server, SERVER_ADDRESS) ? body.server : null;
  const version = typeof body.version === "string" ? body.version.slice(0, 64) : null;
  const instance = cleanText(body.instance, 60) || null;
  const now = Date.now();
  const open = await db.prepare(
    "SELECT id FROM sessions WHERE uuid = ?1 AND ended > ?2 AND server IS ?3 AND instance IS ?4 " +
    "ORDER BY ended DESC LIMIT 1"
  ).bind(me, now - SESSION_GAP, server, instance).first();

  await db.batch([
    db.prepare(
      "INSERT INTO status (uuid, server, version, instance, updated) VALUES (?1, ?2, ?3, ?4, ?5) " +
      "ON CONFLICT(uuid) DO UPDATE SET server = excluded.server, version = excluded.version, " +
      "instance = excluded.instance, updated = excluded.updated"
    ).bind(me, server, version, instance, now),
    ...(open
      ? [db.prepare("UPDATE sessions SET ended = ?1 WHERE id = ?2").bind(now, open.id)]
      : [
        db.prepare("INSERT INTO sessions (uuid, server, instance, started, ended) VALUES (?1, ?2, ?3, ?4, ?4)")
          .bind(me, server, instance, now),
        db.prepare("DELETE FROM sessions WHERE uuid = ?1 AND ended < ?2").bind(me, now - SESSION_DAYS * DAY)
      ])
  ]);
  return json({ ok: true });
}

// ========== Freunde ==========
// Server, Instanz und Online-Status sehen nur Freunde, die sich gegenseitig hinzugefügt haben.

async function listFriends({ db, me }) {
  const { results } = await db.prepare(
    "SELECT u.uuid, u.name, u.last_seen, s.server, s.version, s.instance, s.updated, " +
    "(SELECT f.created FROM friends f WHERE f.owner = ?1 AND f.friend = u.uuid) AS outgoing, " +
    "(SELECT f.created FROM friends f WHERE f.owner = u.uuid AND f.friend = ?1) AS incoming " +
    "FROM users u LEFT JOIN status s ON s.uuid = u.uuid " +
    "WHERE u.uuid IN (SELECT friend FROM friends WHERE owner = ?1 UNION SELECT owner FROM friends WHERE friend = ?1) " +
    "ORDER BY u.name COLLATE NOCASE"
  ).bind(me).all();

  const onlineSince = Date.now() - ONLINE_MILLIS;
  return json({
    friends: results.map(r => {
      const mutual = r.outgoing != null && r.incoming != null;
      const playing = mutual && r.updated > onlineSince;
      return {
        uuid: r.uuid,
        name: r.name,
        state: mutual ? "friend" : r.outgoing != null ? "outgoing" : "incoming",
        playing,
        online: mutual && (playing || r.last_seen > onlineSince),
        server: playing ? r.server : null,
        version: playing ? r.version : null,
        instance: playing ? r.instance : null,
        since: mutual ? Math.max(r.outgoing, r.incoming) : null,
        seen: mutual ? r.last_seen : null
      };
    })
  });
}

/** Ist die Gegenseite schon befreundet, wird daraus "angenommen", sonst eine neue Anfrage. */
async function addFriend({ db, body, me }) {
  const name = typeof body.name === "string" ? body.name.trim() : "";
  expect(PLAYER_NAME.test(name), "Ungültiger Spielername");
  const user = await db.prepare("SELECT uuid, name FROM users WHERE name = ?1 COLLATE NOCASE").bind(name).first();
  expect(user, `${name} nutzt AxoClient nicht oder hat damit noch nie gespielt.`, 404);
  expect(user.uuid !== me, "Du kannst dich nicht selbst hinzufügen.");
  const { n } = await db.prepare("SELECT COUNT(*) AS n FROM friends WHERE owner = ?1").bind(me).first();
  expect(n < MAX_FRIENDS, "Zu viele Freunde");

  const now = Date.now();
  const added = await db.prepare("INSERT OR IGNORE INTO friends (owner, friend, created) VALUES (?1, ?2, ?3)")
    .bind(me, user.uuid, now).run();
  if (added.meta?.changes > 0) {
    const accepted = !!await db.prepare("SELECT 1 FROM friends WHERE owner = ?1 AND friend = ?2")
      .bind(user.uuid, me).first();
    await db.batch([
      db.prepare(
        "DELETE FROM notifications WHERE kind = 'friend-request' " +
        "AND ((recipient = ?1 AND sender = ?2) OR (recipient = ?2 AND sender = ?1))"
      ).bind(user.uuid, me),
      notify(db, user.uuid, accepted ? "friend-accepted" : "friend-request", me, now)
    ]);
  }
  return json({ ok: true, name: user.name });
}

/** Entfernt die Freundschaft in beide Richtungen (auch: Anfrage ablehnen oder zurückziehen). */
async function removeFriend({ db, body, me }) {
  expect(isString(body.uuid, UUID));
  await db.batch([
    db.prepare("DELETE FROM friends WHERE (owner = ?1 AND friend = ?2) OR (owner = ?2 AND friend = ?1)")
      .bind(me, body.uuid),
    db.prepare("DELETE FROM notifications WHERE (recipient = ?1 AND sender = ?2) OR (recipient = ?2 AND sender = ?1)")
      .bind(me, body.uuid)
  ]);
  return json({ ok: true });
}

/** Seit wann a und b gegenseitig befreundet sind, oder null. */
async function friendsSince(db, a, b) {
  const row = await db.prepare(
    "SELECT (SELECT created FROM friends WHERE owner = ?1 AND friend = ?2) AS a, " +
    "(SELECT created FROM friends WHERE owner = ?2 AND friend = ?1) AS b"
  ).bind(a, b).first();
  return row?.a != null && row?.b != null ? Math.max(row.a, row.b) : null;
}

async function areMutualFriends(db, a, b) {
  return await friendsSince(db, a, b) != null;
}

/** Gemeinsame Spielzeit = Überschneidung der Sitzungen auf demselben Server. */
async function friendProfile({ db, body, me }) {
  expect(isString(body.uuid, UUID));
  const since = await friendsSince(db, me, body.uuid);
  expect(since != null, "Ihr seid (noch) keine gegenseitigen Freunde.", 403);

  const overlap = "MAX(0, MIN(a.ended, b.ended) - MAX(a.started, b.started))";
  const shared =
    "FROM sessions a JOIN sessions b ON a.server = b.server AND a.started < b.ended AND b.started < a.ended " +
    "WHERE a.uuid = ?1 AND b.uuid = ?2 AND a.server IS NOT NULL";
  const [together, servers, recent] = await db.batch([
    db.prepare(`SELECT SUM(${overlap}) AS ms ${shared}`).bind(me, body.uuid),
    db.prepare(`SELECT a.server, SUM(${overlap}) AS ms ${shared} GROUP BY a.server ORDER BY ms DESC LIMIT 6`)
      .bind(me, body.uuid),
    db.prepare("SELECT server, instance, started, ended FROM sessions WHERE uuid = ?1 ORDER BY ended DESC LIMIT 6")
      .bind(body.uuid)
  ]);

  return json({
    since,
    together: Math.round((together.results[0]?.ms ?? 0) / 1000),
    servers: servers.results.map(r => r.server),
    recent: recent.results.map(r => ({ time: r.started, text: describeSession(r) }))
  });
}

function describeSession({ server, instance, started, ended }) {
  const minutes = Math.max(1, Math.round((ended - started) / MINUTE));
  const length = minutes >= 60 ? `${Math.floor(minutes / 60)} Std ${minutes % 60} Min` : `${minutes} Min`;
  return [server ? `Auf ${server}` : "Einzelspieler oder Menü", instance, length].filter(Boolean).join(" · ");
}

// ========== Einladungen und Benachrichtigungen ==========
// Arten: friend-request, friend-accepted, friend-online, invite. Den Text baut der Launcher selbst.

function notify(db, recipient, kind, sender, created, server = null, version = null) {
  return db.prepare(
    "INSERT INTO notifications (recipient, kind, sender, server, version, created) VALUES (?1, ?2, ?3, ?4, ?5, ?6)"
  ).bind(recipient, kind, sender, server, version, created);
}

async function invite({ db, body, me }) {
  expect(isString(body.to, UUID) && isString(body.server, SERVER_ADDRESS));
  expect(await areMutualFriends(db, me, body.to), "Einladen geht nur unter gegenseitigen Freunden.", 403);

  const now = Date.now();
  const { n } = await db.prepare(
    "SELECT COUNT(*) AS n FROM notifications WHERE sender = ?1 AND kind = 'invite' AND created > ?2"
  ).bind(me, now - HOUR).first();
  expect(n < MAX_INVITES_PER_HOUR, "Du hast in der letzten Stunde zu viele Einladungen verschickt.", 429);

  const version = typeof body.version === "string" ? body.version.slice(0, 64) : null;
  await db.batch([
    db.prepare("DELETE FROM notifications WHERE recipient = ?1 AND sender = ?2 AND kind = 'invite'").bind(body.to, me),
    notify(db, body.to, "invite", me, now, body.server, version)
  ]);
  return json({ ok: true });
}

async function listNotifications({ db, me }) {
  await db.prepare("DELETE FROM notifications WHERE recipient = ?1 AND created < ?2")
    .bind(me, Date.now() - NOTIFICATION_DAYS * DAY).run();
  const { results } = await db.prepare(
    "SELECT n.id, n.kind, n.sender, u.name, n.text, n.server, n.version, n.created, n.read " +
    "FROM notifications n LEFT JOIN users u ON u.uuid = n.sender " +
    "WHERE n.recipient = ?1 ORDER BY n.created DESC LIMIT 50"
  ).bind(me).all();

  return json({
    notifications: results.map(r => ({
      id: r.id, kind: r.kind, fromUuid: r.sender, fromName: r.name, text: r.text,
      server: r.server, version: r.version, created: r.created, read: !!r.read
    }))
  });
}

async function readNotifications({ db, me }) {
  await db.prepare("UPDATE notifications SET read = 1 WHERE recipient = ?1 AND read = 0").bind(me).run();
  return json({ ok: true });
}

// ========== Admins ==========
// Der Launcher löst Spielernamen vorher bei Mojang in die UUID auf (Cloudflare wird dort blockiert).

async function listAdmins({ db }) {
  const { results } = await db.prepare("SELECT uuid, name FROM admins ORDER BY name COLLATE NOCASE").all();
  return json({ admins: results.map(r => ({ uuid: r.uuid, name: r.name })) });
}

async function addAdmin({ env, db, body, me }) {
  expect(isString(body.uuid, UUID) && isString(body.name, PLAYER_NAME));
  expect(!ownerUuids(env).includes(body.uuid), "Der Besitzer ist sowieso Admin.");
  await db.prepare(
    "INSERT INTO admins (uuid, name, added_by, created) VALUES (?1, ?2, ?3, ?4) " +
    "ON CONFLICT(uuid) DO UPDATE SET name = excluded.name"
  ).bind(body.uuid, body.name, me, Date.now()).run();
  return json({ ok: true });
}

async function removeAdmin({ db, body }) {
  expect(isString(body.uuid, UUID));
  await db.prepare("DELETE FROM admins WHERE uuid = ?1").bind(body.uuid).run();
  return json({ ok: true });
}

// ========== Umhänge ==========
// cape_images enthält die Bilder: owner NULL = für alle (nur Admins laden sie hoch), sonst ein eigener Umhang, den
// nur sein Besitzer trägt (jeder darf MAX_PERSONAL_CAPES davon haben). Die Tabelle capes merkt sich, wer welchen trägt.

/** SQL-Bedingung: Umhang i darf von diesem Spieler getragen werden. */
const WEARABLE_BY = uuid => `(i.owner IS NULL OR i.owner = ${uuid})`;

/** Ohne "cape" im Body: eigenen Umhang und Rolle liefern. Mit "cape" (ID oder null): anlegen bzw. ablegen. */
async function cape(ctx) {
  const { db, body, me } = ctx;
  if (!("cape" in body)) {
    const row = await db.prepare(
      `SELECT i.id FROM capes c JOIN cape_images i ON i.id = c.cape AND ${WEARABLE_BY("c.uuid")} WHERE c.uuid = ?1`
    ).bind(me).first();
    return json({ cape: row?.id ?? null, role: await roleOf(ctx, me) });
  }
  if (body.cape === null) {
    await db.prepare("DELETE FROM capes WHERE uuid = ?1").bind(me).run();
    return json({ ok: true, cape: null });
  }
  expect(isString(body.cape, CAPE_ID), "Ungültiger Umhang");
  const image = await capeImageInfo(db, body.cape);
  expect(image, "Diesen Umhang gibt es nicht mehr.", 404);
  expect(!image.owner || image.owner === me, "Diesen Umhang darf nur sein Besitzer tragen.", 403);
  await db.prepare("INSERT INTO capes (uuid, cape) VALUES (?1, ?2) ON CONFLICT(uuid) DO UPDATE SET cape = excluded.cape")
    .bind(me, body.cape).run();
  return json({ ok: true, cape: body.cape });
}

function capeImageInfo(db, id) {
  return db.prepare("SELECT id, owner FROM cape_images WHERE id = ?1").bind(id).first();
}

async function allCapes(db) {
  const { results } = await db.prepare("SELECT id, name, owner FROM cape_images ORDER BY owner IS NOT NULL, created").all();
  return results.map(r => r.owner ? { id: r.id, name: r.name, owner: r.owner } : { id: r.id, name: r.name });
}

async function capeList({ db }) {
  return json(await allCapes(db), 200, { "Cache-Control": "public, max-age=60" });
}

async function capeImage({ db }, id) {
  const row = await db.prepare("SELECT png FROM cape_images WHERE id = ?1").bind(id).first();
  if (!row)
    return new Response("Diesen Umhang gibt es nicht.", { status: 404 });
  return new Response(new Uint8Array(row.png), {
    headers: { "Content-Type": "image/png", "Cache-Control": "public, max-age=300" }
  });
}

/** "global" (Standard) nur für Admins, sonst ein eigener Umhang. */
async function uploadCape(ctx) {
  const { db, body, me } = ctx;
  const global = body.global !== false;
  if (global)
    expect(await roleOf(ctx, me), "Umhänge für alle dürfen nur Admins hochladen.", 403);

  const name = cleanText(body.name, 32);
  expect(name, "Gib dem Umhang einen Namen.");
  expect(typeof body.png === "string" && body.png.length > 0, "Es fehlt das Bild.");

  let png;
  try {
    png = base64ToBytes(body.png);
  } catch {
    throw new HttpError(400, "Das Bild ist beschädigt.");
  }
  expect(png.length <= MAX_CAPE_BYTES, "Das Bild ist zu groß (höchstens 1,5 MB).", 413);
  const size = pngSize(png);
  expect(size, "Das ist kein PNG-Bild.");
  expect(size.width >= 64 && size.width <= MAX_CAPE_WIDTH && size.width % 64 === 0 && size.height * 2 === size.width,
    `Ein Umhang muss 64×32 Pixel groß sein oder ein Vielfaches davon (bis ${MAX_CAPE_WIDTH}×${MAX_CAPE_WIDTH / 2}).`);

  const capes = await allCapes(db);
  if (!global)
    expect(capes.filter(c => c.owner === me).length < MAX_PERSONAL_CAPES,
      `Du kannst höchstens ${MAX_PERSONAL_CAPES} eigene Umhänge haben. Lösche erst einen.`, 429);

  const id = freeId(slug(name), new Set(capes.map(c => c.id)));
  await db.prepare("INSERT INTO cape_images (id, name, png, uploader, created, owner) VALUES (?1, ?2, ?3, ?4, ?5, ?6)")
    .bind(id, name, png.buffer, me, Date.now(), global ? null : me).run();
  return json({ ok: true, id, name });
}

/** Eigene Umhänge löscht ihr Besitzer, alle anderen nur Admins. Wer ihn trägt, hat danach keinen mehr. */
async function deleteCape(ctx) {
  const { db, body, me } = ctx;
  expect(isString(body.id, CAPE_ID), "Ungültiger Umhang");
  const image = await capeImageInfo(db, body.id);
  expect(image, "Diesen Umhang gibt es nicht mehr.", 404);
  if (image.owner !== me)
    expect(await roleOf(ctx, me), "Diesen Umhang dürfen nur Admins löschen.", 403);
  await db.batch([
    db.prepare("DELETE FROM cape_images WHERE id = ?1").bind(body.id),
    db.prepare("DELETE FROM capes WHERE cape = ?1").bind(body.id)
  ]);
  return json({ ok: true });
}

/** Breite und Höhe aus dem PNG-Kopf, oder null, wenn es kein PNG ist. */
function pngSize(bytes) {
  const signature = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];
  if (bytes.length < 24 || signature.some((b, i) => bytes[i] !== b))
    return null;
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  return { width: view.getUint32(16), height: view.getUint32(20) };
}

function slug(name) {
  return name.toLowerCase()
    .replace(/ä/g, "ae").replace(/ö/g, "oe").replace(/ü/g, "ue").replace(/ß/g, "ss")
    .replace(/[^a-z0-9]+/g, "-").replace(/^-+/, "").slice(0, 28).replace(/-+$/, "") || "umhang";
}

function freeId(base, taken) {
  for (let n = 1; ; n++) {
    const id = n === 1 ? base : `${base}-${n}`;
    if (!taken.has(id))
      return id;
  }
}

// ========== Geteilte Pakete ==========
// Freunde schicken sich Instanzen, Overlay-Einstellungen, Mods und Server. Der Dienst reicht die Pakete nur weiter
// (ein Postfach je Person) und prüft bloß das Format; den Inhalt prüft der Launcher des Empfängers.

/** Nur Pakete von Personen, mit denen man noch gegenseitig befreundet ist. ?1 ist der Empfänger. */
const FROM_MUTUAL_FRIEND =
  "EXISTS (SELECT 1 FROM friends f WHERE f.owner = s.sender AND f.friend = ?1) " +
  "AND EXISTS (SELECT 1 FROM friends f WHERE f.owner = ?1 AND f.friend = s.sender)";

async function sendShare({ db, body, me }) {
  expect(isString(body.to, UUID), "Ungültiger Empfänger");
  expect(body.to !== me, "Du kannst dir nichts selbst schicken.");
  expect(SHARE_KINDS.includes(body.kind), "Unbekannte Art von Paket");
  expect(typeof body.payload === "string" && body.payload.length > 0, "Leeres Paket");
  expect(body.payload.length <= MAX_SHARE_CHARS, "Das Paket ist zu groß zum Verschicken.", 413);
  expect(parseJson(body.payload)?.format === "axoclient-" + body.kind, "Das ist kein gültiges AxoClient-Paket.");
  expect(await areMutualFriends(db, me, body.to),
    "Ihr müsst euch gegenseitig als Freunde hinzugefügt haben, um etwas zu teilen.", 403);

  const now = Date.now();
  await db.prepare("DELETE FROM shares WHERE created < ?1").bind(now - SHARE_DAYS * DAY).run();
  const counts = await db.prepare(
    "SELECT (SELECT COUNT(*) FROM shares WHERE recipient = ?1) AS inbox, " +
    "(SELECT COUNT(*) FROM shares WHERE recipient = ?1 AND sender = ?2) AS pair, " +
    "(SELECT COUNT(*) FROM shares WHERE sender = ?2 AND created > ?3) AS hour"
  ).bind(body.to, me, now - HOUR).first();
  expect(counts.inbox < MAX_INBOX, "Das Postfach deines Freundes ist voll.", 429);
  expect(counts.pair < MAX_PENDING_PER_PAIR,
    "Dein Freund hat noch zu viele ungelesene Pakete von dir. Warte, bis er sie abgeholt hat.", 429);
  expect(counts.hour < MAX_SENDS_PER_HOUR,
    "Du hast in der letzten Stunde zu viel verschickt. Versuche es später noch einmal.", 429);

  const title = cleanText(body.title, 100) || body.kind;
  await db.prepare("INSERT INTO shares (sender, recipient, kind, title, payload, created) VALUES (?1, ?2, ?3, ?4, ?5, ?6)")
    .bind(me, body.to, body.kind, title, body.payload, now).run();
  return json({ ok: true });
}

/** Postfach ohne Inhalt; den holt der Launcher erst, wenn man ein Paket öffnet. */
async function shareInbox({ db, me }) {
  await db.prepare("DELETE FROM shares WHERE recipient = ?1 AND created < ?2")
    .bind(me, Date.now() - SHARE_DAYS * DAY).run();
  const { results } = await db.prepare(
    "SELECT s.id, s.kind, s.title, s.created, length(s.payload) AS size, u.uuid AS from_uuid, u.name AS from_name " +
    "FROM shares s JOIN users u ON u.uuid = s.sender " +
    `WHERE s.recipient = ?1 AND ${FROM_MUTUAL_FRIEND} ORDER BY s.created DESC LIMIT 100`
  ).bind(me).all();

  return json({
    shares: results.map(r => ({
      id: r.id, kind: r.kind, title: r.title, size: r.size, created: r.created,
      fromUuid: r.from_uuid, fromName: r.from_name
    }))
  });
}

async function getShare({ db, body, me }) {
  expect(isShareId(body.id));
  const row = await db.prepare(
    `SELECT s.kind, s.payload FROM shares s WHERE s.id = ?2 AND s.recipient = ?1 AND ${FROM_MUTUAL_FRIEND}`
  ).bind(me, body.id).first();
  expect(row, "Dieses Paket gibt es nicht mehr.", 404);
  return json({ kind: row.kind, payload: row.payload });
}

async function deleteShare({ db, body, me }) {
  expect(isShareId(body.id));
  await db.prepare("DELETE FROM shares WHERE id = ?1 AND recipient = ?2").bind(body.id, me).run();
  return json({ ok: true });
}

function isShareId(value) {
  return Number.isSafeInteger(value) && value > 0;
}

// ========== Hilfsfunktionen ==========

function json(data, status = 200, headers = {}) {
  return new Response(JSON.stringify(data), {
    status,
    headers: { "Content-Type": "application/json; charset=utf-8", ...headers }
  });
}

function isString(value, pattern) {
  return typeof value === "string" && pattern.test(value);
}

function cleanText(value, maxLength) {
  return (typeof value === "string" ? value : "").replace(/\p{Cc}/gu, "").trim().slice(0, maxLength);
}

function parseJson(text) {
  try {
    const value = JSON.parse(text);
    return value && typeof value === "object" ? value : null;
  } catch {
    return null;
  }
}

function base64ToBytes(text) {
  return Uint8Array.from(atob(text), c => c.charCodeAt(0));
}

function hexToBytes(hex) {
  return Uint8Array.from(hex.match(/../g), h => parseInt(h, 16));
}

function toHex(bytes) {
  return [...bytes].map(x => x.toString(16).padStart(2, "0")).join("");
}

function randomHex(byteCount) {
  return toHex(crypto.getRandomValues(new Uint8Array(byteCount)));
}

async function sha256(text) {
  return toHex(new Uint8Array(await crypto.subtle.digest("SHA-256", new TextEncoder().encode(text))));
}
