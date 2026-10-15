// Local test for worker.js: mocked fetch + Map-based KV. Run with: node test.mjs
import worker from './worker.js';

const FAKE_HOOK = 'https://discord.test/api/webhooks/123/SECRET-TOKEN';
let passed = 0, failed = 0;
const check = (name, cond, extra = '') => {
  if (cond) passed++; else { failed++; console.error(`FAIL: ${name} ${extra}`); }
};

// ---- mocks ----
class MapKV {
  constructor() { this.m = new Map(); }
  async get(k) { return this.m.has(k) ? this.m.get(k) : null; }
  async put(k, v) { this.m.set(k, String(v)); }
}
let calls = [];
let discordReply = () => new Response(JSON.stringify({ id: '111', channel_id: '222' }), { status: 200 });
globalThis.fetch = async (url, init) => { calls.push({ url: String(url), init }); return discordReply(); };
const logs = [];
const realLog = console.log;
console.log = (...a) => logs.push(a.join(' '));

const env = (extra = {}) => ({ DISCORD_WEBHOOK_URL: FAKE_HOOK, RATE: new MapKV(), ...extra });
let ipSeq = 0;
const freshIp = () => `10.0.0.${++ipSeq}`;

function body(over = {}) {
  return {
    schema: 1,
    app: { version: '1.8.0', install: 'installer', os: 'Windows 11', cpu: 'Ryzen 7', ram_gb: 16,
      gpu: 'RTX 3070', gpu_type: 'discrete', renderer: 'Forward+', driver: '560.1',
      display: '2560x1440 @ 165 Hz', window_mode: 'fullscreen', fps_cap: '240', graphics: 'high' },
    context: { screen: 'menu', mode: 'gridshot', uptime_s: 123 },
    settings: { sens: '0.35', crosshair: 'green' },
    report: { title: 'Crash on start', what: 'The game crashes when I press play.',
      steps: '1. open\n2. press play', expected: 'It runs', contact: 'someone#1' },
    log_tail: 'line1\nline2',
    ...over,
  };
}
function req(b, { method = 'POST', path = '/report', ip = freshIp(), headers = {} } = {}) {
  const h = { 'content-type': 'application/json', 'x-valtrainer-client': 'ValTrainer/1.8.0',
    'cf-connecting-ip': ip, ...headers };
  for (const k of Object.keys(h)) if (h[k] === null) delete h[k];
  const init = { method, headers: h };
  if (b !== undefined && method !== 'GET' && method !== 'HEAD') init.body = typeof b === 'string' ? b : JSON.stringify(b);
  return new Request(`https://relay.test${path}`, init);
}
const send = async (r, e = env()) => {
  const res = await worker.fetch(r, e);
  const txt = await res.text();
  let j = null; try { j = JSON.parse(txt); } catch {}
  return { status: res.status, txt, j, res };
};
const sentPayload = async (i = calls.length - 1) => {
  const form = calls[i].init.body;
  const file = form.get('files[0]');
  return { p: JSON.parse(form.get('payload_json')), file, fileText: file ? await file.text() : null };
};

// ---- routing / health ----
let r = await send(req(undefined, { method: 'GET', path: '/' }));
check('GET / health', r.status === 200 && r.txt.includes('OK') && !r.txt.includes('SECRET'));
r = await send(req(undefined, { method: 'OPTIONS', path: '/report' }));
check('OPTIONS health', r.status === 200 && r.res.headers.get('content-type').startsWith('text/plain'));
r = await send(req(undefined, { method: 'GET', path: '/nope' }));
check('unknown path 404', r.status === 404 && r.j?.ok === false);
r = await send(req(undefined, { method: 'GET', path: '/report' }));
check('GET /report 405', r.status === 405);
r = await send(req('{}', { method: 'PUT', path: '/' }));
check('PUT / 405', r.status === 405);

// ---- valid report ----
calls = [];
const e1 = env({ BUG_TAG_ID: '1234567890123, 9876543210987,junk' });
r = await send(req(body()), e1);
check('valid -> 200', r.status === 200 && r.j?.ok === true && r.j?.id === '222', r.txt);
check('valid -> exact response keys', r.j && Object.keys(r.j).join() === 'ok,id');
check('discord called once', calls.length === 1);
check('wait=true + same webhook', calls[0].url === FAKE_HOOK + '?wait=true');
check('multipart body', calls[0].init.body instanceof FormData && calls[0].init.method === 'POST');
let { p, file, fileText } = await sentPayload();
check('thread_name', p.thread_name === 'v1.8.0: Crash on start');
check('username', p.username === 'ValTrainer Bug Reports');
check('allowed_mentions', JSON.stringify(p.allowed_mentions) === '{"parse":[]}');
check('applied_tags (comma list, junk dropped)', JSON.stringify(p.applied_tags) === '["1234567890123","9876543210987"]', JSON.stringify(p.applied_tags));
check('content = what', p.content === 'The game crashes when I press play.');
check('one embed, color', p.embeds.length === 1 && p.embeds[0].color === 16729685);
const names = p.embeds[0].fields.map((f) => f.name);
check('embed fields', ['Steps', 'Expected', 'Contact', 'App', 'Context', 'System', 'GPU', 'Display', 'Settings']
  .every((n) => names.includes(n)), names.join());
const fv = (n) => p.embeds[0].fields.find((f) => f.name === n)?.value;
check('App value', fv('App') === 'v1.8.0 · installer');
check('System value', fv('System') === 'Windows 11 · Ryzen 7 · 16 GB RAM');
check('Settings k=v', fv('Settings') === 'sens=0.35\ncrosshair=green');
check('log.txt attached', file && file.name === 'log.txt' && fileText === 'line1\nline2');
check('attachments meta', JSON.stringify(p.attachments) === '[{"id":0,"filename":"log.txt"}]');

// no tag var, no log -> no applied_tags, no file
calls = [];
r = await send(req(body({ log_tail: '' })));
({ p, file } = await sentPayload());
check('no tag / no file', r.status === 200 && !p.applied_tags && !file && !p.attachments);

// ---- validation -> 400 ----
const expect400 = async (name, b, opts) => {
  calls = [];
  const x = await send(req(b, opts));
  check(`400: ${name}`, x.status === 400 && x.j?.ok === false && typeof x.j.error === 'string' && calls.length === 0, x.txt);
};
await expect400('no client header', body(), { headers: { 'x-valtrainer-client': null } });
await expect400('bad client header', body(), { headers: { 'x-valtrainer-client': 'curl/8' } });
await expect400('wrong content-type', body(), { headers: { 'content-type': 'text/plain' } });
await expect400('invalid json', '{nope');
await expect400('schema 2', body({ schema: 2 }));
await expect400('no version', body({ app: { install: 'dev' } }));
await expect400('version too long', body({ app: { version: 'x'.repeat(33) } }));
await expect400('no title', body({ report: { what: 'long enough text here' } }));
await expect400('short title', body({ report: { title: 'ab', what: 'long enough text here' } }));
await expect400('short what', body({ report: { title: 'abc', what: 'short' } }));
await expect400('long steps', body({ report: { title: 'abc', what: 'long enough text', steps: 's'.repeat(1501) } }));
await expect400('long contact', body({ report: { title: 'abc', what: 'long enough text', contact: 'c'.repeat(65) } }));
await expect400('long os', body({ app: { version: '1', os: 'o'.repeat(121) } }));
await expect400('object field', body({ context: { screen: { a: 1 } } }));
await expect400('too many settings', body({ settings: Object.fromEntries([...Array(31)].map((_, i) => [`k${i}`, 'v'])) }));
await expect400('long settings key', body({ settings: { ['k'.repeat(41)]: 'v' } }));
await expect400('long log_tail', body({ log_tail: 'x'.repeat(16001) }));
await expect400('array body', [1, 2]);

// ---- size -> 413 ----
calls = [];
r = await send(req(body({ pad: 'x'.repeat(33 * 1024) })));
check('oversized -> 413', r.status === 413 && r.j?.error === 'too large' && calls.length === 0);
r = await send(req(body({ pad: 'x'.repeat(31 * 1024) })));
check('31 KB with unknown field ok', r.status === 200, r.txt);

// ---- mentions / hygiene ----
calls = [];
r = await send(req(body({ report: { title: '@everyone look', what: 'ping @here and <@123> and <@&456>\u0007\u0000 end\n\n\n\n\n\n\nafter',
  steps: '@everyone' } })));
({ p } = await sentPayload());
const all = JSON.stringify(p);
check('mentions -> 200', r.status === 200);
check('no raw @everyone/@here', !/@everyone|@here|<@\d|<@&\d/.test(all));
check('zwsp inserted', p.content.includes('@​here') && p.thread_name.includes('@​everyone'));
check('control chars stripped', !/[\u0000\u0007]/.test(p.content));
check('blank lines collapsed', p.content.includes('end\n\n\n\nafter') && !p.content.includes('\n\n\n\n\n'));

// ---- Discord limits + settings overflow into log.txt ----
calls = [];
const bigSettings = Object.fromEntries([...Array(30)].map((_, i) => [`setting_${String(i).padStart(2, '0')}_${'k'.repeat(28)}`, 'v'.repeat(120)]));
r = await send(req(body({ settings: bigSettings, log_tail: '',
  report: { title: 'T'.repeat(100), what: 'w'.repeat(1500), steps: 's'.repeat(1500), expected: 'e'.repeat(1500), contact: 'c'.repeat(64) } })));
({ p, fileText } = await sentPayload());
const emb = p.embeds[0];
const total = emb.title.length + emb.fields.reduce((n, f) => n + f.name.length + f.value.length, 0);
check('big -> 200', r.status === 200, r.txt);
check('thread_name <= 100', p.thread_name.length <= 100 && p.thread_name.endsWith('…'));
check('field values <= 1024', emb.fields.every((f) => f.value.length <= 1024 && f.value.length > 0));
check('embed total <= 6000', total <= 6000, String(total));
check('content <= 2000', p.content.length <= 2000);
check('steps truncated with …', fv('Steps').length === 1024 && fv('Steps').endsWith('…'));
check('full settings in log.txt', fileText && fileText.includes('--- settings (full) ---') &&
  Object.keys(bigSettings).every((k) => fileText.includes(`${k}=${'v'.repeat(120)}`)));

// ---- rate limit (KV) ----
const eRate = env();
const ip = '203.0.113.7';
let statuses = [];
for (let i = 0; i < 6; i++) statuses.push((await send(req(body(), { ip }), eRate)));
check('5 allowed', statuses.slice(0, 5).every((x) => x.status === 200));
const sixth = statuses[5];
check('6th -> 429', sixth.status === 429 && sixth.j?.error === 'rate limited' && sixth.j.ok === false);
check('retry_after_s sane', Number.isInteger(sixth.j?.retry_after_s) && sixth.j.retry_after_s > 3500 && sixth.j.retry_after_s <= 3600);
check('Retry-After header', sixth.res.headers.get('retry-after') === String(sixth.j?.retry_after_s));
r = await send(req(body(), { ip: '203.0.113.8' }), eRate);
check('other IP still ok', r.status === 200);
r = await send(req(body(), { ip }), env({ RATE: eRate.RATE, MAX_PER_HOUR: '10' }));
check('MAX_PER_HOUR raises limit', r.status === 200);

// global daily cap
const eDay = env();
await eDay.RATE.put(`day:${new Date().toISOString().slice(0, 10)}`, '200');
r = await send(req(body()), eDay);
check('global cap -> 429', r.status === 429 && r.j.retry_after_s > 0 && r.j.retry_after_s <= 86400);

// in-memory fallback (no KV bound)
const eMem = { DISCORD_WEBHOOK_URL: FAKE_HOOK };
statuses = [];
for (let i = 0; i < 6; i++) statuses.push((await send(req(body(), { ip: '198.51.100.1' }), eMem)).status);
check('memory fallback limits', statuses.join() === '200,200,200,200,200,429', statuses.join());

// ---- failures ----
r = await send(req(body()), { RATE: new MapKV() });
check('missing secret -> 500', r.status === 500 && r.j?.ok === false);
discordReply = () => new Response('{"message":"Unknown Webhook SECRET-TOKEN","code":10015}', { status: 404 });
r = await send(req(body()));
check('discord reject -> 502', r.status === 502 && r.j?.error === 'discord failed');
check('no discord body / secret echoed', !r.txt.includes('Unknown') && !r.txt.includes('SECRET'));
discordReply = () => { throw new TypeError('network down'); };
r = await send(req(body()));
check('discord network error -> 502', r.status === 502);
check('webhook URL never logged', !logs.some((l) => l.includes(FAKE_HOOK)));

console.log = realLog;
console.log(`${passed} passed, ${failed} failed`);
process.exit(failed ? 1 : 0);
