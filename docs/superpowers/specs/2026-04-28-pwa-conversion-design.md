# PWA Conversion — `bom-web` to Installable Progressive Web App

**Date:** 2026-04-28
**Status:** Brainstorm complete; awaiting plan
**Driver:** Free iOS/iPadOS distribution path for Fujairah factory staff (no Apple Developer account required)

---

## Goal

Convert `bom-web` (React 19 + Vite + TS) into an installable PWA so Fujairah factory iOS / iPadOS staff can install `https://bom-fpf.pages.dev` from Safari → "Add to Home Screen" and get a native-app-like experience: home screen icon, fullscreen launch, offline read access, and background push notifications — at zero distribution cost.

**Why now:** Apple Developer account ($99/yr) deemed out of scope; user wants a free production path. PWA is the only viable iOS distribution method that doesn't require an Apple paid account.

**Out of scope (explicit):**
- Apple Developer account / native iOS app build
- Play Store / production AAB (memory: `feedback_no_play_store`)
- Mobile RN app changes (Android RN remains as-is)
- Offline-first writes (queue + sync)
- Background sync API (defer mutations)
- Periodic background sync (Apple-restricted, unreliable)
- Desktop install dialog customization
- Localization of install UX strings
- Telemetry on install rate (future polish)
- iPad-specific landscape multi-column layouts (future polish, separate item)

---

## Decisions locked during brainstorm

| # | Question | Decision |
|---|---|---|
| Q1 | Offline scope | **(b)** App shell + cached reads (NetworkFirst, 5s network timeout, 24h cache TTL, read-only offline) |
| Q2 | Web Push | **(b)** Enable — adds backend VAPID + subscription endpoints + NotificationService extension |
| Q3 | Branding | **(a)** Match RN mobile: name `FPF Quotations`, theme `#1e40af`, blue splash |
| Q4 | Icons | **(a)** Use `bom-mobile/assets/icon.png` as source — auto-generate all PWA sizes |
| Q5 | Install UX | **(c)** Aggressive fullscreen modal on first login (iOS / iPadOS Safari only); skippable |
| Q6 | SW updates | **(b)** Toast prompt — "Naya version available. [Refresh now] [Later]" |
| iPad | iPadOS support | Yes — same flow; requires iPad-aware `userAgent` detection (iPadOS 13+ disguises as Mac) |

---

## High-level architecture

### Three building blocks

```
┌───────────────────────────────────────────────────────────┐
│  1. PWA shell (manifest + icons + meta tags)              │
│     → installable on home screen, branded, offline-capable│
├───────────────────────────────────────────────────────────┤
│  2. Service worker (Workbox via vite-plugin-pwa)          │
│     → app shell precache + API GET runtime cache          │
│     → update toast plumbing                               │
├───────────────────────────────────────────────────────────┤
│  3. Web Push (frontend SW + backend endpoints)            │
│     → VAPID-signed background notifs (iOS/iPadOS 16.4+)   │
│     → augments existing SignalR (in-app real-time)        │
└───────────────────────────────────────────────────────────┘
```

### Stack additions

| Layer | Tool | Why |
|---|---|---|
| Build | `vite-plugin-pwa` (~9.x) | Officially maintained Vite PWA plugin, Workbox-backed |
| Service worker | Workbox (auto via plugin) | Battle-tested caching strategies |
| Icon generation | `@vite-pwa/assets-generator` | Single source PNG → all PWA sizes |
| Backend push | `WebPush` NuGet (~3M downloads) | Mature library; manual VAPID + AES-128-GCM is 200+ LOC and security-sensitive |

---

## Phase / PR breakdown

3 separate PRs, each independently shippable + reviewable. Order: PR 1 → PR 2 → PR 3.

| Phase | Branch | Scope | LOC est. | Time est. |
|---|---|---|---|---|
| **P1** | `feat/pwa-shell-and-offline` | Manifest + icons + SW + install modal + update toast + offline banner | ~600 web | 3-4 hr |
| **P2** | `feat/pwa-web-push-backend` | DB migration + 2 endpoints + NotificationService extension + VAPID keys | ~400 backend | 2-3 hr |
| **P3** | `feat/pwa-push-frontend` | SW push listener + permission flow + subscription management | ~200 web | 1-2 hr |
| **Total** | — | — | ~1200 LOC | 6-9 hr |

P1 is standalone-shippable (iOS staff install, get offline + update prompts; no push). P2+P3 land within same week.

---

## P1 — PWA Shell & Offline (Frontend)

### Files added/changed

**Web (~30 files):**
- `vite.config.ts` (add `VitePWA` plugin)
- `index.html` (meta tags + manifest link)
- `public/manifest.webmanifest` (new)
- `public/icons/*` (generated, ~10 PNG files)
- `public/icon-source.png` (copied from `bom-mobile/assets/icon.png`)
- `src/sw.ts` (custom service worker source — Workbox `injectManifest` mode)
- `src/components/pwa/InstallModal.tsx`
- `src/components/pwa/InstallBanner.tsx`
- `src/components/pwa/UpdateToast.tsx`
- `src/components/pwa/OfflineBanner.tsx`
- `src/hooks/usePwaInstall.ts`
- `src/hooks/useServiceWorker.ts`
- `src/utils/platform.ts`
- **Delete:** `public/favicon.svg` + `public/icons.svg` (boilerplate, never used by app)

### Manifest (`public/manifest.webmanifest`)

```json
{
  "name": "FPF Quotations",
  "short_name": "FPF Quotations",
  "description": "Fujairah Plastic Factory — BOM & Quotation Approval",
  "start_url": "/",
  "scope": "/",
  "display": "standalone",
  "orientation": "any",
  "theme_color": "#1e40af",
  "background_color": "#1e40af",
  "icons": [
    { "src": "/icons/icon-192.png", "sizes": "192x192", "type": "image/png" },
    { "src": "/icons/icon-512.png", "sizes": "512x512", "type": "image/png" },
    { "src": "/icons/icon-maskable-192.png", "sizes": "192x192", "type": "image/png", "purpose": "maskable" },
    { "src": "/icons/icon-maskable-512.png", "sizes": "512x512", "type": "image/png", "purpose": "maskable" },
    { "src": "/icons/apple-touch-icon-180.png", "sizes": "180x180", "type": "image/png" }
  ]
}
```

### `index.html` additions

```html
<link rel="manifest" href="/manifest.webmanifest" />
<meta name="theme-color" content="#1e40af" />
<meta name="apple-mobile-web-app-capable" content="yes" />
<meta name="apple-mobile-web-app-status-bar-style" content="black-translucent" />
<meta name="apple-mobile-web-app-title" content="FPF Quotations" />
<link rel="apple-touch-icon" href="/icons/apple-touch-icon-180.png" />
```

### Service worker — caching strategy

Mode: `injectManifest` (custom `src/sw.ts` — full control over Workbox routes).

| Resource | Strategy | Cache name | Notes |
|---|---|---|---|
| App shell (HTML/JS/CSS/fonts/icons) | Precache (build-time) | `bom-precache-v{rev}` | Version-pinned, swapped on SW activate |
| API GET — list endpoints | NetworkFirst (5s timeout, 24h TTL) | `bom-api-list-cache` | Patterns: `/api/requisitions`, `/api/customers`, `/api/items`, `/api/branches`, `/api/users`, `/api/groups` |
| API GET — detail endpoints | NetworkFirst (5s timeout, 24h TTL) | `bom-api-detail-cache` | Patterns: `/api/requisitions/{id}`, `/api/bom/...`, `/api/costing/...`, `/api/approvals/...` |
| `/api/notifications` | NetworkOnly | — | Real-time, never stale |
| `/api/auth/*` | NetworkOnly | — | Token-bearing, security-sensitive |
| Mutating requests (POST/PUT/PATCH/DELETE) | NetworkOnly | — | User sees error toast if offline |
| `/hubs/notifications` (SignalR WebSocket) | NetworkOnly (passthrough) | — | SW does not intercept WS upgrade |

```typescript
runtimeCaching: [
  {
    urlPattern: /\/api\/(requisitions|customers|items|branches|users|groups)$/,
    handler: "NetworkFirst",
    options: {
      cacheName: "bom-api-list-cache",
      expiration: { maxEntries: 100, maxAgeSeconds: 60 * 60 * 24 },
      networkTimeoutSeconds: 5,
      cacheableResponse: { statuses: [200] }
    }
  },
  // similar for detail cache (regex matches /id endpoints)
]
```

### Install UX

#### `InstallModal` (iOS / iPadOS Safari)

**When it shows:** post-login, `isIOSorIPadOS() && isSafari() && !isStandalone() && !dismissedRecently`.

**Content:**
- Title: "📱 Install FPF Quotations"
- Benefit list: home screen icon / notifications / faster offline
- Step-by-step (visual + text): 1) Tap Share button 2) Add to Home Screen 3) Tap Add
- Animated diagram of these steps (lightweight CSS animation, no GIF asset for P1)
- Single dismiss button: "I'll do it later"

**Dismiss state:**
- LocalStorage key `pwa-install-modal-dismissed = Date.now()`
- Re-show after 30 days OR manual click on profile-page Install link
- Permanent suppression once `isStandalone() === true`

#### `InstallBanner` (Android Chrome)

**When it shows:** `isAndroidChrome() && !isStandalone() && beforeinstallprompt event captured`.

**Content:** small dismissible banner top-right of dashboard: "Install FPF Quotations [Install] [×]"

**Click "Install":** trigger stored `beforeinstallprompt.prompt()` → native Android dialog → done.

#### Profile/settings card (permanent access)

`src/features/auth/ProfilePage.tsx` (or equivalent) — new "App Settings" card:

- "Install on this device" → button shows "Install Now" or "Already installed"
- "Notifications" → enabled/disabled state + iOS Settings deep link

### Update flow (toast pattern)

```
Background: SW detects new build
  ↓ (Workbox lifecycle)
SW reaches 'waiting' state
  ↓
useServiceWorker() hook → setUpdateAvailable(true)
  ↓
<UpdateToast> renders via Sonner: "✨ Naya version available. [Refresh now] [Later]"
  ↓
User clicks Refresh → postMessage('SKIP_WAITING') → controllerchange → window.location.reload()
```

### Offline UX

`<OfflineBanner>`:
- Trigger: `navigator.onLine === false` event
- Content: "🔴 You're offline — showing last cached data. New requisitions will fail until reconnected."
- Position: top of viewport, persistent until reconnection
- Auto-hide on `online` event

### Platform detection (`utils/platform.ts`)

```typescript
export const isIOSorIPadOS = (): boolean => {
  // Classic iPhone/iPod and pre-iPadOS-13 iPads
  if (/iPad|iPhone|iPod/.test(navigator.userAgent)) return true;
  // iPadOS 13+ masquerades as Mac — distinguish via touch
  return /Macintosh/.test(navigator.userAgent) && navigator.maxTouchPoints > 1;
};

export const isSafari = (): boolean =>
  /Safari/.test(navigator.userAgent) && !/Chrome|CriOS|FxiOS|EdgiOS/.test(navigator.userAgent);

export const isStandalone = (): boolean =>
  window.matchMedia("(display-mode: standalone)").matches ||
  (navigator as any).standalone === true; // iOS/iPadOS Safari quirk

export const isAndroidChrome = (): boolean =>
  /Android/.test(navigator.userAgent) && /Chrome/.test(navigator.userAgent);
```

### Logout cache clear (security)

Existing logout flow (`useAuthStore.logout()`) extended:

```typescript
async function logout() {
  // existing: clear tokens + redirect
  // NEW: clear API caches so next user can't see prior user's data
  if ("caches" in window) {
    await caches.delete("bom-api-list-cache");
    await caches.delete("bom-api-detail-cache");
  }
}
```

### Disable SW in dev

`vite-plugin-pwa` `devOptions.enabled = false` (default). Local `npm run dev` does not register SW — debugging clean. Production build only.

### P1 success criteria

- ✅ Lighthouse PWA audit ≥ 90 on production deploy
- ✅ iPhone + iPad smoke: install via Safari Add-to-Home-Screen, app icon shows FPF logo, fullscreen launch works
- ✅ Offline test: install → DevTools throttle to offline → reload → cached requisitions list visible
- ✅ Update test: deploy 2nd build → existing user sees toast → click refresh → new version loads
- ✅ Existing 263 web vitest tests still pass + ~5-10 new vitest tests for `InstallModal` / `useServiceWorker` / `platform.ts`
- ✅ No runtime errors in console for 24h post-deploy
- ✅ Boilerplate `favicon.svg` + `icons.svg` deleted

---

## P2 — Web Push Backend

### VAPID keys

- Generate once via `node web-push generate-vapid-keys` (or .NET equivalent)
- 2 keys: public (frontend) + private (backend)
- Subject: `mailto:shan@fujairahplastic.com`
- **Storage:** `dotnet user-secrets` (dev) + `fly secrets set` (prod)

```json
// appsettings.json (committed; values empty)
{
  "WebPush": {
    "VapidPublicKey": "",
    "VapidPrivateKey": "",
    "Subject": "mailto:shan@fujairahplastic.com"
  }
}
```

### Schema — `PushSubscription` table

```csharp
public class PushSubscription
{
    public int Id { get; set; }
    public int UserId { get; set; }                  // FK Users (Cascade delete)
    public string Endpoint { get; set; } = "";       // Apple/Google push URL — UNIQUE
    public string P256dh { get; set; } = "";         // Public key for encryption
    public string Auth { get; set; } = "";           // Auth secret
    public string? UserAgent { get; set; }           // For debugging
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastUsedAt { get; set; }        // Updated on successful push send

    public User? User { get; set; }
}
```

**Indexes:**
- `(UserId)` — fan-out lookup
- `(Endpoint)` UNIQUE — re-subscribe is upsert

**Migration:** `2026XXXX_AddPushSubscription.cs` (auto-generated via `dotnet ef migrations add`)

### `PushSubscriptionsController`

Route prefix: `/api/notifications`. All endpoints require `[Authorize]`.

#### `POST /api/notifications/push-subscribe`

```json
{
  "endpoint": "https://web.push.apple.com/Q...",
  "keys": { "p256dh": "BJa...", "auth": "Xy..." },
  "userAgent": "Mozilla/5.0 (iPhone; ...)"
}
```

Behavior:
- Upsert by `Endpoint`
- Set `UserId = JWT.UserId`
- Returns `204 No Content`
- Validation: endpoint required, p256dh + auth required

#### `DELETE /api/notifications/push-subscribe`

```json
{ "endpoint": "https://web.push.apple.com/Q..." }
```

Behavior:
- Delete row matching `(UserId, Endpoint)` (own-only — can't delete another user's sub)
- Returns `204 No Content` (idempotent — non-existent → 204)
- Called on logout + when user disables notifications in settings

### `WebPushService` (new)

Wraps `WebPush` NuGet:

```csharp
public class WebPushService
{
    private readonly WebPushClient _client;
    private readonly VapidDetails _vapid;
    private readonly ILogger<WebPushService> _logger;

    public WebPushService(IConfiguration cfg, ILogger<WebPushService> logger) { ... }

    public async Task SendAsync(PushSubscription sub, string title, string body)
    {
        var pushSubscription = new WebPush.PushSubscription(sub.Endpoint, sub.P256dh, sub.Auth);
        var payload = JsonSerializer.Serialize(new { title, body });
        await _client.SendNotificationAsync(pushSubscription, payload, _vapid);
    }
}
```

DI registered in `Program.cs` as singleton (stateless, holds VAPID config).

### `NotificationService` extension

Current behavior preserved (DB row + SignalR). Add fan-out to web push:

```csharp
public async Task SendAsync(int userId, NotificationType type, string title, string message, ...)
{
    // 1. Insert Notification row to DB (existing)
    // 2. SignalR push to user group (existing)
    // 3. NEW: Web Push fan-out
    var subs = await _db.PushSubscriptions.Where(s => s.UserId == userId).ToListAsync();
    foreach (var sub in subs)
    {
        try
        {
            await _webPushService.SendAsync(sub, title, message);
            sub.LastUsedAt = DateTime.UtcNow;
        }
        catch (WebPushException ex) when (ex.StatusCode == HttpStatusCode.Gone || ex.StatusCode == HttpStatusCode.NotFound)
        {
            _db.PushSubscriptions.Remove(sub);  // 410 Gone — auto-cleanup
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Web push failed for sub {SubId}", sub.Id);
            // Swallow — SignalR + DB already succeeded
        }
    }
    await _db.SaveChangesAsync();
}
```

Same pattern in `SendToUsersAsync` (multi-user fan-out — already R1-optimized in V23c P2).

### Resilience invariant

**Web push failure NEVER breaks SignalR + DB notification flow. It is purely additive.**

| Failure | Behavior |
|---|---|
| VAPID keys missing/invalid at startup | Log warning, web push **disabled** for run; SignalR + DB still work |
| Push service returns 410 Gone | Auto-delete row from DB |
| Push service timeout/500 | Log warning, swallow |
| User has no subscription | Skip silently |

### P2 success criteria

- ✅ `PushSubscription` migration applied cleanly
- ✅ Backend tests: `PushSubscriptionsControllerTests` (~10 cases — POST upsert, DELETE idempotent, 401 unauth, validation errors)
- ✅ Backend tests: `NotificationServiceWebPushTests` (~5 cases — fan-out, 410 cleanup, error swallow, no-subs no-op)
- ✅ Existing 318 backend tests still pass
- ✅ VAPID keys set in user-secrets (dev) + fly secrets (prod)
- ✅ Manual smoke: hit `POST /api/notifications/push-subscribe` from Postman with valid mock subscription → 204 + row in DB

---

## P3 — Web Push Frontend

### Files added/changed

- `src/sw.ts` (extend with `push` event listener)
- `src/components/pwa/NotificationPermissionPrompt.tsx`
- `src/features/notifications/usePushSubscription.ts` (new hook)
- `src/features/auth/ProfilePage.tsx` (add notifications card)
- `src/api/pushSubscriptions.ts` (axios client for POST/DELETE)
- `src/store/auth.ts` (logout extension to call `unsubscribe`)

### `usePushSubscription` hook

Lifecycle:
- Reads `Notification.permission`
- Reads VAPID public key (from a config endpoint or Vite env var `VITE_VAPID_PUBLIC_KEY`)
- `subscribe()`: call `Notification.requestPermission()` → if granted, register PushManager subscription → POST to backend
- `unsubscribe()`: PushManager.unsubscribe() → DELETE to backend
- Returns: `{ permission, subscribed, subscribe, unsubscribe }`

### `NotificationPermissionPrompt`

**When it shows:**
- `isStandalone() === true` (only post-install)
- `Notification.permission === "default"` (not yet asked)
- User logs in (in-app context, not on launch)
- LocalStorage `push-prompt-dismissed-at` not set within last 14 days

**Content:** Sonner toast (NOT modal — softer):
```
🔔 Get notified when reqs need you?
[Yes, enable]   [Not now]
```

**Click "Yes, enable":**
1. Call `subscribe()` from `usePushSubscription`
2. Browser shows native permission prompt
3. If granted → POST subscription → success toast: "🔔 Notifications enabled"
4. If denied → toast: "Disabled. Enable later from iOS Settings"

**Click "Not now":** localStorage timestamp; don't re-ask for 14 days.

### Service worker `push` event listener (in `src/sw.ts`)

```typescript
self.addEventListener("push", (event: PushEvent) => {
  const data = event.data?.json() ?? { title: "FPF Quotations", body: "You have a new notification" };
  event.waitUntil(
    self.registration.showNotification(data.title, {
      body: data.body,
      icon: "/icons/icon-192.png",
      badge: "/icons/icon-192.png",
      tag: "bom-notification"  // collapse multiple notifs into one
    })
  );
});

self.addEventListener("notificationclick", (event: NotificationEvent) => {
  event.notification.close();
  event.waitUntil(self.clients.openWindow("/"));
});
```

### Logout extension

```typescript
// src/store/auth.ts
async function logout() {
  // NEW: unsubscribe push before clearing tokens (need auth header for DELETE)
  await pushSubscriptions.unsubscribe().catch(() => {}); // best-effort
  // existing: clear caches, clear tokens, redirect
}
```

### Notification content guideline (security)

- **Title:** `"FPF Quotations"` (generic, no entity data)
- **Body:** `"You have a new approval request"` / `"BOM ready for costing"` / `"Quotation approved"` (generic action, no customer/price)
- User taps notification → opens app → sees details inside authenticated session
- **Same pattern as banking apps** — protects sensitive data on lock screen

### iOS / iPadOS critical detail

If user denies permission, `Notification.permission === "denied"` is permanent until manual re-enable in iOS/iPadOS Settings → [App] → Notifications. We surface this in profile page with deep-link guidance: "Notifications disabled — open iOS Settings to re-enable".

### P3 success criteria

- ✅ All P1 + P2 criteria still pass
- ✅ Vitest unit: `usePushSubscription` lifecycle (mock PushManager)
- ✅ Vitest component: `NotificationPermissionPrompt` shows/hides correctly per state
- ✅ Manual smoke (real iPhone OR iPad on iPadOS 16.4+):
  1. Install PWA via Safari Add-to-Home-Screen
  2. Re-launch from icon (standalone mode)
  3. Login → permission prompt appears
  4. Grant permission → backend row created
  5. Trigger notification (e.g., MD approves a req) → notification appears on lock screen
  6. Tap notification → opens PWA → routed to relevant page
- ✅ Subscription auto-cleanup verified: revoke browser permission → next push → 410 Gone → row deleted from DB

---

## Security considerations

### 1. Auth tokens in cached responses

**Risk:** Service worker caches API responses → if cache contains responses with sensitive data, another user on same physical device could see prior user's data.

**Mitigations:**
- `/api/auth/*` is `NetworkOnly` — never cached
- 401 responses never cached (Workbox default)
- **Logout flow** clears `bom-api-list-cache` and `bom-api-detail-cache` before clearing tokens

### 2. VAPID keys leak

**Risk:** Private key leaked → attacker can send push notifs from your origin.

**Mitigations:**
- Stored in user-secrets (dev) and Fly secrets (prod) only — never in git
- Subject `mailto:` is corporate (`shan@fujairahplastic.com`), not personal
- Recovery: regenerate keys → all existing subscriptions invalidate naturally on next push (410 Gone)

### 3. Push subscription endpoints contain user-routable tokens

**Risk:** `Endpoint` URL is essentially a user-routable address.

**Mitigations:**
- `PushSubscription.UserId` is FK + required — no orphan subs
- DELETE endpoint validates `UserId == JWT.UserId` (can't delete another user's sub)
- HTTPS only (Cloudflare Pages + Fly enforce)

### 4. Service worker scope hijack

**Risk:** Malicious script registers wider-scope SW.

**Mitigations:**
- SW scope = `/` (entire app)
- Only one SW source (`src/sw.ts`)
- Cloudflare Pages serves correct `Service-Worker-Allowed` header (default — no extra config)

### 5. Notification content leak on lock screen

**Risk:** Push notification body shown on lock screen — sensitive info visible to anyone holding phone.

**Mitigations:** Generic notification content (see P3 Notification content guideline above).

---

## Risks & unknowns

| Risk | Likelihood | Mitigation |
|---|---|---|
| iOS Safari PWA push silently fails on iOS / iPadOS < 16.4 | Medium | Detect version → show "Update OS to 16.4+ for notifications" banner |
| Staff installs from Chrome iOS instead of Safari → push doesn't work | High | InstallModal explicitly shows only for Safari; warn in modal if user is on Chrome iOS |
| Apple deprecates PWA features in future iOS / iPadOS | Low-Med | No mitigation — accept platform risk; fall back to in-app SignalR if push dies |
| Cloudflare Pages caching interferes with SW updates | Low | `no-cache` header on `manifest.webmanifest` and `sw.js` |
| Service worker breaks during deploy → users see stale forever | Low-Med | "Refresh app" button in profile manually clears SW + caches |
| `WebPush` NuGet incompatibility with .NET 8 | Low | Verified .NET 6+ support before commit |
| iPad in Mac-disguised user-agent breaks `isIOSorIPadOS` | Low | Touch-points heuristic covers it (verified pattern) |

---

## Test strategy summary

| Phase | Test type | What's tested |
|---|---|---|
| P1 | Vitest unit | `platform.ts` helpers (mocked userAgent), `usePwaInstall` state transitions |
| P1 | Vitest component | `InstallModal` show/hide logic, `UpdateToast` reacts to update state |
| P1 | Manual | Lighthouse PWA audit ≥ 90; iPhone + iPad install + offline + update flows |
| P2 | xUnit integration | `PushSubscriptionsController` POST/DELETE/upsert/idempotent/401 |
| P2 | xUnit unit | `NotificationService` web push fan-out: 410 cleanup, error swallow |
| P3 | Vitest unit | `usePushSubscription` lifecycle (mock PushManager) |
| P3 | Vitest component | `NotificationPermissionPrompt` show/hide |
| P3 | Manual | Real-device end-to-end: install → grant → trigger → lock-screen notif |

**No SW unit tests** — Workbox internals tested upstream; custom logic best validated via integration smoke.

---

## Rollback plan

- **P1:** Revert PR. PWA users return to regular web app. Optionally ship a 1-line "kill switch" SW that unregisters itself.
- **P2:** Revert migration to drop `PushSubscription` table. NotificationService extension is additive — no callers break.
- **P3:** Revert PR. Frontend stops asking for permission; existing subscriptions become inert (backend keeps them but no triggers fire).

---

## Open items for plan phase

(none — all design decisions are locked)

---

## Memory hooks for future sessions

This spec should be referenced from:
- `project_pwa_conversion.md` (new memory) — phases shipped + URLs + rollback notes
- After PR 1 ships: update `project_session_2026_04_28_full_cleanup.md` "pending" list to remove iOS app placeholder + add "PWA shipped"
