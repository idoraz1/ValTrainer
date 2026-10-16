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
- **`BUG_TAG_ID`**: forum tag ID(s) to put on every post, comma-separated (max 5), e.g. the
  **Open** status tag. Optional. Discord doesn't let you right-click a forum tag to copy its ID
  (Developer Mode only works for servers, channels, users and messages); read the IDs through
  the Discord API with the bug-tracker bot's token (see "Marking reports fixed" below), or use
  the Actions workflow "Discord bug reports - mark fixed", which logs the forum's tags.

### 5b. Feature suggestions (`/suggest` → #suggestions)

The in-app **Suggest a feature** form posts to `/suggest` on the same Worker (title, idea, why, an
optional category, Discord name, and only the app version and install type; nothing about the PC
and no log). It has its own hourly limit per IP, separate from bug reports.

1. In Discord: **#suggestions** → Edit Channel → **Integrations → Webhooks → New Webhook**, name
   it "ValTrainer Suggestions", **Copy Webhook URL**.
2. Cloudflare: Worker → **Settings → Variables and Secrets** → add a **Secret**
   **`DISCORD_SUGGEST_WEBHOOK_URL`** with that URL. Without it `/suggest` answers 500
   ("relay not configured") and the game tells the player to copy the idea instead.
3. Plain **Text** variables (optional, but #suggestions has "Require tags" on, so set at least the first):
   - **`SUGGEST_TAG_ID`**: tag id(s) for every suggestion, e.g. the **Open** tag.
   - **`SUGGEST_CATEGORY_TAGS`**: `drill:<id>,agent:<id>,map:<id>,coach:<id>,ui:<id>`. The category
     the player picked adds that tag ("other" adds none).

## Marking reports fixed (GitHub-issue style)

`#bug-reports` has status tags **Open**, **In progress**, **Fixed**, **Won't fix** and
**Can't reproduce**. When a release fixes a report, the release workflow
(`.github/workflows/discord-fixes.yml`) replies in the thread with "Fixed in ValTrainer x.y.z" and a link
to the release notes, swaps the status tag to **Fixed** and closes the post. Anyone replying reopens it.

1. In the fixing commit, add a trailer line with the thread id (the last number of the post's link):
   `Fixes-Discord: 1560414622480334859` (several ids may be comma-separated).
2. Publish as usual. The job runs after the release.

The job needs a Discord **bot**, because webhooks can't change tags or close posts:

1. <https://discord.com/developers/applications> → **New Application** ("ValTrainer Bug Tracker") →
   **Bot** → **Reset Token** → copy it. Paste it **only** into GitHub: repository → Settings →
   Secrets and variables → Actions → New secret **`DISCORD_BOT_TOKEN`**.
2. **OAuth2 → URL Generator**: scope `bot`; permissions View Channels, Send Messages,
   Send Messages in Threads, Read Message History, Manage Threads. Open the URL and add the bot
   to the ValTrainer server.
3. To catch up on an earlier release: Actions → "Discord bug reports - mark fixed" → Run workflow
   (version, and optionally extra thread ids).
4. Give the bot the same permissions on **#suggestions**. A commit line
   `Adds-Discord: <thread id>` marks a suggestion the same way: "Added in ValTrainer x.y.z", the
   **Done** (or **Added**) tag replaces Open / Planned / In progress / Not planned, and the post closes.

## Moderator commands: /close and /reopen

Inside a #bug-reports or #suggestions post, moderators (Manage Posts) and admins can type:

- `/close status:<Fixed | Won't fix | Can't reproduce | Added | Not planned | Duplicate | Custom> [reason]`:
  replies in the post ("✅ Fixed · by @mod" + the reason), swaps the status tag (category tags stay) and closes
  the post. **Custom** is for anything else: the reason is required and is the message. Duplicate and Custom
  use a "Duplicate" / "Closed" tag if the forum has one (make them mod-only), else the tags stay as they are.
  With `lock:True` it's a permanent close: the post is locked, so replies can't reopen it, only a moderator.
- `/reopen [reason]`: replies "🔄 Reopened", sets the tag back to **Open**, unlocks and reopens the post.

`worker.js` is kept ASCII-only (emoji as `\u` escapes; `node test.mjs` checks it): Windows PowerShell 5.1 reads
UTF-8 files without a BOM as ANSI, which once turned the emoji into mojibake on the way to the Cloudflare editor.

Nobody else sees the commands, and the Worker checks the permission again. Discord sends each command,
signed, to the Worker's `/interactions`; the Worker uses the bot to read and change the post.

Setup (once):

1. Cloudflare → Worker → **Settings → Variables and Secrets**:
   - **Secret** `DISCORD_BOT_TOKEN`: the bug-tracker bot's token (same as the GitHub secret).
   - **Text** `DISCORD_PUBLIC_KEY`: Developer Portal → your app → **General Information → Public Key**.
2. Deploy this `worker.js`.
3. Developer Portal → **General Information → Interactions Endpoint URL**:
   `https://valtrainer-bug-relay.<subdomain>.workers.dev/interactions` → **Save Changes**. Discord tests it
   right away; "interactions_endpoint_url could not be verified" means step 1 or 2 isn't done.
4. GitHub → Actions → **Discord - register mod commands** → Run workflow. It registers
   `commands.json` in the server. If it fails with 403 "Missing Access", re-add the bot with the
   scopes `bot` **and** `applications.commands` (OAuth2 → URL Generator).
5. Moderators need **Manage Posts** in the two forums (admins always can). Server Settings →
   Integrations → the bot → can limit the commands to roles or channels further.

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
