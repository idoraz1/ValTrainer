# ValTrainer bug-report relay (Cloudflare Worker)

A tiny Cloudflare Worker that receives bug reports from ValTrainer's in-app
"Report a bug" form and posts each one as a new thread in the ValTrainer
Discord server's **#bug-reports** forum channel.

## Why a relay?

Posting to Discord needs a webhook URL, and anyone who has that URL can post
anything to the channel (spam, `@everyone` pings, impersonation). ValTrainer is
open source (GPL-3.0), so anything compiled into the app is public. The webhook
therefore lives only in Cloudflare as an encrypted secret. The app only knows the
public Worker URL, and the Worker validates, rate-limits and cleans every
report before forwarding it.

## Data flow

1. The player fills in the bug form in ValTrainer and presses Send.
2. The app sends `POST https://valtrainer-bug-relay.<subdomain>.workers.dev/report`
   with headers `Content-Type: application/json` and
   `X-ValTrainer-Client: ValTrainer/<version>`, and a JSON body (schema 1, max 32 KB):
   app/system info, current screen/mode, a set of settings, the report text, and
   the end of the app log.
3. The Worker:
   - rejects anything malformed (400), too large (413), wrong path/method (404/405);
   - rate-limits: 5 reports per IP per rolling hour (`MAX_PER_HOUR`), and 200
     reports per day in total (429 with `retry_after_s`);
   - cleans the text: strips control characters, puts an invisible zero-width space
     after every `@` (so `@everyone`, `@here`, `<@id>` can never ping), collapses huge
     blank gaps, and also sends `allowed_mentions: {"parse":[]}`;
   - posts to the webhook (`?wait=true`, multipart): thread title `v<version>: <title>`,
     the "what happened" text as the message, one embed with Steps / Expected /
     Contact / App / Context / System / GPU / Display / Settings, and the log tail as
     `log.txt` (plus the full settings if they didn't fit in the embed).
4. The app gets `{"ok":true,"id":"<thread id>"}`, or an error JSON. Discord's own
   error text and the webhook URL are never sent back; a short reason goes to the
   Worker's Cloudflare logs for the owner.

| Status | Body |
|---|---|
| 200 | `{"ok":true,"id":"..."}` |
| 400 | `{"ok":false,"error":"<short reason>"}` |
| 404 / 405 | `{"ok":false,"error":"not found" / "method not allowed"}` |
| 413 | `{"ok":false,"error":"too large"}` |
| 429 | `{"ok":false,"error":"rate limited","retry_after_s":N}` |
| 500 | `{"ok":false,"error":"relay not configured"}` (secret missing) |
| 502 | `{"ok":false,"error":"discord failed"}` |

`GET /` and `OPTIONS` return a one-line plain-text health check.

## Privacy notes

- Only what the player typed plus the technical info listed above (app/system/display
  info, a few settings, the end of the log) is forwarded. The relay adds nothing else.
- The player's IP address is **not** forwarded to Discord. For rate limiting the Worker
  stores a truncated SHA-256 hash of it in KV, which expires after 1 hour. Cloudflare
  itself sees request IPs, as with any website it serves.
- "Contact" is optional and is shown publicly in the forum post.
- Reports are visible to everyone who can read #bug-reports.

## Dashboard deploy guide (no CLI needed)

You need: your Cloudflare account and admin rights on the ValTrainer Discord server.

### 1. Create the Discord webhook

1. In Discord, hover **#bug-reports**, click the gear (**Edit Channel**).
2. **Integrations** → **Webhooks** → **New Webhook**.
3. Click the new webhook, name it **ValTrainer Bug Reports** (an avatar is optional).
4. Click **Copy Webhook URL**. Paste it **only** into Cloudflare in step 4. Never put it
   in the repo, the app, an issue or a chat.
5. In the forum's settings, make sure **"Require people to select tags when posting"**
   is **off**, unless you set `BUG_TAG_ID` (step 5). Otherwise Discord rejects the posts.

### 2. Create the Worker

1. Cloudflare dashboard → **Workers & Pages** → **Create** → **Create Worker**
   (the **"Hello World"** template).
2. Name it **`valtrainer-bug-relay`** → **Deploy**.
3. Click **Edit code**, delete everything in `worker.js`, paste in the full contents of
   this folder's `worker.js`, then click **Deploy**.

### 3. Create the rate-limit store (KV) and bind it as `RATE`

1. **Storage & Databases** → **Workers KV** → **Create** (namespace). Name it
   `valtrainer-bug-relay-rate` → **Add**.
2. Open the Worker → **Settings** → **Bindings** → **Add** → **KV namespace**.
3. Variable name: **`RATE`** (exactly). Namespace: `valtrainer-bug-relay-rate` → **Save/Deploy**.

(If you skip this, the Worker still runs but rate limits only last per server instance,
so they are weaker. The free KV plan easily covers this: each accepted report costs 2 writes.)

### 4. Add the webhook secret

1. Worker → **Settings** → **Variables and Secrets** → **Add**.
2. Type: **Secret**. Name: **`DISCORD_WEBHOOK_URL`**. Value: paste the webhook URL.
3. **Deploy**. (After saving, Cloudflare never shows the value again, and that's expected.)

### 5. Optional plain variables

In the same **Variables and Secrets** screen, Type **Text**:

- **`MAX_PER_HOUR`**: reports per IP per hour (default `5`, allowed 1–100).
- **`BUG_TAG_ID`**: a forum tag ID to put on every post. **Optional; leave it unset.**
  Discord doesn't let you right-click a forum tag to copy its ID (Developer Mode only
  works for servers, channels, users and messages), and the only way to read tag IDs is
  through the Discord API with a bot token. The relay works fine without it.

### 6. Test it

Your Worker URL is shown on the Worker's overview page:
`https://valtrainer-bug-relay.<your-subdomain>.workers.dev`

```bash
curl https://valtrainer-bug-relay.<your-subdomain>.workers.dev/
# -> ValTrainer bug relay: OK. POST /report

curl -X POST https://valtrainer-bug-relay.<your-subdomain>.workers.dev/report \
  -H "Content-Type: application/json" \
  -H "X-ValTrainer-Client: ValTrainer/1.8.0" \
  -d '{
    "schema": 1,
    "app": { "version": "1.8.0", "install": "dev", "os": "Windows 11", "ram_gb": 16,
             "display": "2560x1440 @ 165 Hz" },
    "context": { "screen": "menu", "mode": "gridshot", "uptime_s": 42 },
    "settings": { "sensitivity": "0.35" },
    "report": { "title": "Relay test", "what": "Testing the bug relay, please ignore.",
                "contact": "" },
    "log_tail": "test log line"
  }'
# -> {"ok":true,"id":"<thread id>"}   and a new post appears in #bug-reports
```

On Windows PowerShell, use `curl.exe` and put the JSON in a file (`-d "@body.json"`).
If something fails, open the Worker → **Logs** (or **Observability**) to see the
reason. Give the ValTrainer devs **only the Worker URL** above. Never share the webhook.

## If the webhook leaks or gets abused

1. Discord: **#bug-reports → Edit Channel → Integrations → Webhooks** → select
   *ValTrainer Bug Reports* → **Delete Webhook**. The old URL stops working at once.
2. Create a new webhook (step 1) and copy its URL.
3. Cloudflare: Worker → **Settings → Variables and Secrets** → edit
   `DISCORD_WEBHOOK_URL` → paste the new URL → **Deploy**.

The app doesn't need an update because it only knows the Worker URL. To slow down spam
from the relay itself, lower `MAX_PER_HOUR`. To stop it entirely, delete the secret
(the relay then returns 500) or disable the Worker's `workers.dev` route.

## Local test

```bash
node test.mjs     # mocks fetch and KV; no network, no secrets
```

## Wrangler (optional)

`wrangler.toml` is provided for CLI users: set the KV `id`, run
`wrangler secret put DISCORD_WEBHOOK_URL`, then `wrangler deploy`.
