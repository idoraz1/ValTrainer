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
//
// Feature suggestions (POST /suggest, from the app's "Suggest a feature" form) go to the #suggestions forum:
//   DISCORD_SUGGEST_WEBHOOK_URL  secret, required for /suggest (a webhook of the #suggestions forum)
//   SUGGEST_TAG_ID               plain var, optional, tag id(s) for every suggestion (e.g. "Open")
//   SUGGEST_CATEGORY_TAGS        plain var, optional, "drill:<id>,agent:<id>,map:<id>,coach:<id>,ui:<id>"
//                                (the category the player picked -> that forum tag)
//
// Moderator commands /close and /reopen (POST /interactions, see "moderator commands" below):
//   DISCORD_PUBLIC_KEY           plain var, the Discord application's public key (checks Discord's signature)
//   DISCORD_BOT_TOKEN            secret, the bug-tracker bot's token (reads, re-tags and closes the post)

const MAX_BODY = 32 * 1024;          // bytes
const HOUR_MS = 3600 * 1000;
const GLOBAL_PER_DAY = 200;
const EMBED_COLOR = 16729685;
const ZWSP = '\u200B';
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
  .replace(/[\u0000-\u0008\u000B-\u001F\u007F-\u009F\u202A-\u202E\u2066-\u2069]/g, '');

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
  return cut.trimEnd() + '\u2026';
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

// ---------- feature suggestions (POST /suggest -> #suggestions) ----------
const IDEA_CATEGORIES = ['drill', 'agent', 'map', 'coach', 'ui', 'other'];

function validateIdea(b) {
  if (!b || typeof b !== 'object' || Array.isArray(b)) throw new BadRequest('body must be an object');
  if (b.schema !== 1) throw new BadRequest('unsupported schema');
  for (const k of ['app', 'idea']) {
    if (b[k] != null && (typeof b[k] !== 'object' || Array.isArray(b[k]))) throw new BadRequest(`${k} must be an object`);
  }
  const app = { version: field(b.app, 'version', 32, 'app.version', 1), install: field(b.app, 'install', 120, 'app.install') };
  const idea = {
    title: field(b.idea, 'title', 100, 'idea.title', 3),
    what: field(b.idea, 'what', 1500, 'idea.what', 10),
    why: field(b.idea, 'why', 1500, 'idea.why'),
    category: field(b.idea, 'category', 16, 'idea.category'),
    contact: field(b.idea, 'contact', 64, 'idea.contact'),
  };
  if (idea.category && !IDEA_CATEGORIES.includes(idea.category)) throw new BadRequest('unknown idea.category');
  return { app, idea };
}

// "drill:123,agent:456" -> the forum tag id for a category (optional; unknown -> none).
function categoryTag(env, category) {
  for (const pair of String(env.SUGGEST_CATEGORY_TAGS || '').split(',')) {
    const [k, v] = pair.split(':').map((s) => (s || '').trim());
    if (k === category && /^\d{5,25}$/.test(v)) return v;
  }
  return null;
}

function buildIdeaPayload(r, env) {
  const { app, idea } = r;
  const label = { drill: 'New drill', agent: 'Agent', map: 'Map', coach: 'Coach', ui: 'UI / settings', other: 'Other' }[idea.category] || '';
  const fields = [['Why', idea.why], ['Category', label, true], ['Contact', idea.contact, true],
    ['App', [`v${app.version}`, app.install].filter(Boolean).join(' \u00B7 '), true]];
  const embedFields = [];
  for (const [name, raw, inline = false] of fields) {
    const value = trunc(clean(raw), 1024);
    if (value) embedFields.push({ name, value, inline });
  }
  const payload = {
    thread_name: trunc(clean(idea.title).replace(/\s+/g, ' '), 100),
    username: 'ValTrainer Suggestions',
    content: trunc(clean(idea.what), 2000),
    allowed_mentions: { parse: [] },
    embeds: [{ title: trunc(clean(idea.title).replace(/\s+/g, ' '), 256), color: EMBED_COLOR, fields: embedFields, timestamp: new Date().toISOString() }],
  };
  const tagIds = String(env.SUGGEST_TAG_ID || '').split(',').map((s) => s.trim()).filter((s) => /^\d{5,25}$/.test(s));
  const cat = categoryTag(env, idea.category);
  if (cat) tagIds.push(cat);
  if (tagIds.length) payload.applied_tags = [...new Set(tagIds)].slice(0, 5);
  return payload;
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
  const join = (...parts) => parts.filter(Boolean).join(' \u00B7 ');
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

// ---------- moderator commands (POST /interactions: the Discord app's "Interactions Endpoint URL") ----------
// /close status:<Fixed | Won't fix | Can't reproduce | Added | Not planned | Duplicate | Custom> [reason] and
// /reopen [reason], used inside a
// #bug-reports or #suggestions post: the same as the release bot does (a reply, the status tag swapped, the post
// closed). Only members with Manage Posts (= Manage Threads) or Administrator see them (commands.json registers them
// with that default permission) and the Worker checks it again. Registered by .github/workflows/discord-commands.yml.
const DISCORD_API = 'https://discord.com/api/v10';
const MANAGE_THREADS = 1n << 34n, ADMINISTRATOR = 1n << 3n;
// Every status tag of both forums: a new status replaces whichever of these the post has (category tags stay).
const STATUS_TAGS = ['open', 'in progress', 'planned', 'fixed', "won't fix", "can't reproduce", 'added', 'done', 'not planned',
  'duplicate', 'closed'];
const STATUSES = {
  fixed: { tags: ['fixed'], head: '\u2705 **Fixed**', tail: 'Still happening after updating? Reply here and this post reopens.' },
  wontfix: { tags: ["won't fix"], head: "\uD83D\uDEAB **Won't fix**", tail: 'Questions? Reply here and this post reopens.' },
  cantrepro: { tags: ["can't reproduce"], head: "\uD83D\uDD0D **Can't reproduce**",
    tail: 'Still happening? Reply with more details (steps, a screenshot, godot.log) and this post reopens.' },
  added: { tags: ['added', 'done'], head: '\u2728 **Added**', tail: 'Thanks for the idea! Not quite what you meant? Reply here and this post reopens.' },
  notplanned: { tags: ['not planned'], head: '\uD83D\uDEAB **Not planned**', tail: 'Thanks for the idea! Reply here to talk about it, and this post reopens.' },
  // Duplicate / Custom fit both forums. Their tags are optional: without a "Duplicate" / "Closed" tag in the forum the
  // post keeps its tags and only gets the reply and closed. Custom needs the reason (it's the whole message).
  duplicate: { tags: ['duplicate'], optionalTag: true, head: '\uD83D\uDD01 **Duplicate**',
    tail: 'Follow the other post for updates. If yours is different, reply here and this post reopens.' },
  custom: { tags: ['closed'], optionalTag: true, needsReason: true, head: '\uD83D\uDD12 **Closed**', tail: 'Questions? Reply here and this post reopens.' },
  open: { tags: ['open'], head: '\uD83D\uDD04 **Reopened**', tail: '', reopen: true },
};
// "Won't Fix" -> "won't fix": tag names compared without case, emoji or curly apostrophes.
const tagName = (s) => String(s || '').toLowerCase().replace(/[\u2019`]/g, "'").replace(/[^a-z' ]/g, '').replace(/\s+/g, ' ').trim();

const hexBytes = (h) => (/^(?:[0-9a-f]{2})+$/i.test(h) ? new Uint8Array(h.match(/../g).map((b) => parseInt(b, 16))) : null);

// Discord signs every interaction (Ed25519 over timestamp + body); unsigned or forged requests get 401.
async function verifyDiscord(request, env, buf) {
  const sig = hexBytes(request.headers.get('x-signature-ed25519') || '');
  const ts = request.headers.get('x-signature-timestamp') || '';
  const key = hexBytes(String(env.DISCORD_PUBLIC_KEY || '').trim());
  if (!sig || sig.length !== 64 || !ts || !key || key.length !== 32) return false;
  const t = new TextEncoder().encode(ts);
  const msg = new Uint8Array(t.length + buf.byteLength);
  msg.set(t); msg.set(new Uint8Array(buf), t.length);
  for (const alg of [{ name: 'Ed25519' }, { name: 'NODE-ED25519', namedCurve: 'NODE-ED25519' }]) {
    try {
      const k = await crypto.subtle.importKey('raw', key, alg, false, ['verify']);
      return await crypto.subtle.verify(alg, k, sig, msg);
    } catch { /* older Workers runtime: try the NODE-ED25519 name */ }
  }
  return false;
}

async function handleInteraction(request, env, ctx) {
  const buf = await request.arrayBuffer();
  if (buf.byteLength > MAX_BODY) return tooLarge();
  if (!(await verifyDiscord(request, env, buf))) return text(401, 'invalid request signature');
  let i;
  try { i = JSON.parse(new TextDecoder().decode(buf)); } catch { return bad('invalid JSON'); }
  if (i.type === 1) return json(200, { type: 1 });   // PING (Discord checks the endpoint with it)
  if (i.type !== 2) return bad('unsupported interaction');
  // Only the moderator sees these answers (ephemeral).
  const say = (content) => json(200, { type: 4, data: { content, flags: 64, allowed_mentions: { parse: [] } } });
  const name = i.data && i.data.name;
  if (name !== 'close' && name !== 'reopen') return say('Unknown command.');
  let perms = 0n;
  try { perms = BigInt((i.member && i.member.permissions) || '0'); } catch { /* not a number: no permissions */ }
  if (!(perms & (MANAGE_THREADS | ADMINISTRATOR))) return say('Only moderators can use this.');
  if (!env.DISCORD_BOT_TOKEN) {
    console.log('DISCORD_BOT_TOKEN secret is not set');
    return say("The relay can't change posts yet: its DISCORD_BOT_TOKEN secret isn't set.");
  }
  const ch = i.channel || {};
  if (ch.type !== 11 && ch.type !== 12) return say('Use this inside a #bug-reports or #suggestions post.');
  const opt = (n) => ((i.data.options || []).find((o) => o.name === n) || {}).value;
  const key = name === 'reopen' ? 'open' : String(opt('status') || '');
  if (!STATUSES[key]) return say('Unknown status.');
  // "reason" (older registrations called it "note")
  const note = trunc(clean(String(opt('reason') || opt('note') || '')), 500);
  if (STATUSES[key].needsReason && !note) return say('Custom needs a reason: write it in the `reason` option.');
  // lock: a "permanent" close. Replies can't reopen a locked post, only a moderator (/reopen unlocks).
  const lock = key !== 'open' && opt('lock') === true;
  const work = applyStatus(i, env, key, note, lock);
  if (ctx && ctx.waitUntil) ctx.waitUntil(work); else await work;
  return json(200, { type: 5 });   // public "ValTrainer is thinking...", filled in by applyStatus
}

// Reads the post and its forum, then: the reply (edits the deferred answer), new tags and archived (= closed).
// Anything that goes wrong replaces the public answer with a note only the moderator sees.
async function applyStatus(i, env, key, note, lock = false) {
  const s = STATUSES[key];
  const threadId = (i.channel && i.channel.id) || i.channel_id;
  const hook = `${DISCORD_API}/webhooks/${i.application_id}/${i.token}`;
  const jsonInit = (method, body) => ({ method, headers: { 'content-type': 'application/json' }, body: JSON.stringify(body) });
  const bot = (method, path, body) => fetch(DISCORD_API + path, {
    method,
    headers: { authorization: `Bot ${env.DISCORD_BOT_TOKEN}`, 'content-type': 'application/json',
      'user-agent': 'DiscordBot (https://github.com/idoraz1/ValTrainer, 1)' },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const privately = (content) => fetch(hook, jsonInit('POST', { content, flags: 64, allowed_mentions: { parse: [] } })).catch(() => {});
  const fail = async (content) => {
    await fetch(`${hook}/messages/@original`, { method: 'DELETE' }).catch(() => {});
    await privately(content);
  };
  try {
    const tr = await bot('GET', `/channels/${threadId}`);
    if (!tr.ok) return fail(`Couldn't read this post (HTTP ${tr.status}). Does the bot have View Channel here?`);
    const thread = await tr.json();
    const fr = await bot('GET', `/channels/${thread.parent_id}`);
    if (!fr.ok) return fail(`Couldn't read this forum (HTTP ${fr.status}).`);
    const forum = await fr.json();
    if (forum.type !== 15) return fail('Use this inside a #bug-reports or #suggestions post.');
    const tags = forum.available_tags || [];
    const target = s.tags.map((n) => tags.find((t) => tagName(t.name) === n)).find(Boolean);
    if (!target && !s.optionalTag) return fail(`#${forum.name} has no "${s.tags[0]}" tag, so that status doesn't fit this post.`);
    const drop = new Set(tags.filter((t) => STATUS_TAGS.includes(tagName(t.name))).map((t) => t.id));
    const applied = target
      ? [...new Set([target.id, ...(thread.applied_tags || []).filter((id) => !drop.has(id))])].slice(0, 5)
      : (thread.applied_tags || []);

    const by = i.member && i.member.user && /^\d+$/.test(i.member.user.id) ? ` \u00B7 by <@${i.member.user.id}>` : '';
    const tail = lock ? 'This post is locked: only a moderator can reopen it.' : s.tail;
    const content = [s.head + by, note, tail].filter(Boolean).join('\n');
    const ed = await fetch(`${hook}/messages/@original`, jsonInit('PATCH', { content, allowed_mentions: { parse: [] } }));
    if (!ed.ok) console.log(`interaction reply failed: HTTP ${ed.status}`);
    // after the reply: a message in a closed post would reopen it
    const change = { applied_tags: applied, archived: !s.reopen };
    if (lock || s.reopen) change.locked = lock;   // /reopen also unlocks
    const pr = await bot('PATCH', `/channels/${threadId}`, change);
    if (!pr.ok) {
      console.log(`thread update failed: HTTP ${pr.status}`);
      await privately(`Replied, but changing the tags / ${s.reopen ? 'reopening' : 'closing'} failed (HTTP ${pr.status}). Does the bot have Manage Posts here?`);
    }
  } catch (e) {
    console.log(`interaction failed: ${e && e.message}`);
    await fail('Something went wrong, nothing was changed.');
  }
}

// ---------- request handling ----------
async function handleReport(request, env, kind = 'report') {
  const client = request.headers.get('x-valtrainer-client') || '';
  if (!client.startsWith('ValTrainer/') || client.length > 64) return bad('missing or invalid client header');
  const type = (request.headers.get('content-type') || '').toLowerCase();
  if (!type.startsWith('application/json')) return bad('content-type must be application/json');
  if (Number(request.headers.get('content-length') || 0) > MAX_BODY) return tooLarge();
  const buf = await request.arrayBuffer();
  if (buf.byteLength > MAX_BODY) return tooLarge();

  const idea = kind === 'suggest';
  let report;
  try {
    const body = JSON.parse(new TextDecoder('utf-8', { fatal: true }).decode(buf));
    report = idea ? validateIdea(body) : validate(body);
  } catch (e) {
    if (e instanceof BadRequest) return bad(e.message);
    if (e instanceof SyntaxError || e instanceof TypeError) return bad('invalid JSON');
    throw e;
  }

  // Bug reports -> #bug-reports (DISCORD_WEBHOOK_URL); suggestions -> #suggestions (DISCORD_SUGGEST_WEBHOOK_URL).
  const webhook = idea ? env.DISCORD_SUGGEST_WEBHOOK_URL : env.DISCORD_WEBHOOK_URL;
  if (!webhook) {
    console.log(`${idea ? 'DISCORD_SUGGEST_WEBHOOK_URL' : 'DISCORD_WEBHOOK_URL'} secret is not set`);
    return json(500, { ok: false, error: 'relay not configured' });
  }

  const perHour = Math.min(100, Math.max(1, parseInt(env.MAX_PER_HOUR, 10) || 5));
  // Separate hourly buckets for reports and suggestions (the daily total is shared).
  const ip = await hashIp((request.headers.get('cf-connecting-ip') || 'unknown') + (idea ? '|suggest' : ''));
  const wait = await limit(env.RATE, ip, perHour);
  if (wait) return json(429, { ok: false, error: 'rate limited', retry_after_s: wait }, { 'retry-after': String(wait) });

  const { payload, log } = idea ? { payload: buildIdeaPayload(report, env), log: '' } : buildPayload(report, env);
  const id = await postToDiscord(webhook, payload, log);
  if (id === null) return json(502, { ok: false, error: 'discord failed' });
  return json(200, { ok: true, id });
}

export default {
  async fetch(request, env = {}, ctx = undefined) {
    const { pathname } = new URL(request.url);
    const m = request.method;
    if (m === 'OPTIONS' || (pathname === '/' && (m === 'GET' || m === 'HEAD'))) {
      return text(200, 'ValTrainer bug relay: OK. POST /report, POST /suggest, POST /interactions\n');
    }
    if (pathname === '/') return json(405, { ok: false, error: 'method not allowed' }, { allow: 'GET, OPTIONS' });
    if (!['/report', '/suggest', '/interactions'].includes(pathname)) return json(404, { ok: false, error: 'not found' });
    if (m !== 'POST') return json(405, { ok: false, error: 'method not allowed' }, { allow: 'POST, OPTIONS' });
    try {
      if (pathname === '/interactions') return await handleInteraction(request, env, ctx);
      return await handleReport(request, env, pathname === '/suggest' ? 'suggest' : 'report');
    } catch (e) {
      console.log(`unexpected error: ${e && e.message}`);
      return json(500, { ok: false, error: 'internal error' });
    }
  },
};
