// AxoClient Badge-Dienst (Cloudflare Worker + D1)
//
// POST /register  -> Spieler als Launcher-Nutzer eintragen. Nachweis über das Spieler-Zertifikat von Mojang
//                    (dasselbe, das Minecraft für signierte Chatnachrichten nutzt):
//                    1. Mojang hat den öffentlichen Schlüssel des Spielers für genau diese UUID unterschrieben.
//                    2. Der Launcher hat mit dem passenden privaten Schlüssel eine Nachricht mit Zeitstempel signiert.
//                    Der Dienst braucht dafür keine Verbindung zu Mojang (Cloudflare wird dort blockiert),
//                    und das Minecraft-Token kommt nie hier an.
// POST /check     { uuids: [...] } -> welche dieser Spieler nutzen den Launcher?
// GET  /                           -> Statusanzeige (zum Testen im Browser)

const ACTIVE_DAYS = 30;               // wer so lange nicht gespielt hat, verliert das Symbol
const MAX_UUIDS = 90;                 // D1 erlaubt höchstens 100 Parameter pro Abfrage
const MAX_CLOCK_SKEW = 5 * 60 * 1000; // so weit darf die Uhr des Spielers abweichen
const TOKEN_DAYS = 60;                // so lange gilt ein Anmelde-Token des Launchers
const ONLINE_MILLIS = 150 * 1000;     // der Launcher meldet sich jede Minute, danach gilt man als offline
const MAX_FRIENDS = 200;

// Mojangs öffentliche Schlüssel für Spieler-Zertifikate (https://api.minecraftservices.com/publickeys,
// "playerCertificateKeys"). Ändert Mojang sie irgendwann, hier aktualisieren.
const MOJANG_KEYS = [
  "MIICIjANBgkqhkiG9w0BAQEFAAOCAg8AMIICCgKCAgEAylB4B6m5lz7jwrcFz6Fd/fnfUhcvlxsTSn5kIK/2aGG1C3kMy4VjhwlxF6BFUSnfxhNswPjh3ZitkBxEAFY25uzkJFRwHwVA9mdwjashXILtR6OqdLXXFVyUPIURLOSWqGNBtb08EN5fMnG8iFLgEJIBMxs9BvF3s3/FhuHyPKiVTZmXY0WY4ZyYqvoKR+XjaTRPPvBsDa4WI2u1zxXMeHlodT3lnCzVvyOYBLXL6CJgByuOxccJ8hnXfF9yY4F0aeL080Jz/3+EBNG8RO4ByhtBf4Ny8NQ6stWsjfeUIvH7bU/4zCYcYOq4WrInXHqS8qruDmIl7P5XXGcabuzQstPf/h2CRAUpP/PlHXcMlvewjmGU6MfDK+lifScNYwjPxRo4nKTGFZf/0aqHCh/EAsQyLKrOIYRE0lDG3bzBh8ogIMLAugsAfBb6M3mqCqKaTMAf/VAjh5FFJnjS+7bE+bZEV0qwax1CEoPPJL1fIQjOS8zj086gjpGRCtSy9+bTPTfTR/SJ+VUB5G2IeCItkNHpJX2ygojFZ9n5Fnj7R9ZnOM+L8nyIjPu3aePvtcrXlyLhH/hvOfIOjPxOlqW+O5QwSFP4OEcyLAUgDdUgyW36Z5mB285uKW/ighzZsOTevVUG2QwDItObIV6i8RCxFbN2oDHyPaO5j1tTaBNyVt8CAwEAAQ==",
  "MIICIjANBgkqhkiG9w0BAQEFAAOCAg8AMIICCgKCAgEAt4t9NPuu7cktclnaH7eZj0omkLcJHeLz5MKsyJEntHZ0INtuBjSSul3Pp3pBeJN8k3ADdcdBLUN90bcAi7WsQqTx3Ft363q3W7TbM8j2iTEdp/0uVspoRt/DP1tkaWFs/w2WwUv9jbVoBUzfUc4pSTIxRwdjmqjZQfvjwKNDbOx3IhP2H0WXodbISejPi1wBZqNW4m1rnZAXp/EpUguxA8mobCa4vUCBkyFDyXdl69/wUSJHyCPmgcMJ364OlAhIqtwVPShBZObvrK/f0BYk6ShJD3N7TFDatSYsIIdcTKRknaIm91s+EsMrdB9U4Yw+ZJ/pyCB4S3vk8zfDCnb0DWIxYH3/EMzaxl77djmTmMzi/JDITup5z3jfWtRZmrAhU2/+W5IO5hEpo3/bCS9PXIY5xb41Lmp2ZO8dXKtyD66Chchy0W129n8vPl2GIruOdrxsjZAHnneyAb9jm0uaGaphwnEnuecX/qgHY6ZMtayvLLsPst8PO6R1vufMy8WqjK+j7LnC1krL7CPDg0NEhyQTmw5l+NCNjSlvB1juM9V4PARg0bYCOkGXm7ydRCjSSH8CJXZpwnd5cBB5WKAX3KPzutRgMi/LFwNSMZzFuUyXaYOZPpD259yqph1LmGqegEdDriACVU+dVEONFMm8eIuBofe7ljmsAFKW9BINwK0CAwEAAQ=="
];

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    try {
      if (request.method === "GET" && url.pathname === "/")
        return json({ ok: true, service: "mclauncher-badge" });
      if (request.method === "POST" && url.pathname === "/register")
        return await register(request, env);
      if (request.method === "POST" && url.pathname === "/check")
        return await check(request, env);
      if (request.method === "POST" && url.pathname === "/status")
        return await status(request, env);
      if (request.method === "POST" && url.pathname === "/friends")
        return await friends(request, env);
      if (request.method === "POST" && url.pathname === "/friends/add")
        return await addFriend(request, env);
      if (request.method === "POST" && url.pathname === "/friends/remove")
        return await removeFriend(request, env);
      if (request.method === "POST" && url.pathname === "/cape")
        return await cape(request, env);
      return json({ error: "Nicht gefunden" }, 404);
    } catch (e) {
      return json({ error: "Interner Fehler: " + e.message }, 500);
    }
  }
};

async function register(request, env) {
  const b = await request.json().catch(() => null);
  const valid = b
    && typeof b.uuid === "string" && /^[0-9a-f]{32}$/.test(b.uuid)
    && typeof b.name === "string" && /^[A-Za-z0-9_]{1,16}$/.test(b.name)
    && typeof b.publicKey === "string" && typeof b.keySignature === "string" && typeof b.signature === "string"
    && Number.isSafeInteger(b.expiresAt) && Number.isSafeInteger(b.timestamp);
  if (!valid)
    return json({ error: "Ungültige Anfrage" }, 400);

  const now = Date.now();
  if (Math.abs(now - b.timestamp) > MAX_CLOCK_SKEW)
    return json({ error: "Zeitstempel ungültig – stimmt die Uhr des PCs?" }, 403);
  if (b.expiresAt < now)
    return json({ error: "Spieler-Zertifikat ist abgelaufen" }, 403);

  const publicKey = base64ToBytes(b.publicKey);

  // 1. Hat Mojang diesen Schlüssel für diese UUID ausgestellt? (UUID + Ablaufzeit + Schlüssel, SHA1withRSA)
  const payload = new Uint8Array(24 + publicKey.length);
  payload.set(hexToBytes(b.uuid), 0);
  new DataView(payload.buffer).setBigUint64(16, BigInt(b.expiresAt));
  payload.set(publicKey, 24);
  if (!await verifyWithAnyMojangKey(payload, base64ToBytes(b.keySignature)))
    return json({ error: "Spieler-Zertifikat wurde nicht von Mojang ausgestellt" }, 403);

  // 2. Besitzt der Absender den passenden privaten Schlüssel? (SHA256withRSA über die Nachricht)
  const playerKey = await crypto.subtle.importKey("spki", publicKey,
    { name: "RSASSA-PKCS1-v1_5", hash: "SHA-256" }, false, ["verify"]);
  const message = new TextEncoder().encode(`mclauncher-badge:register:${b.uuid}:${b.timestamp}`);
  if (!await crypto.subtle.verify("RSASSA-PKCS1-v1_5", playerKey, base64ToBytes(b.signature), message))
    return json({ error: "Signatur ungültig" }, 403);

  await env.DB.prepare(
    "INSERT INTO users (uuid, name, last_seen) VALUES (?1, ?2, ?3) " +
    "ON CONFLICT(uuid) DO UPDATE SET name = excluded.name, last_seen = excluded.last_seen"
  ).bind(b.uuid, b.name, now).run();

  // Token für Freunde und Status (gespeichert wird nur sein Hash); alte Tokens aufräumen
  const token = [...crypto.getRandomValues(new Uint8Array(32))].map(x => x.toString(16).padStart(2, "0")).join("");
  await env.DB.batch([
    env.DB.prepare("INSERT INTO tokens (hash, uuid, created) VALUES (?1, ?2, ?3)").bind(await sha256(token), b.uuid, now),
    env.DB.prepare("DELETE FROM tokens WHERE created < ?1").bind(now - TOKEN_DAYS * 86400000)
  ]);

  return json({ ok: true, uuid: b.uuid, name: b.name, token });
}

async function verifyWithAnyMojangKey(data, signature) {
  for (const key of MOJANG_KEYS) {
    const cryptoKey = await crypto.subtle.importKey("spki", base64ToBytes(key),
      { name: "RSASSA-PKCS1-v1_5", hash: "SHA-1" }, false, ["verify"]);
    if (await crypto.subtle.verify("RSASSA-PKCS1-v1_5", cryptoKey, signature, data))
      return true;
  }
  return false;
}

async function check(request, env) {
  const body = await request.json().catch(() => null);
  const uuids = Array.isArray(body?.uuids)
    ? [...new Set(body.uuids.filter(u => typeof u === "string" && /^[0-9a-f]{32}$/.test(u)))].slice(0, MAX_UUIDS)
    : [];
  if (uuids.length === 0)
    return json({ users: [], capes: {} });

  const since = Date.now() - ACTIVE_DAYS * 24 * 60 * 60 * 1000;
  const placeholders = uuids.map((_, i) => "?" + (i + 2)).join(",");
  const { results } = await env.DB.prepare(
    `SELECT u.uuid, c.cape FROM users u LEFT JOIN capes c ON c.uuid = u.uuid ` +
    `WHERE u.last_seen > ?1 AND u.uuid IN (${placeholders})`
  ).bind(since, ...uuids).all();

  // AxoClient-Umhänge der gefundenen Spieler (uuid -> Umhang-ID)
  const capes = {};
  for (const r of results)
    if (r.cape)
      capes[r.uuid] = r.cape;
  return json({ users: results.map(r => r.uuid), capes });
}

function base64ToBytes(text) {
  return Uint8Array.from(atob(text), c => c.charCodeAt(0));
}

function hexToBytes(hex) {
  return Uint8Array.from(hex.match(/../g), h => parseInt(h, 16));
}

function json(data, status = 200) {
  return new Response(JSON.stringify(data), {
    status,
    headers: { "Content-Type": "application/json; charset=utf-8" }
  });
}

// ---------- Freunde & Status ----------
// Alle Anfragen tragen das Token aus /register. Server sehen nur Freunde, die sich gegenseitig hinzugefügt haben.

/** Liest die Anfrage und liefert { body, me } oder eine Fehlerantwort. */
async function authenticate(request, env) {
  const body = await request.json().catch(() => null);
  if (typeof body?.token !== "string" || !/^[0-9a-f]{64}$/.test(body.token))
    return { error: json({ error: "Nicht angemeldet" }, 401) };
  const row = await env.DB.prepare("SELECT uuid FROM tokens WHERE hash = ?1 AND created > ?2")
    .bind(await sha256(body.token), Date.now() - TOKEN_DAYS * 86400000).first();
  return row ? { body, me: row.uuid } : { error: json({ error: "Nicht angemeldet" }, 401) };
}

async function status(request, env) {
  const { body, me, error } = await authenticate(request, env);
  if (error)
    return error;
  const server = typeof body.server === "string" && /^[A-Za-z0-9.\-_:\[\]]{1,255}$/.test(body.server) ? body.server : null;
  const version = typeof body.version === "string" ? body.version.slice(0, 64) : null;
  if (body.playing === false)
    await env.DB.prepare("DELETE FROM status WHERE uuid = ?1").bind(me).run();
  else
    await env.DB.prepare(
      "INSERT INTO status (uuid, server, version, updated) VALUES (?1, ?2, ?3, ?4) " +
      "ON CONFLICT(uuid) DO UPDATE SET server = excluded.server, version = excluded.version, updated = excluded.updated"
    ).bind(me, server, version, Date.now()).run();
  return json({ ok: true });
}

async function friends(request, env) {
  const { me, error } = await authenticate(request, env);
  if (error)
    return error;
  const { results } = await env.DB.prepare(
    "SELECT u.uuid, u.name, s.server, s.version, s.updated, " +
    "EXISTS (SELECT 1 FROM friends f WHERE f.owner = ?1 AND f.friend = u.uuid) AS outgoing, " +
    "EXISTS (SELECT 1 FROM friends f WHERE f.owner = u.uuid AND f.friend = ?1) AS incoming " +
    "FROM users u LEFT JOIN status s ON s.uuid = u.uuid " +
    "WHERE u.uuid IN (SELECT friend FROM friends WHERE owner = ?1 UNION SELECT owner FROM friends WHERE friend = ?1) " +
    "ORDER BY u.name COLLATE NOCASE"
  ).bind(me).all();

  const onlineSince = Date.now() - ONLINE_MILLIS;
  return json({
    friends: results.map(r => {
      const mutual = !!r.outgoing && !!r.incoming;
      const playing = mutual && r.updated > onlineSince;
      return {
        uuid: r.uuid,
        name: r.name,
        state: mutual ? "friend" : r.outgoing ? "outgoing" : "incoming",
        playing,
        server: playing ? r.server : null,
        version: playing ? r.version : null
      };
    })
  });
}

async function addFriend(request, env) {
  const { body, me, error } = await authenticate(request, env);
  if (error)
    return error;
  const name = typeof body.name === "string" ? body.name.trim() : "";
  if (!/^[A-Za-z0-9_]{1,16}$/.test(name))
    return json({ error: "Ungültiger Spielername" }, 400);
  const user = await env.DB.prepare("SELECT uuid, name FROM users WHERE name = ?1 COLLATE NOCASE").bind(name).first();
  if (!user)
    return json({ error: `${name} nutzt AxoClient nicht oder hat damit noch nie gespielt.` }, 404);
  if (user.uuid === me)
    return json({ error: "Du kannst dich nicht selbst hinzufügen." }, 400);
  const count = await env.DB.prepare("SELECT COUNT(*) AS n FROM friends WHERE owner = ?1").bind(me).first();
  if (count.n >= MAX_FRIENDS)
    return json({ error: "Zu viele Freunde" }, 400);
  await env.DB.prepare("INSERT OR IGNORE INTO friends (owner, friend, created) VALUES (?1, ?2, ?3)")
    .bind(me, user.uuid, Date.now()).run();
  return json({ ok: true, name: user.name });
}

/** Entfernt eine Freundschaft bzw. lehnt eine Anfrage ab (beide Richtungen). */
async function removeFriend(request, env) {
  const { body, me, error } = await authenticate(request, env);
  if (error)
    return error;
  if (typeof body.uuid !== "string" || !/^[0-9a-f]{32}$/.test(body.uuid))
    return json({ error: "Ungültige Anfrage" }, 400);
  await env.DB.prepare("DELETE FROM friends WHERE (owner = ?1 AND friend = ?2) OR (owner = ?2 AND friend = ?1)")
    .bind(me, body.uuid).run();
  return json({ ok: true });
}

async function sha256(text) {
  const hash = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(text));
  return [...new Uint8Array(hash)].map(x => x.toString(16).padStart(2, "0")).join("");
}

// ---------- AxoClient-Umhänge ----------
// Die Bilder liegen im GitHub-Repository (capes/); der Dienst merkt sich nur, wer welchen Umhang trägt.

/** Ohne "cape" im Body: aktuellen Umhang liefern. Mit "cape" (ID oder null): setzen bzw. ablegen. */
async function cape(request, env) {
  const { body, me, error } = await authenticate(request, env);
  if (error)
    return error;
  if (!("cape" in body)) {
    const row = await env.DB.prepare("SELECT cape FROM capes WHERE uuid = ?1").bind(me).first();
    return json({ cape: row ? row.cape : null });
  }
  if (body.cape === null) {
    await env.DB.prepare("DELETE FROM capes WHERE uuid = ?1").bind(me).run();
    return json({ ok: true, cape: null });
  }
  if (typeof body.cape !== "string" || !/^[a-z0-9_-]{1,32}$/.test(body.cape))
    return json({ error: "Ungültiger Umhang" }, 400);
  await env.DB.prepare(
    "INSERT INTO capes (uuid, cape) VALUES (?1, ?2) ON CONFLICT(uuid) DO UPDATE SET cape = excluded.cape"
  ).bind(me, body.cape).run();
  return json({ ok: true, cape: body.cape });
}
