// ValTrainer bug-report relay: Cloudflare Worker (ES modules, zero dependencies).
//
// Receives in-app bug reports (POST /report) from the ValTrainer desktop client,
// validates and sanitises them, and posts them as a new thread in a Discord forum
// channel via a webhook. The webhook URL lives ONLY in the DISCORD_WEBHOOK_URL
// secret; it is never returned to clients or written to logs.
//
// Bindings / variables (all set in the Cloudflare dashboard):
//   DISCORD_WEBHOOK_URL  secret, required
//   RATE                 KV namespace, optional (falls back to per-isolate memory)
//   MAX_PER_HOUR         plain var, optional, reports per IP per rolling hour (default 5)
//   BUG_TAG_ID           plain var, optional, forum tag id(s) applied to each new post (comma-separated, max 5)

const MAX_BODY = 32 * 1024;          // bytes
const HOUR_MS = 3600 * 1000;
const GLOBAL_PER_DAY = 200;
const EMBED_COLOR = 16729685;
const ZWSP = '​';
const APP_FIELDS = ['install', 'os', 'cpu', 'ram_gb', 'gpu', 'gpu_type', 'renderer', 'driver',
  'display', 'window_mode', 'fps_cap', 'graphics'];
const CTX_FIELDS = ['screen', 'mode', 'uptime_s'];

class BadRequest extends Error {}

// ---------- responses ----------
const json = (status, obj, headers = {}) => new Response(JSON.stringify(obj), {
  status, headers: { 'content-type': 'application/json; charset=utf-8', ...headers } });
const text = (status, body) => new Response(body, {
  status, headers: { 'content-type': 'text/plain; charset=utf-8' } });
const bad = (error) => json(400, { ok: false, error });
const tooLarge = () => json(413, { ok: false, error: 'too large' });

// ---------- text hygiene ----------
// Normalise newlines and drop C0/C1 control chars (keeping \n and \t) plus bidi overrides.
const stripControl = (s) => s.replace(/\r\n?/g, '\n')
  .replace(/[\u0000-\u0008\u000B-\u001F\u007F-\u009F‪-‮⁦-⁩]/g, '');

// Text Discord will render: defuse every mention (@everyone, @here, <@id>, <@&id>) with a
// zero-width space after "@", break masked links ([text](url)) so a link always shows its real
// address, wrap bare URLs in <> so they get no preview, and collapse floods of more than 3 blank
// lines down to 3.
const clean = (s) => stripControl(s).replace(/@/g, '@' + ZWSP)
  .replace(/\]\(/g, ']' + ZWSP + '(')
  .replace(/<?(https?:\/\/[^\s<>()]+)>?/gi, '<$1>')
  .replace(/\n(?:[ \t]*\n){4,}/g, '\n\n\n\n').trim();

// Truncate to max chars with an ellipsis, never splitting a surrogate pair.
function trunc(s, max) {
  if (s.length <= max) return s;
  let cut = s.slice(0, Math.max(0, max - 1));
  if (/[\uD800-\uDBFF]$/.test(cut)) cut = cut.slice(0, -1);
  return cut.trimEnd() + '…';
}

// ---------- validation ----------
// Read one scalar field as a string. min > 0 makes it required. Numbers/booleans are
// accepted and stringified; anything else (objects, arrays) is rejected.
function field(obj, key, max, label, min = 0) {
  let v = obj ? obj[key] : undefined;
  if (typeof v === 'number' && Number.isFinite(v)) v = String(v);
  else if (typeof v === 'boolean') v = String(v);
  if (v === undefined || v === null || v === '') {
    if (min > 0) throw new BadRequest(`missing ${label}`);
    return '';
  }
  if (typeof v !== 'string') throw new BadRequest(`${label} must be a string`);
  if (v.length > max) throw new BadRequest(`${label} too long`);
  if (v.trim().length < min) throw new BadRequest(`${label} too short`);
  return v;
}

function validate(b) {
  if (!b || typeof b !== 'object' || Array.isArray(b)) throw new BadRequest('body must be an object');
  if (b.schema !== 1) throw new BadRequest('unsupported schema');
  for (const k of ['app', 'context', 'settings', 'report']) {
    if (b[k] != null && (typeof b[k] !== 'object' || Array.isArray(b[k]))) throw new BadRequest(`${k} must be an object`);
  }
  const app = { version: field(b.app, 'version', 32, 'app.version', 1) };
  for (const k of APP_FIELDS) app[k] = field(b.app, k, 120, `app.${k}`);
  const ctx = {};
  for (const k of CTX_FIELDS) ctx[k] = field(b.context, k, 120, `context.${k}`);
  const rep = {
    title: field(b.report, 'title', 100, 'report.title', 3),
    what: field(b.report, 'what', 1500, 'report.what', 10),
    steps: field(b.report, 'steps', 1500, 'report.steps'),
    expected: field(b.report, 'expected', 1500, 'report.expected'),
    contact: field(b.report, 'contact', 64, 'report.contact'),
  };
  const keys = Object.keys(b.settings || {});
  if (keys.length > 30) throw new BadRequest('too many settings');
  const settings = keys.map((k) => {
    if (k.length > 40) throw new BadRequest('settings key too long');
    return [k, field(b.settings, k, 120, 'settings value')];
  });
  const log = b.log_tail == null ? '' : b.log_tail;
  if (typeof log !== 'string') throw new BadRequest('log_tail must be a string');
  if (log.length > 16000) throw new BadRequest('log_tail too long');
  return { app, ctx, rep, settings, log };
}

// ---------- rate limiting ----------
// Per-isolate fallback with the same get/put shape as a KV namespace.
const memMap = new Map();
const memKV = {
  async get(k) { const e = memMap.get(k); return e && e.exp > Date.now() ? e.v : null; },
  async put(k, v, o = {}) { memMap.set(k, { v, exp: Date.now() + (o.expirationTtl || 86400) * 1000 }); },
};

// Rate-limit keys use a truncated SHA-256 of the IP, so raw addresses are never stored.
async function hashIp(ip) {
  const d = await crypto.subtle.digest('SHA-256', new TextEncoder().encode('valtrainer-relay:' + ip));
  return [...new Uint8Array(d).slice(0, 12)].map((x) => x.toString(16).padStart(2, '0')).join('');
}

// Returns 0 if allowed (and records the hit), otherwise seconds until a retry can succeed.
// KV is eventually consistent, so limits are approximate across edge locations: fine here.
async function rateLimit(store, ip, perHour) {
  const now = Date.now();
  const day = new Date(now).toISOString().slice(0, 10);
  const dayKey = `day:${day}`, ipKey = `ip:${ip}`;
  const dayCount = Number(await store.get(dayKey)) || 0;
  if (dayCount >= GLOBAL_PER_DAY) {
    return Math.max(1, Math.ceil((Date.parse(`${day}T00:00:00Z`) + 24 * HOUR_MS - now) / 1000));
  }
  let hits = [];
  try { hits = JSON.parse((await store.get(ipKey)) || '[]'); } catch { /* corrupt entry: reset */ }
  hits = (Array.isArray(hits) ? hits : []).filter((t) => typeof t === 'number' && t > now - HOUR_MS);
  if (hits.length >= perHour) return Math.max(1, Math.ceil((hits[0] + HOUR_MS - now) / 1000));
  hits.push(now);
  await Promise.all([
    store.put(ipKey, JSON.stringify(hits), { expirationTtl: 3600 }),
    store.put(dayKey, String(dayCount + 1), { expirationTtl: 2 * 86400 }),
  ]);
  return 0;
}

async function limit(kv, ip, perHour) {
  if (kv) {
    try { return await rateLimit(kv, ip, perHour); }
    catch (e) { console.log(`KV rate limit failed, using memory: ${e && e.message}`); }
  }
  return rateLimit(memKV, ip, perHour);
}

// ---------- Discord message ----------
function buildPayload(r, env) {
  const { app, ctx, rep } = r;
  const join = (...parts) => parts.filter(Boolean).join(' · ');
  const settingsText = r.settings.map(([k, v]) => `${k}=${v}`).join('\n');
  const fields = [
    ['Steps', rep.steps], ['Expected', rep.expected], ['Contact', rep.contact, true],
    ['App', join(`v${app.version}`, app.install), true],
    ['Context', join(ctx.screen, ctx.mode, ctx.uptime_s && `up ${ctx.uptime_s}s`), true],
    ['System', join(app.os, app.cpu, app.ram_gb && `${app.ram_gb} GB RAM`)],
    ['GPU', join(app.gpu, app.gpu_type, app.renderer, app.driver)],
    ['Display', join(app.display, app.window_mode, app.fps_cap && `cap ${app.fps_cap}`, app.graphics)],
    ['Settings', settingsText],   // last, so it is the one squeezed by the 6000-char budget
  ];

  const title = trunc(clean(rep.title).replace(/\s+/g, ' '), 256);
  let budget = 6000 - title.length - 32;     // Discord total-embed limit, with headroom
  let settingsCut = false;
  const embedFields = [];
  for (const [name, raw, inline = false] of fields) {
    const value = clean(raw);
    if (!value) continue;                     // Discord rejects empty field values
    const max = Math.min(1024, budget - name.length);
    const v = max >= 16 ? trunc(value, max) : '';
    if (name === 'Settings' && v !== value) settingsCut = true;
    if (!v) continue;
    embedFields.push({ name, value: v, inline });
    budget -= name.length + v.length;
  }

  let log = stripControl(r.log).trim();
  if (settingsCut) {
    log = `--- settings (full) ---\n${stripControl(settingsText)}\n` + (log ? `\n--- log tail ---\n${log}` : '');
  }

  const payload = {
    thread_name: trunc(clean(`v${app.version}: ${rep.title}`).replace(/\s+/g, ' '), 100),
    username: 'ValTrainer Bug Reports',
    content: trunc(clean(rep.what), 2000),
    allowed_mentions: { parse: [] },
    embeds: [{ title, color: EMBED_COLOR, fields: embedFields, timestamp: new Date().toISOString() }],
  };
  // One or more forum tag ids (comma-separated, max 5): e.g. the "Open" status tag plus a category.
  const tagIds = String(env.BUG_TAG_ID || '').split(',').map(s => s.trim()).filter(s => /^\d{5,25}$/.test(s));
  if (tagIds.length) payload.applied_tags = [...new Set(tagIds)].slice(0, 5);
  if (log) payload.attachments = [{ id: 0, filename: 'log.txt' }];
  return { payload, log };
}

// Posts as multipart/form-data with ?wait=true. Returns the thread/message id, or null on failure.
async function postToDiscord(webhook, payload, log) {
  try {
    const url = new URL(webhook);
    url.searchParams.set('wait', 'true');
    const form = new FormData();
    form.append('payload_json', JSON.stringify(payload));
    if (log) form.append('files[0]', new Blob([log], { type: 'text/plain; charset=utf-8' }), 'log.txt');
    const res = await fetch(url.toString(), { method: 'POST', body: form });
    if (!res.ok) {
      const detail = (await res.text().catch(() => '')).slice(0, 200);
      console.log(`discord rejected report: HTTP ${res.status} ${detail}`);  // owner logs only
      return null;
    }
    const msg = await res.json().catch(() => ({}));
    return String(msg.channel_id || msg.id || '');   // forum post: channel_id is the new thread
  } catch (e) {
    console.log(`discord request failed: ${e && e.name}`);
    return null;
  }
}

// ---------- request handling ----------
async function handleReport(request, env) {
  const client = request.headers.get('x-valtrainer-client') || '';
  if (!client.startsWith('ValTrainer/') || client.length > 64) return bad('missing or invalid client header');
  const type = (request.headers.get('content-type') || '').toLowerCase();
  if (!type.startsWith('application/json')) return bad('content-type must be application/json');
  if (Number(request.headers.get('content-length') || 0) > MAX_BODY) return tooLarge();
  const buf = await request.arrayBuffer();
  if (buf.byteLength > MAX_BODY) return tooLarge();

  let report;
  try {
    report = validate(JSON.parse(new TextDecoder('utf-8', { fatal: true }).decode(buf)));
  } catch (e) {
    if (e instanceof BadRequest) return bad(e.message);
    if (e instanceof SyntaxError || e instanceof TypeError) return bad('invalid JSON');
    throw e;
  }

  if (!env.DISCORD_WEBHOOK_URL) {
    console.log('DISCORD_WEBHOOK_URL secret is not set');
    return json(500, { ok: false, error: 'relay not configured' });
  }

  const perHour = Math.min(100, Math.max(1, parseInt(env.MAX_PER_HOUR, 10) || 5));
  const ip = await hashIp(request.headers.get('cf-connecting-ip') || 'unknown');
  const wait = await limit(env.RATE, ip, perHour);
  if (wait) return json(429, { ok: false, error: 'rate limited', retry_after_s: wait }, { 'retry-after': String(wait) });

  const { payload, log } = buildPayload(report, env);
  const id = await postToDiscord(env.DISCORD_WEBHOOK_URL, payload, log);
  if (id === null) return json(502, { ok: false, error: 'discord failed' });
  return json(200, { ok: true, id });
}

export default {
  async fetch(request, env = {}) {
    const { pathname } = new URL(request.url);
    const m = request.method;
    if (m === 'OPTIONS' || (pathname === '/' && (m === 'GET' || m === 'HEAD'))) {
      return text(200, 'ValTrainer bug relay: OK. POST /report\n');
    }
    if (pathname === '/') return json(405, { ok: false, error: 'method not allowed' }, { allow: 'GET, OPTIONS' });
    if (pathname !== '/report') return json(404, { ok: false, error: 'not found' });
    if (m !== 'POST') return json(405, { ok: false, error: 'method not allowed' }, { allow: 'POST, OPTIONS' });
    try {
      return await handleReport(request, env);
    } catch (e) {
      console.log(`unexpected error: ${e && e.message}`);
      return json(500, { ok: false, error: 'internal error' });
    }
  },
};
