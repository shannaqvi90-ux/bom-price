# Production deploy runbook — BOM Price Approval

This runbook walks the **first-time** production deployment of:

- **Backend (.NET 8 API + PostgreSQL)** → Fly.io + Neon
- **Web frontend (React + Vite)** → Cloudflare Pages
- **Mobile app (Expo / React Native)** → EAS preview channel (already wired)

Target audience: project owner doing the deploy. Total time: ~2-3 hours for first run, <15 min for subsequent deploys.

**Cost:** $0/month using all free tiers. (~$10/yr if you register a custom domain.)

---

## Step 0 — Account creation (you do this; ~30 min total)

These need email + sometimes a credit card on file (no charge, just verification). Claude cannot do these for you.

### 0.1 Fly.io (~10 min)

1. Sign up at https://fly.io/app/sign-up — Google or GitHub OAuth.
2. **Add a credit card on file** (required even for free tier — no charge for the workload we're targeting).
3. Install the CLI:
   - Windows: `iwr https://fly.io/install.ps1 -useb | iex` (PowerShell)
4. Authenticate: `fly auth login` — opens browser.
5. Verify: `fly version` — should print version.

### 0.2 Neon (~5 min)

1. Sign up at https://neon.tech — GitHub OAuth.
2. Create a project: name `bom-price-approval`, region **Frankfurt** (matches Fly.io region for low latency).
3. From the dashboard, copy the **connection string** (Connection Details → Pooled connection — uses `pgbouncer` for the small free-tier pool). Looks like:
   ```
   postgresql://USER:PASS@ep-XXXX.eu-central-1.aws.neon.tech/bom_price_approval?sslmode=require
   ```
4. Save this somewhere private — we need it in step 1.

### 0.3 Cloudflare (~5 min)

1. Sign up at https://dash.cloudflare.com/sign-up — email + verify.
2. No credit card needed for Pages free tier.
3. (Optional) Add a custom domain to Cloudflare DNS — needed only if you bought a `.com` from Namecheap. Skip for now if using `bom-fpf.pages.dev` subdomain.

### 0.4 GitHub (already done)

You already have https://github.com/shannaqvi90-ux/bom-price set up. Cloudflare Pages will connect to this repo.

---

## Step 1 — Backend deploy to Fly.io (~30 min)

### 1.1 Create the Fly app

From the repo root:

```bash
fly launch --copy-config --no-deploy
```

When prompted:
- App name: `bom-fpf-api` (or pick another — `fly.toml` will be updated). If taken, try `bom-fpf-api-1` or similar.
- Region: `fra` (Frankfurt — already in `fly.toml`).
- PostgreSQL: **No** (we're using Neon, not Fly's PG).
- Redis / Tigris: No.

This rewrites `fly.toml` with your final app name. Commit the change.

### 1.2 Set secrets

`fly secrets set` writes the env vars into the Fly machine without persisting them in the repo. **Replace the values below** with your real ones.

```bash
fly secrets set ConnectionStrings__DefaultConnection="postgresql://USER:PASS@ep-XXXX.eu-central-1.aws.neon.tech/bom_price_approval?sslmode=require"
fly secrets set Jwt__Key="<your-32+ char random JWT secret>"
fly secrets set Email__Username="<gmail address used for SMTP>"
fly secrets set Email__Password="<gmail app password>"
```

Generate a JWT key on Linux/Mac: `openssl rand -base64 48`. On Windows PowerShell: `[Convert]::ToBase64String((1..48 | ForEach-Object { [byte](Get-Random -Minimum 0 -Maximum 256) }))`.

### 1.3 First deploy

```bash
fly deploy
```

This builds the Docker image (multi-stage, takes ~3-5 min on first run) and ships it to Fly.

On success Fly prints the URL: `https://bom-fpf-api.fly.dev`. Verify:

```bash
curl https://bom-fpf-api.fly.dev/health
```

Should return `{"status":"ok"}`.

EF migrations apply automatically on app startup (`db.Database.Migrate()` in `Program.cs`).

### 1.4 Seed users

The first deploy auto-seeds the 5 canonical users (admin / Ali Sales / Bob BOM / Sara Accounts / Managing Director — see `Program.cs` seed block) **only if those users don't exist**. Since you've also been seeding eve@/frank@ for tests, those will be created too.

⚠ Update seed-user passwords for production by logging in once with the dev passwords (`Admin@1234`, `Test@1234`) then immediately changing them via the UI's `/change-password`.

---

## Step 2 — Web frontend deploy to Cloudflare Pages (~15 min)

### 2.1 Connect GitHub repo

1. Go to https://dash.cloudflare.com → Workers & Pages → Pages → **Connect to Git**.
2. Authorize Cloudflare to read `shannaqvi90-ux/bom-price`.
3. Pick the repo. Project name: `bom-fpf` (URL becomes `bom-fpf.pages.dev`).

### 2.2 Build settings

- **Framework preset:** None (Vite-aware preset overspecifies; we set our own)
- **Build command:** `cd bom-web && npm install && npm run build`
- **Build output directory:** `bom-web/dist`
- **Root directory (advanced):** leave blank (the build command `cd`s into bom-web)
- **Node version:** 20 (set as env var below)

### 2.3 Environment variables (production)

Pages → Settings → Environment variables → Production:

| Key | Value |
|---|---|
| `NODE_VERSION` | `20` |
| `VITE_API_BASE_URL` | `https://bom-fpf-api.fly.dev` (replace with your Fly app URL) |

`bom-web/.env.production` already pre-fills `VITE_API_BASE_URL` — Cloudflare's env var overrides it if you ever rename the Fly app.

### 2.4 First build

Click **Save and Deploy**. First build takes ~2-3 min. Subsequent builds happen automatically on every `git push` to master.

Verify by visiting `https://bom-fpf.pages.dev` and logging in as `admin@test.com / Admin@1234`.

### 2.5 Update CORS allowlist on Fly

Once the Pages domain is live, add it to the API CORS allowlist:

```bash
fly secrets set CorsAllowedOrigins="https://bom-fpf.pages.dev"
```

(Multiple comma-separated origins are fine.) Fly will redeploy with the updated env var.

---

## Step 3 — Mobile EAS preview rebuild (~15 min)

The mobile app is already wired to EAS preview channel. After the API URL is finalized, rebuild the preview APK so it points at production:

### 3.1 Update `eas.json` (already done in this commit)

`bom-mobile/eas.json` now has:

```json
"preview": {
  "env": { "EXPO_PUBLIC_API_BASE_URL": "https://bom-fpf-api.fly.dev" }
}
```

If your Fly app is at a different URL, edit this and commit.

### 3.2 Rebuild the APK

```bash
cd bom-mobile
eas build --profile preview --platform android
```

When complete, EAS gives you a download URL for the APK. Distribute via:
- Direct download link in WhatsApp / email
- QR code from the EAS build page
- Internal MDM if your company uses one

### 3.3 OTA updates (no rebuild needed for code changes)

For subsequent JavaScript-only changes:

```bash
cd bom-mobile
eas update --channel preview --message "<short release note>"
```

Apps already installed will pull the update next launch — no APK reinstall.

---

## Step 4 — Verify end-to-end (~15 min)

1. Open `https://bom-fpf.pages.dev` in a browser.
2. Login as `admin@test.com / Admin@1234`. **Immediately change the password.**
3. Same for `ali@`, `bob@`, `sara@`, `md@` if those will be used by real staff.
4. Create a customer, requisition, BOM, costing, approval — full happy path.
5. From a phone with the rebuilt APK installed: same login → verify SignalR notifications fire.
6. Trigger an Approve flow and confirm SP receives the email with PDF attachment.

---

## Subsequent deploys (~5 min)

After the first run, ongoing deploys are simple:

```bash
# Backend
fly deploy

# Web
git push origin master    # Cloudflare Pages auto-deploys

# Mobile (code-only changes)
cd bom-mobile && eas update --channel preview --message "fix: <short>"

# Mobile (native changes — RN config, Expo SDK, native modules)
cd bom-mobile && eas build --profile preview --platform android
```

---

## Backups (Neon)

Neon free tier doesn't include automated backups beyond the 7-day point-in-time-restore window. For longer retention:

```bash
# Run weekly via cron / Windows Task Scheduler from any machine with network access
PGPASSWORD="<password>" pg_dump \
  "postgresql://USER:PASS@ep-XXXX.eu-central-1.aws.neon.tech/bom_price_approval?sslmode=require" \
  > "bom_$(date +%Y%m%d).sql"

# Then upload to Cloudflare R2 (10 GB free tier) or any S3-compatible storage:
# rclone copy bom_$(date +%Y%m%d).sql r2:bom-backups/
```

A cron job is documented separately if you want automated backups.

---

## Rollback procedures

| What broke | How to revert |
|---|---|
| Bad API code | `fly releases list` → `fly releases rollback <version>` |
| Bad web build | Cloudflare Pages → Deployments → previous deployment → "Rollback to this deployment" |
| Bad mobile JS | `eas update --channel preview --message "rollback" --branch <previous>` |
| Bad migration | `fly ssh console` → `dotnet ef database update <PreviousMigrationName>` |
| DB corruption | Neon dashboard → Branches → restore from point-in-time snapshot (≤ 7 days) |

---

## Cost monitoring

Monthly check (5 min):
- Fly.io: dashboard → Usage tab. Free tier is 3 shared VMs + 160 GB egress. Shouldn't exceed.
- Neon: dashboard → Storage. 0.5 GB limit. Should be fine for this app.
- Cloudflare Pages: free tier is unlimited bandwidth. No worries.

If any tier limit is hit:
- Fly.io paid: $5/mo for 1 GB RAM dedicated machine
- Neon paid: $19/mo for 10 GB DB + always-on
- Cloudflare Pages paid: only if you exceed 500 builds/month — extremely unlikely
