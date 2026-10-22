// AxoClient-Dienst (Cloudflare Worker + D1 + Durable Object), Einrichtung siehe ANLEITUNG.md
//
// Zwei Schnittstellen in einem Worker:
//   - die alte (ohne Präfix, POST mit Token im Body) für Launcher bis 1.x – unverändert, damit alte Launcher laufen
//   - v2 unter /v2/… (REST, Token im Authorization-Header) für den neuen Launcher und die AxoMod
// Beide nutzen dieselben Tabellen: Freunde, Status, Umhänge und Benachrichtigungen sind gemeinsam.
//
// Bindungen und Variablen des Workers:
//   DB            D1-Datenbank
//   LIVE          Durable Object "LiveRoom" (Echtzeit-Kanal für Emotes); ohne Bindung antwortet /v2/live mit 503
//   OWNER_UUID    Minecraft-UUID des Besitzers (mehrere mit Komma getrennt); er ernennt die Admins
//   MIN_LAUNCHER  optional: kleinste Launcher-Version für v2 (z. B. "1.0.0"); ältere bekommen "update_required"

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
    const { pathname } = new URL(request.url);
    if (pathname === "/v2" || pathname.startsWith("/v2/")) {
      try {
        return await handleV2(request, env);
      } catch (e) {
        return v2Error(e);
      }
    }
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
  "CREATE INDEX IF NOT EXISTS shares_sender ON shares (sender, created)",
  // ---- v2 ----
  "CREATE TABLE IF NOT EXISTS blocks (owner TEXT NOT NULL, blocked TEXT NOT NULL, created INTEGER NOT NULL, " +
  "PRIMARY KEY (owner, blocked))",
  "CREATE INDEX IF NOT EXISTS blocks_blocked ON blocks (blocked)",
  "CREATE TABLE IF NOT EXISTS inbox (id TEXT PRIMARY KEY, sender TEXT NOT NULL, recipient TEXT NOT NULL, " +
  "kind TEXT NOT NULL, title TEXT NOT NULL, payload TEXT NOT NULL, created INTEGER NOT NULL, expires INTEGER NOT NULL)",
  "CREATE INDEX IF NOT EXISTS inbox_recipient ON inbox (recipient, created)",
  "CREATE INDEX IF NOT EXISTS inbox_sender ON inbox (sender, created)",
  "CREATE TABLE IF NOT EXISTS cape_submissions (id TEXT PRIMARY KEY, uuid TEXT NOT NULL, local_id TEXT NOT NULL, " +
  "name TEXT NOT NULL, png BLOB NOT NULL, created INTEGER NOT NULL)",
  "CREATE INDEX IF NOT EXISTS cape_submissions_uuid ON cape_submissions (uuid)"
];

// Spalten, die später dazugekommen sind; ALTER TABLE scheitert, wenn es sie schon gibt.
const ADDED_COLUMNS = [
  ["status", "instance TEXT"],
  ["cape_images", "owner TEXT"],
  // ---- v2 ----
  ["tokens", "device TEXT"],
  ["status", "state TEXT"],
  ["status", "since INTEGER"],
  ["notifications", "data TEXT"],
  ["cape_images", "local_id TEXT"]
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

/** prefix: "mclauncher-badge" (alter Launcher) oder "axoclient" (v2) – steht in der signierten Nachricht. */
async function verifyCertificate(b, prefix = "mclauncher-badge", keySignature = b.keySignature) {
  const publicKey = base64ToBytes(b.publicKey);

  const certified = new Uint8Array(24 + publicKey.length);
  certified.set(hexToBytes(b.uuid), 0);
  new DataView(certified.buffer).setBigUint64(16, BigInt(b.expiresAt));
  certified.set(publicKey, 24);
  let issuedByMojang = false;
  for (const key of MOJANG_KEYS)
    issuedByMojang ||= await verifyRsa(base64ToBytes(key), "SHA-1", keySignature, certified);
  expect(issuedByMojang, "Spieler-Zertifikat wurde nicht von Mojang ausgestellt", 403);

  const message = new TextEncoder().encode(`${prefix}:register:${b.uuid}:${b.timestamp}`);
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

  const results = await activePlayers(db, uuids);
  const capes = Object.fromEntries(results.filter(r => r.cape).map(r => [r.uuid, r.cape]));
  return json({ users: results.map(r => r.uuid), capes });
}

/** Aktive AxoClient-Spieler unter diesen UUIDs, mit dem Umhang, den sie tragen dürfen. */
async function activePlayers(db, uuids) {
  const placeholders = uuids.map((_, i) => "?" + (i + 2)).join(",");
  const { results } = await db.prepare(
    "SELECT u.uuid, i.id AS cape FROM users u LEFT JOIN capes c ON c.uuid = u.uuid " +
    `LEFT JOIN cape_images i ON i.id = c.cape AND ${WEARABLE_BY("c.uuid")} ` +
    `WHERE u.last_seen > ?1 AND u.uuid IN (${placeholders})`
  ).bind(Date.now() - ACTIVE_DAYS * DAY, ...uuids).all();
  return results;
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
  await db.batch([
    db.prepare(
      "INSERT INTO status (uuid, server, version, instance, updated, state, since) VALUES (?1, ?2, ?3, ?4, ?5, NULL, NULL) " +
      "ON CONFLICT(uuid) DO UPDATE SET server = excluded.server, version = excluded.version, " +
      "instance = excluded.instance, updated = excluded.updated, state = NULL, since = NULL"
    ).bind(me, server, version, instance, now),
    ...await sessionStatements(db, me, server, instance, now)
  ]);
  return json({ ok: true });
}

/** Laufende Sitzung verlängern oder eine neue beginnen (gemeinsam für alten und v2-Status). */
async function sessionStatements(db, me, server, instance, now) {
  const open = await db.prepare(
    "SELECT id FROM sessions WHERE uuid = ?1 AND ended > ?2 AND server IS ?3 AND instance IS ?4 " +
    "ORDER BY ended DESC LIMIT 1"
  ).bind(me, now - SESSION_GAP, server, instance).first();
  return open
    ? [db.prepare("UPDATE sessions SET ended = ?1 WHERE id = ?2").bind(now, open.id)]
    : [
      db.prepare("INSERT INTO sessions (uuid, server, instance, started, ended) VALUES (?1, ?2, ?3, ?4, ?4)")
        .bind(me, server, instance, now),
      db.prepare("DELETE FROM sessions WHERE uuid = ?1 AND ended < ?2").bind(me, now - SESSION_DAYS * DAY)
    ];
}

// ========== Freunde ==========
// Server, Instanz und Online-Status sehen nur Freunde, die sich gegenseitig hinzugefügt haben.

async function listFriends({ db, me }) {
  const { results } = await db.prepare(
    "SELECT u.uuid, u.name, u.last_seen, s.server, s.version, s.instance, s.updated, s.state, " +
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
      // v2-Launcher melden sich auch im Menü; "spielt" heißt dort state = ingame, "versteckt" state = offline
      const hidden = r.state === "offline";
      const playing = mutual && !hidden && r.updated > onlineSince && (r.state == null || r.state === "ingame");
      return {
        uuid: r.uuid,
        name: r.name,
        state: mutual ? "friend" : r.outgoing != null ? "outgoing" : "incoming",
        playing,
        online: mutual && !hidden && (playing || r.last_seen > onlineSince),
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
  expect(!await isBlockedEitherWay(db, me, user.uuid), "Das ist gerade nicht möglich.", 403);
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
  await db.batch(unfriendStatements(db, me, body.uuid));
  return json({ ok: true });
}

function unfriendStatements(db, a, b) {
  return [
    db.prepare("DELETE FROM friends WHERE (owner = ?1 AND friend = ?2) OR (owner = ?2 AND friend = ?1)").bind(a, b),
    db.prepare("DELETE FROM notifications WHERE (recipient = ?1 AND sender = ?2) OR (recipient = ?2 AND sender = ?1)")
      .bind(a, b)
  ];
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

async function isBlockedEitherWay(db, a, b) {
  return !!await db.prepare(
    "SELECT 1 FROM blocks WHERE (owner = ?1 AND blocked = ?2) OR (owner = ?2 AND blocked = ?1)"
  ).bind(a, b).first();
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
// v2 legt zusätzlich "inbox" und "capeReview" an (mit JSON in "data"); die sehen alte Launcher nicht.

const LEGACY_KINDS = "('friend-request', 'friend-accepted', 'friend-online', 'invite')";

function notify(db, recipient, kind, sender, created, server = null, version = null, data = null) {
  return db.prepare(
    "INSERT INTO notifications (recipient, kind, sender, server, version, created, data) " +
    "VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7)"
  ).bind(recipient, kind, sender, server, version, created, data == null ? null : JSON.stringify(data));
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
    `WHERE n.recipient = ?1 AND n.kind IN ${LEGACY_KINDS} ORDER BY n.created DESC LIMIT 50`
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
// v2: eigene Umhänge entstehen über eine Einreichung, die ein Admin freigibt; local_id ist die ID im Launcher.

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

// =====================================================================================================================
// ========== v2 =======================================================================================================
// =====================================================================================================================
// REST unter /v2. Token im Header "Authorization: Bearer <token>". Fehler: { "error": { "code": "...", "message": "..." } }
// – der Launcher zeigt den Text zu "service.<code>" an. Die Mod fragt nur öffentliche Routen ab (/v2/players, Bilder).

const V2_INBOX_KINDS = ["instance", "mod", "server", "invite"];
const V2_INBOX_DAYS = 14;
const V2_MAX_RECIPIENTS = 20;
const V2_MAX_SENDS_PER_HOUR = 60;      // Pakete je Stunde (jeder Empfänger zählt einzeln)
const V2_PRESENCE = ["offline", "launcher", "ingame"];
const LOCAL_ID = /^[a-z0-9_-]{1,64}$/;
const HEX_ID = /^[0-9a-f]{16,32}$/;
const MAX_CAPE_BYTES_V2 = 1_572_864;  // wie im Launcher (1,5 MiB)

class ApiError extends Error {
  constructor(status, code, message = code) {
    super(message);
    this.status = status;
    this.code = code;
  }
}

function need(condition, status = 400, code = "invalid_request", message = undefined) {
  if (!condition)
    throw new ApiError(status, code, message);
}

function v2Error(e) {
  if (e instanceof ApiError)
    return json({ error: { code: e.code, message: e.message } }, e.status);
  // Fehler aus gemeinsamen Hilfsfunktionen (HttpError) in v2-Form übersetzen
  if (e instanceof HttpError) {
    const code = { 400: "invalid_request", 401: "unauthorized", 403: "forbidden", 404: "not_found", 413: "too_large",
      429: "rate_limited" }[e.status] ?? "error";
    return json({ error: { code, message: e.message } }, e.status);
  }
  return json({ error: { code: "error", message: "Interner Fehler: " + e.message } }, 500);
}

// [Methode, Pfad (Regex, Gruppen = Parameter), Zugriff, Handler]
const V2_ROUTES = [
  ["GET", /^\/v2\/?$/, "public", () => json({ ok: true, service: "axoclient", api: 2 })],
  ["GET", /^\/v2\/health$/, "public", () => json({ ok: true })],

  ["POST", /^\/v2\/auth\/register$/, "public", v2Register],
  ["DELETE", /^\/v2\/auth\/token$/, "user", v2SignOut],

  ["GET", /^\/v2\/me$/, "user", v2Me],
  ["DELETE", /^\/v2\/me$/, "user", v2DeleteMe],
  ["GET", /^\/v2\/sync$/, "user", v2Sync],
  ["PUT", /^\/v2\/status$/, "user", v2Status],

  ["POST", /^\/v2\/friends\/requests$/, "user", v2Request],
  ["DELETE", /^\/v2\/friends\/requests\/([0-9a-f]{32})$/, "user", v2Decline],
  ["POST", /^\/v2\/friends\/([0-9a-f]{32})$/, "user", v2Accept],
  ["DELETE", /^\/v2\/friends\/([0-9a-f]{32})$/, "user", v2Unfriend],
  ["POST", /^\/v2\/blocks\/([0-9a-f]{32})$/, "user", v2Block],
  ["DELETE", /^\/v2\/blocks\/([0-9a-f]{32})$/, "user", v2Unblock],
  ["POST", /^\/v2\/notifications\/read$/, "user", v2ReadNotifications],

  ["POST", /^\/v2\/inbox$/, "user", v2InboxSend],
  ["GET", /^\/v2\/inbox\/([0-9a-f]{16,32})$/, "user", v2InboxGet],
  ["DELETE", /^\/v2\/inbox\/([0-9a-f]{16,32})$/, "user", v2InboxDelete],
  ["DELETE", /^\/v2\/invites\/([0-9a-f]{32})$/, "user", v2InviteWithdraw],

  ["GET", /^\/v2\/capes$/, "public", v2Capes],
  ["GET", /^\/v2\/capes\/([a-z0-9_-]{1,32})\.png$/, "public", (ctx, id) => capeImage(ctx, id)],
  ["PUT", /^\/v2\/capes\/active$/, "user", v2SetActiveCape],
  ["POST", /^\/v2\/capes\/submissions$/, "user", v2Submit],
  ["DELETE", /^\/v2\/capes\/submissions$/, "user", v2Withdraw],
  ["GET", /^\/v2\/submissions\/([0-9a-f]{16,32})\.png$/, "public", v2SubmissionImage],

  ["GET", /^\/v2\/admin\/submissions$/, "admin", v2Submissions],
  ["POST", /^\/v2\/admin\/submissions\/([0-9a-f]{16,32})$/, "admin", v2Review],
  ["POST", /^\/v2\/admin\/capes$/, "admin", v2UploadOfficial],
  ["DELETE", /^\/v2\/admin\/capes\/([a-z0-9_-]{1,32})$/, "admin", v2DeleteOfficial],
  ["GET", /^\/v2\/admin\/admins$/, "owner", v2Admins],
  ["PUT", /^\/v2\/admin\/admins\/([0-9a-f]{32})$/, "owner", v2AddAdmin],
  ["DELETE", /^\/v2\/admin\/admins\/([0-9a-f]{32})$/, "owner", v2RemoveAdmin],

  ["POST", /^\/v2\/players$/, "public", v2Players],
  ["GET", /^\/v2\/live$/, "user", v2Live]
];

async function handleV2(request, env) {
  const url = new URL(request.url);
  let route = null;
  let params = [];
  let pathKnown = false;
  for (const [method, pattern, access, handler] of V2_ROUTES) {
    const match = url.pathname.match(pattern);
    if (!match)
      continue;
    pathKnown = true;
    if (method === request.method) {
      route = [access, handler];
      params = match.slice(1);
      break;
    }
  }
  need(route, pathKnown ? 405 : 404, pathKnown ? "method_not_allowed" : "not_found");
  checkLauncherVersion(request, env);

  await ensureSchema(env.DB);
  const ctx = { env, db: env.DB, request, url, body: {}, form: null, me: null };
  const type = request.headers.get("Content-Type") ?? "";
  if (["POST", "PUT"].includes(request.method)) {
    if (type.startsWith("multipart/form-data"))
      ctx.form = await request.formData().catch(() => null);
    else {
      const parsed = await request.json().catch(() => null);
      ctx.body = parsed && typeof parsed === "object" ? parsed : {};
    }
  }

  const [access, handler] = route;
  if (access !== "public") {
    ctx.me = await authenticateV2(ctx.db, request);
    if (access !== "user")
      await requireRoleV2(ctx, access);
  }
  return handler(ctx, ...params);
}

/** Optional: zu alte Launcher (Header X-AxoClient-Version) bekommen "update_required". */
function checkLauncherVersion(request, env) {
  const min = env.MIN_LAUNCHER;
  const version = request.headers.get("X-AxoClient-Version");
  if (!min || !version)
    return;
  const parts = v => v.split(/[.+-]/).slice(0, 3).map(n => parseInt(n, 10) || 0);
  const [a, b] = [parts(version), parts(min)];
  for (let i = 0; i < 3; i++) {
    if (a[i] > b[i])
      return;
    need(a[i] === b[i], 426, "update_required", "Bitte aktualisiere AxoClient.");
  }
}

async function authenticateV2(db, request) {
  const header = request.headers.get("Authorization") ?? "";
  const token = header.startsWith("Bearer ") ? header.slice(7).trim() : "";
  need(TOKEN.test(token), 401, "unauthorized");
  const row = await db.prepare(
    "SELECT t.uuid, u.last_seen FROM tokens t JOIN users u ON u.uuid = t.uuid WHERE t.hash = ?1 AND t.created > ?2"
  ).bind(await sha256(token), Date.now() - TOKEN_DAYS * DAY).first();
  need(row, 401, "unauthorized");
  await markSeen(db, row.uuid, row.last_seen ?? 0);
  return row.uuid;
}

async function requireRoleV2(ctx, needed) {
  const role = await roleOf(ctx, ctx.me);
  need(needed === "owner" ? role === "owner" : role !== null, 403, "forbidden");
}

// ---------- Anmeldung ----------

async function v2Register({ db, body: b }) {
  need(isString(b.uuid, UUID) && isString(b.name, PLAYER_NAME)
    && typeof b.publicKey === "string" && typeof b.publicKeySignature === "string" && typeof b.signature === "string"
    && Number.isSafeInteger(b.expiresAt) && Number.isSafeInteger(b.timestamp));

  const now = Date.now();
  need(Math.abs(now - b.timestamp) <= MAX_CLOCK_SKEW, 403, "clock_skew", "Zeitstempel ungültig – stimmt die Uhr des PCs?");
  need(b.expiresAt >= now, 403, "certificate_expired");
  await verifyCertificate(b, "axoclient", b.publicKeySignature);

  const token = randomHex(32);
  await db.batch([
    db.prepare(
      "INSERT INTO users (uuid, name, last_seen) VALUES (?1, ?2, ?3) " +
      "ON CONFLICT(uuid) DO UPDATE SET name = excluded.name, last_seen = excluded.last_seen"
    ).bind(b.uuid, b.name, now),
    db.prepare("INSERT INTO tokens (hash, uuid, created, device) VALUES (?1, ?2, ?3, ?4)")
      .bind(await sha256(token), b.uuid, now, cleanText(b.device, 40) || null),
    db.prepare("DELETE FROM tokens WHERE created < ?1").bind(now - TOKEN_DAYS * DAY)
  ]);
  return json({ token, uuid: b.uuid, name: b.name });
}

async function v2SignOut({ db, request }) {
  const token = (request.headers.get("Authorization") ?? "").slice(7).trim();
  await db.prepare("DELETE FROM tokens WHERE hash = ?1").bind(await sha256(token)).run();
  return json({ ok: true });
}

// ---------- Konto ----------

async function meOf(ctx) {
  const user = await ctx.db.prepare("SELECT uuid, name FROM users WHERE uuid = ?1").bind(ctx.me).first();
  return { uuid: user.uuid, name: user.name, role: await roleOf(ctx, ctx.me) ?? "user" };
}

async function v2Me(ctx) {
  return json(await meOf(ctx));
}

/** Alles zu diesem Konto löschen (DSGVO). */
async function v2DeleteMe({ db, me }) {
  await db.batch([
    db.prepare("DELETE FROM tokens WHERE uuid = ?1").bind(me),
    db.prepare("DELETE FROM status WHERE uuid = ?1").bind(me),
    db.prepare("DELETE FROM friends WHERE owner = ?1 OR friend = ?1").bind(me),
    db.prepare("DELETE FROM sessions WHERE uuid = ?1").bind(me),
    db.prepare("DELETE FROM notifications WHERE recipient = ?1 OR sender = ?1").bind(me),
    db.prepare("DELETE FROM capes WHERE uuid = ?1").bind(me),
    db.prepare("DELETE FROM capes WHERE cape IN (SELECT id FROM cape_images WHERE owner = ?1)").bind(me),
    db.prepare("DELETE FROM cape_images WHERE owner = ?1").bind(me),
    db.prepare("DELETE FROM cape_submissions WHERE uuid = ?1").bind(me),
    db.prepare("DELETE FROM shares WHERE sender = ?1 OR recipient = ?1").bind(me),
    db.prepare("DELETE FROM inbox WHERE sender = ?1 OR recipient = ?1").bind(me),
    db.prepare("DELETE FROM blocks WHERE owner = ?1 OR blocked = ?1").bind(me),
    db.prepare("DELETE FROM admins WHERE uuid = ?1").bind(me),
    db.prepare("DELETE FROM users WHERE uuid = ?1").bind(me)
  ]);
  return json({ ok: true });
}

// ---------- Abgleich ----------
// Ein Aufruf alle 30–60 Sekunden: wer bin ich, Freunde mit Status, Benachrichtigungen, Postfach.

async function v2Sync(ctx) {
  const { db, me } = ctx;
  const now = Date.now();
  await db.batch([
    db.prepare("DELETE FROM notifications WHERE recipient = ?1 AND created < ?2").bind(me, now - NOTIFICATION_DAYS * DAY),
    db.prepare("DELETE FROM inbox WHERE recipient = ?1 AND expires < ?2").bind(me, now)
  ]);
  const [friends, notifications, inbox] = await db.batch([
    db.prepare(
      "SELECT u.uuid, u.name, u.last_seen, s.server, s.updated, s.state, s.since, " +
      "(SELECT f.created FROM friends f WHERE f.owner = ?1 AND f.friend = u.uuid) AS outgoing, " +
      "(SELECT f.created FROM friends f WHERE f.owner = u.uuid AND f.friend = ?1) AS incoming " +
      "FROM users u LEFT JOIN status s ON s.uuid = u.uuid " +
      "WHERE u.uuid IN (SELECT friend FROM friends WHERE owner = ?1 UNION SELECT owner FROM friends WHERE friend = ?1) " +
      "AND NOT EXISTS (SELECT 1 FROM blocks b WHERE b.owner = ?1 AND b.blocked = u.uuid) " +
      "ORDER BY u.name COLLATE NOCASE"
    ).bind(me),
    db.prepare(
      "SELECT n.id, n.kind, n.sender, u.name, n.server, n.version, n.data, n.created, n.read " +
      "FROM notifications n LEFT JOIN users u ON u.uuid = n.sender " +
      "WHERE n.recipient = ?1 AND n.kind != 'friend-online' ORDER BY n.created DESC LIMIT 50"
    ).bind(me),
    db.prepare(
      "SELECT i.id, i.kind, i.title, i.created, i.expires, u.uuid, u.name FROM inbox i JOIN users u ON u.uuid = i.sender " +
      "WHERE i.recipient = ?1 AND i.expires > ?2 " +
      "AND EXISTS (SELECT 1 FROM friends f WHERE f.owner = i.sender AND f.friend = ?1) " +
      "AND EXISTS (SELECT 1 FROM friends f WHERE f.owner = ?1 AND f.friend = i.sender) " +
      "ORDER BY i.created DESC LIMIT 100"
    ).bind(me, now)
  ]);

  return json({
    me: await meOf(ctx),
    friends: friends.results.map(r => v2Friend(r, now)),
    notifications: notifications.results.map(v2Notification).filter(Boolean),
    inbox: inbox.results.map(r => ({
      id: r.id, sender: { uuid: r.uuid, name: r.name }, kind: r.kind, title: r.title, created: r.created, expires: r.expires
    }))
  });
}

/** Status sehen nur gegenseitige Freunde. Alte Launcher melden sich nur im Spiel (ohne "state"). */
function v2Friend(r, now) {
  const mutual = r.outgoing != null && r.incoming != null;
  const relation = mutual ? "friends" : r.outgoing != null ? "pending" : "incoming";
  let state = "offline";
  if (mutual) {
    const fresh = r.updated > now - ONLINE_MILLIS;
    if (fresh && r.state)
      state = r.state;
    else if (fresh)
      state = "ingame";
    else if (r.last_seen > now - ONLINE_MILLIS && r.state !== "offline")
      state = "launcher";
  }
  return {
    uuid: r.uuid,
    name: r.name,
    relation,
    state,
    since: state === "offline" ? null : r.since ?? null,
    join: state === "ingame" ? r.server ?? null : null
  };
}

/** Benachrichtigung in v2-Form; alte Arten werden umbenannt, Daten kommen aus den Spalten. */
function v2Notification(r) {
  const from = r.sender ? { uuid: r.sender, name: r.name ?? "?" } : {};
  const extra = parseJson(r.data) ?? {};
  const base = { id: String(r.id), created: r.created, read: !!r.read };
  switch (r.kind) {
    case "friend-request":
      return { ...base, kind: "friendRequest", data: { ...from } };
    case "friend-accepted":
      return { ...base, kind: "friendAccepted", data: { ...from } };
    case "invite":
      return { ...base, kind: "invite", data: { ...from, ...extra, address: r.server, minecraft: r.version } };
    default:
      return { ...base, kind: r.kind, data: { ...from, ...extra } };
  }
}

async function v2Status({ db, body, me }) {
  need(V2_PRESENCE.includes(body.state));
  const join = body.state === "ingame" && isString(body.join, SERVER_ADDRESS) ? body.join : null;
  const since = Number.isSafeInteger(body.since) ? body.since : null;
  const now = Date.now();
  const statements = [db.prepare(
    "INSERT INTO status (uuid, server, version, instance, updated, state, since) VALUES (?1, ?2, NULL, NULL, ?3, ?4, ?5) " +
    "ON CONFLICT(uuid) DO UPDATE SET server = excluded.server, version = NULL, instance = NULL, " +
    "updated = excluded.updated, state = excluded.state, since = excluded.since"
  ).bind(me, join, now, body.state, since)];
  // Spielsitzungen wie beim alten Launcher, damit Freundesprofile gemeinsame Spielzeit kennen
  if (body.state === "ingame")
    statements.push(...await sessionStatements(db, me, join, null, now));
  await db.batch(statements);
  return json({ ok: true });
}

// ---------- Freunde ----------

async function v2Request({ db, body, me }) {
  const name = typeof body.name === "string" ? body.name.trim() : "";
  need(PLAYER_NAME.test(name), 400, "invalid_request");
  const user = await db.prepare("SELECT uuid, name FROM users WHERE name = ?1 COLLATE NOCASE").bind(name).first();
  need(user, 404, "unknown_player");
  need(user.uuid !== me, 400, "self");
  need(!await isBlockedEitherWay(db, me, user.uuid), 403, "blocked");

  const row = await db.prepare(
    "SELECT (SELECT 1 FROM friends WHERE owner = ?1 AND friend = ?2) AS outgoing, " +
    "(SELECT 1 FROM friends WHERE owner = ?2 AND friend = ?1) AS incoming"
  ).bind(me, user.uuid).first();
  need(!(row.outgoing && row.incoming), 409, "already_friends");
  need(!row.outgoing, 409, "already_requested");
  const { n } = await db.prepare("SELECT COUNT(*) AS n FROM friends WHERE owner = ?1").bind(me).first();
  need(n < MAX_FRIENDS, 429, "rate_limited");

  // Hat die Gegenseite schon angefragt, ist das eine Annahme
  const now = Date.now();
  await db.batch([
    db.prepare("INSERT OR IGNORE INTO friends (owner, friend, created) VALUES (?1, ?2, ?3)").bind(me, user.uuid, now),
    db.prepare(
      "DELETE FROM notifications WHERE kind = 'friend-request' " +
      "AND ((recipient = ?1 AND sender = ?2) OR (recipient = ?2 AND sender = ?1))"
    ).bind(user.uuid, me),
    notify(db, user.uuid, row.incoming ? "friend-accepted" : "friend-request", me, now)
  ]);
  return json({ ok: true, uuid: user.uuid, name: user.name, relation: row.incoming ? "friends" : "pending" });
}

async function v2Accept({ db, me }, uuid) {
  const incoming = await db.prepare("SELECT 1 FROM friends WHERE owner = ?1 AND friend = ?2").bind(uuid, me).first();
  need(incoming, 404, "not_found");
  need(!await isBlockedEitherWay(db, me, uuid), 403, "blocked");
  const now = Date.now();
  await db.batch([
    db.prepare("INSERT OR IGNORE INTO friends (owner, friend, created) VALUES (?1, ?2, ?3)").bind(me, uuid, now),
    db.prepare(
      "DELETE FROM notifications WHERE kind = 'friend-request' " +
      "AND ((recipient = ?1 AND sender = ?2) OR (recipient = ?2 AND sender = ?1))"
    ).bind(uuid, me),
    notify(db, uuid, "friend-accepted", me, now)
  ]);
  return json({ ok: true });
}

async function v2Decline({ db, me }, uuid) {
  await db.batch(unfriendStatements(db, me, uuid));
  return json({ ok: true });
}

async function v2Unfriend({ db, me }, uuid) {
  await db.batch(unfriendStatements(db, me, uuid));
  return json({ ok: true });
}

/** Blockieren beendet die Freundschaft und räumt Anfragen und Pakete zwischen beiden weg. */
async function v2Block({ db, me }, uuid) {
  need(uuid !== me, 400, "self");
  await db.batch([
    db.prepare("INSERT OR IGNORE INTO blocks (owner, blocked, created) VALUES (?1, ?2, ?3)").bind(me, uuid, Date.now()),
    ...unfriendStatements(db, me, uuid),
    db.prepare("DELETE FROM inbox WHERE (sender = ?1 AND recipient = ?2) OR (sender = ?2 AND recipient = ?1)").bind(me, uuid)
  ]);
  return json({ ok: true });
}

async function v2Unblock({ db, me }, uuid) {
  await db.prepare("DELETE FROM blocks WHERE owner = ?1 AND blocked = ?2").bind(me, uuid).run();
  return json({ ok: true });
}

async function v2ReadNotifications({ db, me }) {
  await db.prepare("UPDATE notifications SET read = 1 WHERE recipient = ?1 AND read = 0").bind(me).run();
  return json({ ok: true });
}

// ---------- Postfach ----------
// Instanzen (als Rezept), Mods, Server und Einladungen. Der Dienst prüft nur Größe und Format, den Inhalt der Empfänger.

async function v2InboxSend({ db, body, me }) {
  need(V2_INBOX_KINDS.includes(body.kind));
  need(typeof body.payload === "string" && body.payload.length > 0);
  need(body.payload.length <= MAX_SHARE_CHARS, 413, "too_large");
  const payload = parseJson(body.payload);
  need(payload, 400, "invalid_request");
  const recipients = Array.isArray(body.recipients)
    ? [...new Set(body.recipients.filter(u => isString(u, UUID) && u !== me))]
    : [];
  need(recipients.length > 0 && recipients.length <= V2_MAX_RECIPIENTS);
  for (const to of recipients) {
    need(await areMutualFriends(db, me, to), 403, "not_mutual");
    need(!await isBlockedEitherWay(db, me, to), 403, "blocked");
  }

  const now = Date.now();
  const { hour } = await db.prepare("SELECT COUNT(*) AS hour FROM inbox WHERE sender = ?1 AND created > ?2")
    .bind(me, now - HOUR).first();
  need(hour + recipients.length <= V2_MAX_SENDS_PER_HOUR, 429, "rate_limited");

  const title = cleanText(body.title, 100) || body.kind;
  const address = body.kind === "invite" && isString(payload.address, SERVER_ADDRESS) ? payload.address : null;
  const minecraft = body.kind === "invite" && typeof payload.minecraft === "string" ? payload.minecraft.slice(0, 64) : null;
  const statements = [];
  for (const to of recipients) {
    const { n } = await db.prepare("SELECT COUNT(*) AS n FROM inbox WHERE recipient = ?1 AND expires > ?2").bind(to, now).first();
    need(n < MAX_INBOX, 429, "inbox_full");
    statements.push(db.prepare(
      "INSERT INTO inbox (id, sender, recipient, kind, title, payload, created, expires) VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8)"
    ).bind(randomHex(16), me, to, body.kind, title, body.payload, now, now + V2_INBOX_DAYS * DAY));
    // Einladungen als "invite" (sehen auch alte Launcher), alles andere als "inbox"
    statements.push(body.kind === "invite"
      ? notify(db, to, "invite", me, now, address, minecraft)
      : notify(db, to, "inbox", me, now, null, null, { title, kind: body.kind }));
  }
  await db.batch(statements);
  return json({ ok: true });
}

async function v2InboxGet({ db, me }, id) {
  const row = await db.prepare(
    "SELECT i.id, i.kind, i.title, i.payload, i.created, i.expires, u.uuid, u.name FROM inbox i " +
    "JOIN users u ON u.uuid = i.sender WHERE i.id = ?1 AND i.recipient = ?2 AND i.expires > ?3"
  ).bind(id, me, Date.now()).first();
  need(row, 404, "not_found");
  return json({
    id: row.id, kind: row.kind, title: row.title, payload: row.payload, created: row.created, expires: row.expires,
    sender: { uuid: row.uuid, name: row.name }
  });
}

async function v2InboxDelete({ db, me }, id) {
  await db.prepare("DELETE FROM inbox WHERE id = ?1 AND recipient = ?2").bind(id, me).run();
  return json({ ok: true });
}

/** Eigene Einladung an einen Freund zurückziehen (Postfach-Eintrag und Benachrichtigung). */
async function v2InviteWithdraw({ db, me }, uuid) {
  await db.batch([
    db.prepare("DELETE FROM inbox WHERE sender = ?1 AND recipient = ?2 AND kind = 'invite'").bind(me, uuid),
    db.prepare("DELETE FROM notifications WHERE sender = ?1 AND recipient = ?2 AND kind = 'invite'").bind(me, uuid)
  ]);
  return json({ ok: true });
}

// ---------- Umhänge ----------

function origin(ctx) {
  return ctx.url.origin;
}

async function v2Capes(ctx) {
  const { results } = await ctx.db.prepare("SELECT id, name FROM cape_images WHERE owner IS NULL ORDER BY created").all();
  return json(results.map(r => ({ id: r.id, name: r.name, url: `${origin(ctx)}/v2/capes/${r.id}.png` })), 200,
    { "Cache-Control": "public, max-age=60" });
}

/** kind "official" (id = Bild-ID) oder "custom" (id = ID im Launcher, freigegeben); null legt ab. Antwort: getragene Bild-ID. */
async function v2SetActiveCape({ db, body, me }) {
  if (body.kind == null || body.id == null) {
    await db.prepare("DELETE FROM capes WHERE uuid = ?1").bind(me).run();
    return json({ cape: null });
  }
  let image;
  if (body.kind === "official") {
    need(isString(body.id, CAPE_ID));
    image = await db.prepare("SELECT id FROM cape_images WHERE id = ?1 AND owner IS NULL").bind(body.id).first();
  } else {
    need(body.kind === "custom" && isString(body.id, LOCAL_ID));
    image = await db.prepare("SELECT id FROM cape_images WHERE owner = ?1 AND local_id = ?2").bind(me, body.id).first();
  }
  need(image, 404, "not_found");
  await db.prepare("INSERT INTO capes (uuid, cape) VALUES (?1, ?2) ON CONFLICT(uuid) DO UPDATE SET cape = excluded.cape")
    .bind(me, image.id).run();
  return json({ cape: image.id });
}

/** Bild aus einem Formular prüfen: PNG, 2:1, Breite 64…2048 als Zweierpotenz, höchstens 1,5 MiB. */
async function capeFromForm(form) {
  const file = form?.get("file");
  need(file && typeof file !== "string", 400, "invalid_image");
  const png = new Uint8Array(await file.arrayBuffer());
  need(png.length <= MAX_CAPE_BYTES_V2, 413, "too_large");
  const size = pngSize(png);
  need(size && size.width * 32 === size.height * 64 && size.width >= 64 && size.width <= MAX_CAPE_WIDTH
    && (size.width & (size.width - 1)) === 0, 400, "invalid_image");
  return png;
}

/** Eigenes Cape einreichen; es gibt höchstens eine offene Einreichung je Spieler (eine neue ersetzt die alte). */
async function v2Submit({ db, form, me }) {
  const name = cleanText(form?.get("name"), 40);
  const localId = String(form?.get("localId") ?? "");
  need(name && LOCAL_ID.test(localId));
  const png = await capeFromForm(form);
  const id = randomHex(16);
  await db.batch([
    db.prepare("DELETE FROM cape_submissions WHERE uuid = ?1").bind(me),
    db.prepare("INSERT INTO cape_submissions (id, uuid, local_id, name, png, created) VALUES (?1, ?2, ?3, ?4, ?5, ?6)")
      .bind(id, me, localId, name, png.buffer, Date.now())
  ]);
  return json({ id });
}

async function v2Withdraw({ db, me }) {
  await db.prepare("DELETE FROM cape_submissions WHERE uuid = ?1").bind(me).run();
  return json({ ok: true });
}

/** Bild einer Einreichung (für die Admin-Ansicht; die ID ist zufällig und nicht zu erraten). */
async function v2SubmissionImage({ db }, id) {
  const row = await db.prepare("SELECT png FROM cape_submissions WHERE id = ?1").bind(id).first();
  if (!row)
    return new Response("Nicht gefunden", { status: 404 });
  return new Response(new Uint8Array(row.png), { headers: { "Content-Type": "image/png", "Cache-Control": "private, max-age=60" } });
}

async function v2Submissions(ctx) {
  const { results } = await ctx.db.prepare(
    "SELECT s.id, s.uuid, u.name AS owner_name, s.name, s.created FROM cape_submissions s " +
    "LEFT JOIN users u ON u.uuid = s.uuid ORDER BY s.created"
  ).all();
  return json(results.map(r => ({
    id: r.id, ownerUuid: r.uuid, ownerName: r.owner_name ?? "?", name: r.name,
    url: `${origin(ctx)}/v2/submissions/${r.id}.png`, created: r.created
  })));
}

/** Freigeben: wird ein eigener Umhang des Einreichers (gleiche Launcher-ID ersetzt den alten). Ablehnen: mit Grund. */
async function v2Review({ db, body, me }, id) {
  const sub = await db.prepare("SELECT id, uuid, local_id, name, png FROM cape_submissions WHERE id = ?1").bind(id).first();
  need(sub, 404, "not_found");
  const approve = body.approve === true;
  const reason = approve ? null : cleanText(body.reason, 200) || null;
  const now = Date.now();
  const statements = [db.prepare("DELETE FROM cape_submissions WHERE id = ?1").bind(id)];
  if (approve) {
    const existing = await db.prepare("SELECT id FROM cape_images WHERE owner = ?1 AND local_id = ?2")
      .bind(sub.uuid, sub.local_id).first();
    if (existing)
      statements.push(db.prepare("UPDATE cape_images SET name = ?1, png = ?2, uploader = ?3, created = ?4 WHERE id = ?5")
        .bind(sub.name, sub.png, me, now, existing.id));
    else {
      const { n } = await db.prepare("SELECT COUNT(*) AS n FROM cape_images WHERE owner = ?1").bind(sub.uuid).first();
      need(n < MAX_PERSONAL_CAPES, 429, "rate_limited", "Der Spieler hat schon die Höchstzahl eigener Umhänge.");
      const taken = new Set((await allCapes(db)).map(c => c.id));
      statements.push(db.prepare(
        "INSERT INTO cape_images (id, name, png, uploader, created, owner, local_id) VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7)"
      ).bind(freeId(slug(sub.name), taken), sub.name, sub.png, me, now, sub.uuid, sub.local_id));
    }
  }
  statements.push(notify(db, sub.uuid, "capeReview", null, now, null, null, {
    name: sub.name,
    localId: sub.local_id,
    approved: approve,
    reason,
    // Der Launcher übersetzt Parameter, die selbst ein Textschlüssel sind
    result: approve ? "notifications.capeApproved" : "notifications.capeRejected"
  }));
  await db.batch(statements);
  return json({ ok: true });
}

async function v2UploadOfficial({ db, form, me }) {
  const name = cleanText(form?.get("name"), 40);
  need(name);
  const png = await capeFromForm(form);
  const id = freeId(slug(name), new Set((await allCapes(db)).map(c => c.id)));
  await db.prepare("INSERT INTO cape_images (id, name, png, uploader, created, owner) VALUES (?1, ?2, ?3, ?4, ?5, NULL)")
    .bind(id, name, png.buffer, me, Date.now()).run();
  return json({ id, name });
}

async function v2DeleteOfficial({ db }, id) {
  await db.batch([
    db.prepare("DELETE FROM cape_images WHERE id = ?1").bind(id),
    db.prepare("DELETE FROM capes WHERE cape = ?1").bind(id)
  ]);
  return json({ ok: true });
}

async function v2Admins({ db }) {
  const { results } = await db.prepare("SELECT uuid, name FROM admins ORDER BY name COLLATE NOCASE").all();
  return json(results.map(r => ({ uuid: r.uuid, name: r.name })));
}

async function v2AddAdmin({ env, db, me }, uuid) {
  need(!ownerUuids(env).includes(uuid), 400, "invalid_request", "Der Besitzer ist sowieso Admin.");
  const user = await db.prepare("SELECT name FROM users WHERE uuid = ?1").bind(uuid).first();
  need(user, 404, "unknown_player");
  await db.prepare(
    "INSERT INTO admins (uuid, name, added_by, created) VALUES (?1, ?2, ?3, ?4) " +
    "ON CONFLICT(uuid) DO UPDATE SET name = excluded.name"
  ).bind(uuid, user.name, me, Date.now()).run();
  return json({ ok: true });
}

async function v2RemoveAdmin({ db }, uuid) {
  await db.prepare("DELETE FROM admins WHERE uuid = ?1").bind(uuid).run();
  return json({ ok: true });
}

// ---------- Für die Mod ----------

/** { "players": { "<uuid>": { "cape": "<id>" | null } } } – nur aktive AxoClient-Spieler. */
async function v2Players({ db, body }) {
  const uuids = Array.isArray(body.uuids)
    ? [...new Set(body.uuids.filter(u => isString(u, UUID)))].slice(0, MAX_UUIDS)
    : [];
  const players = {};
  if (uuids.length > 0)
    for (const r of await activePlayers(db, uuids))
      players[r.uuid] = { cape: r.cape ?? null };
  return json({ players });
}

// ---------- Echtzeit-Kanal (Emotes) ----------
// Der Launcher verbindet sich per WebSocket, solange auf einem Server gespielt wird: /v2/live?server=<adresse>.
// Je Server gibt es einen Raum (Durable Object). Was ein Spieler schickt, bekommen alle anderen im Raum.

function roomName(address) {
  const a = address.trim().toLowerCase();
  return a.endsWith(":25565") ? a.slice(0, -6) : a;
}

async function v2Live({ env, request, url, me }) {
  need(request.headers.get("Upgrade")?.toLowerCase() === "websocket", 426, "websocket_required");
  need(env.LIVE, 503, "live_unavailable");
  const server = url.searchParams.get("server") ?? "";
  need(isString(server, SERVER_ADDRESS), 400, "invalid_request");
  const stub = env.LIVE.get(env.LIVE.idFromName(roomName(server)));
  const forwarded = new Request(request);
  forwarded.headers.set("X-AxoClient-Uuid", me);
  return stub.fetch(forwarded);
}

const EMOTE_ID = /^[a-z0-9_]{1,32}$/;
/** Eigene (importierte) Emotes: "c_" und 12 Hex-Zeichen aus dem Hash der Animation. */
const CUSTOM_EMOTE_ID = /^c_[0-9a-f]{12}$/;
const ANIMATION_KEY = /^[\w .:-]{1,64}$/;
const EMOTE_INTERVAL = 800;      // höchstens so oft je Spieler
const CUSTOM_INTERVAL = 3000;    // Emotes mit mitgeschickter Animation seltener
const MAX_ANIM = 200000;         // Zeichen der mitgeschickten Animation (JSON-Text), wie EmoteLibrary.MaxAnimationChars
const MAX_WORLD = 16000;         // Zeichen der Welt-Infos (Version, Loader, Mods)

/**
 * Ein Raum je Server. Nutzt die Hibernation-API: schlafende Räume kosten nichts, Verbindungen bleiben bestehen.
 * Nachrichten vom Launcher:
 *   { "type": "emote", "emote": "wave" } bzw. "emote": null zum Abbrechen; eigene Emotes ("c_…") mit
 *   "anim" (Animation als JSON-Text) und "animation" (Name darin).
 *   { "type": "world", "info": { minecraft, loader, mods: [...] } } vom Gastgeber einer offenen Welt; der Raum merkt
 *   sich das und schickt es jedem, der neu dazukommt (z. B. ein Freund, der vor dem Nachjoinen prüft, was ihm fehlt).
 * An die anderen: { "type": "emote", "uuid": "...", "emote": ..., "anim"?, "animation"?, "at": <ms> } bzw.
 * { "type": "world", "uuid": "...", "info": {...} }.
 * Mit ?peek=1 wird nur zugehört (kein Senden, ersetzt keine laufende Verbindung desselben Spielers).
 */
export class LiveRoom {
  constructor(ctx, env) {
    this.ctx = ctx;
    this.env = env;
    // "ping" beantwortet Cloudflare selbst, ohne den Raum aufzuwecken
    this.ctx.setWebSocketAutoResponse(new WebSocketRequestResponsePair("ping", "pong"));
  }

  async fetch(request) {
    const uuid = request.headers.get("X-AxoClient-Uuid");
    if (!isString(uuid, UUID))
      return new Response("Nicht angemeldet", { status: 401 });
    const peek = new URL(request.url).searchParams.get("peek") === "1";
    const tag = peek ? "peek:" + uuid : uuid;
    // Höchstens eine Verbindung je Spieler und Raum: alte schließen
    for (const old of this.ctx.getWebSockets(tag))
      try { old.close(1000, "replaced"); } catch { /* schon zu */ }
    const [client, server] = Object.values(new WebSocketPair());
    this.ctx.acceptWebSocket(server, [tag]);
    server.serializeAttachment({ uuid, peek, last: 0, lastCustom: 0 });
    // Bekannte offene Welten gleich mitgeben
    const worlds = await this.ctx.storage.list({ prefix: "world:" });
    for (const [, out] of worlds)
      try { server.send(out); } catch { /* Verbindung gerade weg */ }
    return new Response(null, { status: 101, webSocket: client });
  }

  async webSocketMessage(ws, message) {
    if (typeof message !== "string" || message.length > MAX_ANIM + 1024)
      return;
    const state = ws.deserializeAttachment() ?? {};
    if (state.peek)
      return;
    const data = parseJson(message);
    if (data?.type === "world")
      return this.world(ws, state, data, message.length);
    if (data?.type !== "emote")
      return;
    const builtin = data.emote === null || isString(data.emote, EMOTE_ID) && !data.emote.startsWith("c_");
    const custom = isString(data.emote, CUSTOM_EMOTE_ID) && typeof data.anim === "string" && data.anim.length <= MAX_ANIM
      && isString(data.animation, ANIMATION_KEY) && parseJson(data.anim) !== null;
    if (!builtin && !custom)
      return;
    const now = Date.now();
    if (data.emote !== null && now - (state.last ?? 0) < EMOTE_INTERVAL)
      return;
    if (custom && now - (state.lastCustom ?? 0) < CUSTOM_INTERVAL)
      return;
    ws.serializeAttachment({ ...state, last: now, lastCustom: custom ? now : state.lastCustom ?? 0 });
    const out = JSON.stringify(custom
      ? { type: "emote", uuid: state.uuid, emote: data.emote, anim: data.anim, animation: data.animation, at: now }
      : { type: "emote", uuid: state.uuid, emote: data.emote, at: now });
    for (const other of this.ctx.getWebSockets())
      if (other !== ws)
        try { other.send(out); } catch { /* Verbindung gerade weg */ }
  }

  /** Welt-Infos des Gastgebers merken (null = Welt geschlossen) und an alle anderen weitergeben. */
  async world(ws, state, data, length) {
    const key = "world:" + state.uuid;
    if (data.info === null) {
      await this.ctx.storage.delete(key);
      return;
    }
    const info = data.info;
    if (length > MAX_WORLD || typeof info !== "object" || !Array.isArray(info.mods) || info.mods.length > 300)
      return;
    const out = JSON.stringify({
      type: "world", uuid: state.uuid,
      info: { minecraft: cleanText(info.minecraft, 32), loader: cleanText(info.loader, 16), mods: info.mods.slice(0, 300) }
    });
    await this.ctx.storage.put(key, out);
    for (const other of this.ctx.getWebSockets())
      if (other !== ws)
        try { other.send(out); } catch { /* Verbindung gerade weg */ }
  }

  async webSocketClose(ws, code) {
    const state = ws.deserializeAttachment() ?? {};
    if (!state.peek && state.uuid)
      await this.ctx.storage.delete("world:" + state.uuid);
    try { ws.close(code === 1005 ? 1000 : code, "bye"); } catch { /* schon zu */ }
  }

  async webSocketError(ws) {
    try { ws.close(1011, "error"); } catch { /* schon zu */ }
  }
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
