// ValTrainer supporter codes: the signing core (same format as tools/supporter/supporter_code.py and the game's
// src/Core/Supporter.cs). WebCrypto only, so it runs on Supabase Edge (Deno) and in Node for the tests.
//
// Layout before base32: version 1 | name length | name (UTF-8, <= 32 bytes) | days since 2026-01-01 (2, little endian)
// | 4 random bytes | ECDSA P-256 signature r||s over SHA-256 of everything before it (64 bytes).
// Shown as "VT-" + unpadded RFC 4648 base32 in groups of 5.

const ABC = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
const EPOCH = Date.UTC(2026, 0, 1);

export function base32(bytes: Uint8Array): string {
  let out = "", buf = 0, bits = 0;
  for (const b of bytes) {
    buf = (buf << 8) | b; bits += 8;
    while (bits >= 5) { bits -= 5; out += ABC[(buf >> bits) & 31]; }
  }
  if (bits > 0) out += ABC[(buf << (5 - bits)) & 31];
  return out;
}

export function group(s: string): string {
  const parts: string[] = [];
  for (let i = 0; i < s.length; i += 5) parts.push(s.slice(i, i + 5));
  return "VT-" + parts.join("-");
}

/** The supporter's name cut to at most 32 bytes of UTF-8 (never inside a character), trimmed; "Supporter" if empty. */
export function cleanName(name: string | null | undefined): string {
  let s = (name ?? "").replace(/[\u0000-\u001f\u007f]/g, " ").replace(/\s+/g, " ").trim();
  if (!s) s = "Supporter";
  const enc = new TextEncoder();
  while (enc.encode(s).length > 32) s = Array.from(s).slice(0, -1).join("").trimEnd();
  return s || "Supporter";
}

export async function importPrivateKey(pkcs8b64: string): Promise<CryptoKey> {
  const der = Uint8Array.from(atob(pkcs8b64.trim()), (c) => c.charCodeAt(0));
  return await crypto.subtle.importKey("pkcs8", der, { name: "ECDSA", namedCurve: "P-256" }, false, ["sign"]);
}

/** A supporter code for <name>, signed with <key>. WebCrypto's ECDSA signature is already r||s (64 bytes). */
export async function makeCode(key: CryptoKey, name: string, now: Date = new Date()): Promise<string> {
  const nb = new TextEncoder().encode(cleanName(name));
  const days = Math.max(0, Math.floor((now.getTime() - EPOCH) / 86400000));
  const payload = new Uint8Array(2 + nb.length + 2 + 4);
  payload[0] = 1; payload[1] = nb.length;
  payload.set(nb, 2);
  payload[2 + nb.length] = days & 0xff; payload[3 + nb.length] = (days >> 8) & 0xff;
  payload.set(crypto.getRandomValues(new Uint8Array(4)), 4 + nb.length);
  const sig = new Uint8Array(await crypto.subtle.sign({ name: "ECDSA", hash: "SHA-256" }, key, payload));
  const all = new Uint8Array(payload.length + sig.length);
  all.set(payload); all.set(sig, payload.length);
  return group(base32(all));
}
