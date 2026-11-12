// ValTrainer supporter webhook (Supabase Edge Function "supporter-webhook", verify_jwt = false: Ko-fi and Buy Me a
// Coffee can't send a Supabase JWT, so each request is authenticated by the platform's own secret instead).
//
//   POST .../supporter-webhook?src=kofi   Ko-fi: form field "data" = JSON; data.verification_token must equal KOFI_TOKEN
//   POST .../supporter-webhook?src=bmc    Buy Me a Coffee: JSON body; header x-signature-sha256 = hex HMAC-SHA256 of the
//                                         raw body with BMC_SECRET
//
//   POST .../supporter-webhook?claim      the game's GET MY CODE: JSON {"email": "..."} → {"ok": true, "name", "code"} for
//                                         the newest real payment with that email, or {"ok": false, "error":
//                                         "not_found" | "rate_limited" | "bad_email"}. At most CLAIMS_PER_HOUR tries per
//                                         address per hour (public.supporter_claims); unknown and "no donation" look alike.
//
// For a real payment it signs a supporter code (codes.ts, the server key SUPPORTER_PRIVATE_KEY) and records it once in
// public.supporter_codes (unique on platform + transaction id), so a webhook retry never makes a second code. The donor
// claims it in the game with the email they paid with. If BREVO_API_KEY and SENDER_EMAIL are set (a sender on your own
// domain), the code is also emailed. Test events (Ko-fi's "send test" with its example address, BMC's live_mode = false)
// are recorded as tests and can't be claimed.
//
// Secrets (Supabase → Edge Functions → Secrets): KOFI_TOKEN, BMC_SECRET, SUPPORTER_PRIVATE_KEY; optional BREVO_API_KEY,
// SENDER_EMAIL, SENDER_NAME, NOTIFY_EMAIL (email delivery). SUPABASE_URL and SUPABASE_SERVICE_ROLE_KEY are provided by
// Supabase.
import { createClient } from "npm:@supabase/supabase-js@2.45.4";
import { cleanName, importPrivateKey, makeCode } from "./codes.ts";

type Payment = { platform: "kofi" | "bmc"; txId: string; name: string; email: string | null; amount: string; currency: string; kind: string; test: boolean };

const env = (k: string) => Deno.env.get(k) ?? "";
const db = createClient(env("SUPABASE_URL"), env("SUPABASE_SERVICE_ROLE_KEY"), { auth: { persistSession: false } });

function timingSafeEqual(a: string, b: string): boolean {
  if (a.length !== b.length || a.length === 0) return false;
  let d = 0;
  for (let i = 0; i < a.length; i++) d |= a.charCodeAt(i) ^ b.charCodeAt(i);
  return d === 0;
}

async function hmacHex(secret: string, body: string): Promise<string> {
  const key = await crypto.subtle.importKey("raw", new TextEncoder().encode(secret), { name: "HMAC", hash: "SHA-256" }, false, ["sign"]);
  const mac = new Uint8Array(await crypto.subtle.sign("HMAC", key, new TextEncoder().encode(body)));
  return Array.from(mac, (b) => b.toString(16).padStart(2, "0")).join("");
}

const CLAIMS_PER_HOUR = 5;

/** A plausible email, trimmed and lower-cased (the claim compares lower-cased), or null. */
function normEmail(e: unknown): string | null {
  if (typeof e !== "string") return null;
  const s = e.trim().toLowerCase();
  return s.length <= 254 && /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(s) ? s : null;
}

const json = (o: unknown, status = 200) => new Response(JSON.stringify(o), { status, headers: { "content-type": "application/json" } });

async function claim(req: Request): Promise<Response> {
  let body: Record<string, unknown> = {};
  try { body = await req.json(); } catch { /* bad body: treated as a bad email */ }
  const email = normEmail(body.email);
  if (!email) return json({ ok: false, error: "bad_email" });
  // rate limit per client address (the first X-Forwarded-For hop is the caller)
  const ip = (req.headers.get("x-forwarded-for") ?? "").split(",")[0].trim() || "unknown";
  const since = new Date(Date.now() - 3600_000).toISOString();
  const { count } = await db.from("supporter_claims").select("id", { count: "exact", head: true }).eq("ip", ip).gte("at", since);
  if ((count ?? 0) >= CLAIMS_PER_HOUR) return json({ ok: false, error: "rate_limited" });
  await db.from("supporter_claims").insert({ ip, found: false });
  const { data } = await db.from("supporter_codes").select("name, code").eq("email", email).eq("test", false)
    .order("created_at", { ascending: false }).limit(1).maybeSingle();
  if (!data) return json({ ok: false, error: "not_found" });
  await db.from("supporter_codes").update({ claimed_at: new Date().toISOString() }).eq("code", data.code);
  return json({ ok: true, name: data.name, code: data.code });
}

async function parseKofi(req: Request): Promise<Payment | Response> {
  const form = await req.formData();
  let d: Record<string, unknown>;
  try { d = JSON.parse(String(form.get("data") ?? "")); } catch { return new Response("bad payload", { status: 400 }); }
  if (!timingSafeEqual(String(d.verification_token ?? ""), env("KOFI_TOKEN"))) return new Response("unauthorized", { status: 401 });
  const email = normEmail(d.email);
  return {
    platform: "kofi",
    txId: String(d.kofi_transaction_id ?? d.message_id ?? ""),
    name: String(d.from_name ?? ""),
    email,
    amount: String(d.amount ?? ""),
    currency: String(d.currency ?? ""),
    kind: String(d.type ?? "Donation"),
    test: !email || /@example\.(com|org)$/i.test(email), // Ko-fi's "send a test" uses an example address
  };
}

async function parseBmc(req: Request): Promise<Payment | Response> {
  const raw = await req.text();
  const sig = (req.headers.get("x-signature-sha256") ?? req.headers.get("x-bmc-signature") ?? "").trim().toLowerCase();
  if (!env("BMC_SECRET") || !timingSafeEqual(sig, await hmacHex(env("BMC_SECRET"), raw))) return new Response("unauthorized", { status: 401 });
  let p: Record<string, any>;
  try { p = JSON.parse(raw); } catch { return new Response("bad payload", { status: 400 }); }
  const type = String(p.type ?? "");
  // payments only: one-off support, memberships and extras (not refunds, cancellations or other events)
  if (!/^(donation|membership|extra|coffee|support)[._]/i.test(type) || /refund|cancel|delete/i.test(type)) return new Response("ignored", { status: 200 });
  const d = p.data ?? {};
  const email = normEmail([d.supporter_email, d.payer_email, d.email].find((e) => typeof e === "string" && e.includes("@")));
  return {
    platform: "bmc",
    txId: String(d.transaction_id ?? d.id ?? p.id ?? ""),
    name: String(d.supporter_name ?? d.payer_name ?? d.name ?? ""),
    email,
    amount: String(d.amount ?? d.total_amount ?? ""),
    currency: String(d.currency ?? ""),
    kind: type,
    test: p.live_mode === false || !email,
  };
}

function emailHtml(name: string, code: string): string {
  const esc = (s: string) => s.replace(/[&<>"]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[c]!));
  return `<div style="font-family:Segoe UI,Arial,sans-serif;max-width:560px;color:#111">
<h2 style="margin:0 0 12px">Thank you for supporting ValTrainer, ${esc(name)}!</h2>
<p>Your support keeps ValTrainer free and growing. As a thank-you, here is your <b>supporter code</b>. It unlocks every
skin, level and variant, including skins added in future versions:</p>
<p style="font-family:Consolas,monospace;font-size:14px;background:#f2f2f2;padding:12px;word-break:break-all">${code}</p>
<p><b>How to use it:</b> open ValTrainer → <b>Settings</b> → <b>About</b> → <b>Supporter code</b>, paste the code and press
<b>APPLY</b>. It is checked on your PC (no account, no internet needed) and keeps working after updates.</p>
<p style="color:#666;font-size:12px">Keep this email: you can paste the code again on a new PC. ValTrainer is a free, fan-made
aim trainer for VALORANT players, not affiliated with Riot Games.</p></div>`;
}

async function sendEmail(to: string, name: string, code: string): Promise<string | null> {
  const body = {
    sender: { email: env("SENDER_EMAIL"), name: env("SENDER_NAME") || "ValTrainer" },
    to: [{ email: to, name }],
    bcc: env("NOTIFY_EMAIL") ? [{ email: env("NOTIFY_EMAIL") }] : undefined,
    subject: "Your ValTrainer supporter code",
    htmlContent: emailHtml(name, code),
    textContent: `Thank you for supporting ValTrainer, ${name}!\n\nYour supporter code (unlocks every skin, now and in future versions):\n\n${code}\n\nOpen ValTrainer -> Settings -> About -> Supporter code, paste it and press APPLY.`,
  };
  const r = await fetch("https://api.brevo.com/v3/smtp/email", {
    method: "POST",
    headers: { "api-key": env("BREVO_API_KEY"), "content-type": "application/json", accept: "application/json" },
    body: JSON.stringify(body),
  });
  return r.ok ? null : `brevo ${r.status}: ${(await r.text()).slice(0, 300)}`;
}

Deno.serve(async (req) => {
  if (req.method !== "POST") return new Response("ValTrainer supporter webhook", { status: 200 });
  if (new URL(req.url).searchParams.has("claim")) return await claim(req);
  const src = new URL(req.url).searchParams.get("src");
  const parsed = src === "kofi" ? await parseKofi(req) : src === "bmc" ? await parseBmc(req) : new Response("unknown source", { status: 400 });
  if (parsed instanceof Response) return parsed;
  const pay = parsed;
  if (!pay.txId) return new Response("no transaction id", { status: 400 });

  // once per payment: an existing row means a retry (answer OK so the platform stops retrying)
  const { data: existing } = await db.from("supporter_codes").select("id, emailed").eq("platform", pay.platform).eq("tx_id", pay.txId).maybeSingle();
  if (existing) return new Response("already handled", { status: 200 });

  const name = cleanName(pay.name);
  let code: string;
  try { code = await makeCode(await importPrivateKey(env("SUPPORTER_PRIVATE_KEY")), name); }
  catch { return new Response("not configured: SUPPORTER_PRIVATE_KEY", { status: 503 }); } // the platform retries later
  const { data: row, error } = await db.from("supporter_codes").insert({
    platform: pay.platform, tx_id: pay.txId, name, email: pay.email, amount: pay.amount, currency: pay.currency,
    kind: pay.kind, test: pay.test, code,
  }).select("id").single();
  if (error) return new Response(error.code === "23505" ? "already handled" : "db error", { status: error.code === "23505" ? 200 : 500 });

  if (pay.test || !pay.email) return new Response("recorded (test)", { status: 200 });
  if (!env("BREVO_API_KEY") || !env("SENDER_EMAIL")) return new Response("recorded (claim in the game)", { status: 200 });
  const err = await sendEmail(pay.email, name, code);
  await db.from("supporter_codes").update({ emailed: err === null, error: err }).eq("id", row.id);
  // 200 even if the email failed: the code is saved, the maintainer can resend it (a 5xx would make the platform retry,
  // and the retry would be skipped as "already handled" anyway)
  return new Response(err ? "recorded, email failed" : "sent", { status: 200 });
});
