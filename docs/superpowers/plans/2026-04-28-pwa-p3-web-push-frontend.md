# PWA Phase 3 — Web Push Frontend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Status:** Draft, awaiting plan approval before execute.
**Spec:** `docs/superpowers/specs/2026-04-28-pwa-conversion-design.md` (P3 section)
**Branch:** `feat/pwa-push-frontend` (off `master @ {P2_MERGE_SHA}`)
**Goal:** Wire frontend to backend Web Push: subscription registration, permission prompt UX, service worker push event listener, logout cleanup.
**Architecture:** New `usePushSubscription` hook owns the lifecycle (browser PushManager + backend POST/DELETE). New `NotificationPermissionPrompt` component shows Sonner toast post-install when permission is `default`. Service worker (`src/sw.ts` from P1) extended with `push` and `notificationclick` listeners. Logout extends to call unsubscribe before token clear.
**Tech Stack:** React 19, TypeScript, axios, Sonner, browser PushManager API, Workbox.
**Total tasks:** 6. **Estimated:** 1-2 hr.

**Hard dependency:** P2 must be deployed to production AND the backend's VAPID public key must be exposed to the frontend (via Vite env var `VITE_VAPID_PUBLIC_KEY` — set in Cloudflare Pages env config).

---

## Phase ordering rationale

API client first → hook depends on it → component depends on hook → SW push listener parallel-safe → wiring last → real-device smoke.

```
F1 axios client → U1 hook → U2 component → S1 SW listener → W1 wiring → V1 verify → C1 close
```

---

## Tasks

### Foundation (F1)

| # | Task | Outputs |
|---|---|---|
| **F1** | Add `src/api/pushSubscriptions.ts` — typed axios client for `POST /api/notifications/push-subscribe` and `DELETE /api/notifications/push-subscribe`. Vitest with mock axios. | 1 client, 1 test |

#### F1 detailed steps

- [ ] **Step 1: Add Vite env var**

Edit `bom-web/.env.example` (or create) and add:
```
VITE_VAPID_PUBLIC_KEY=
```

For local dev, set in `.env.local` (gitignored):
```
VITE_VAPID_PUBLIC_KEY=BJ...
```

(Same value as backend's `WebPush:VapidPublicKey`.)

For production, set in Cloudflare Pages env config: `VITE_VAPID_PUBLIC_KEY=<prod-vapid-public>`.

- [ ] **Step 2: Create `src/api/pushSubscriptions.ts`**

```typescript
import api from "./axios";

export interface PushSubscribePayload {
  endpoint: string;
  keys: { p256dh: string; auth: string };
  userAgent?: string;
}

export const pushSubscriptions = {
  subscribe: async (payload: PushSubscribePayload): Promise<void> => {
    await api.post("/api/notifications/push-subscribe", payload);
  },
  unsubscribe: async (endpoint: string): Promise<void> => {
    await api.delete("/api/notifications/push-subscribe", { data: { endpoint } });
  },
};
```

- [ ] **Step 3: Write test**

`bom-web/src/api/pushSubscriptions.test.ts`:

```typescript
import { describe, it, expect, vi } from "vitest";
import { pushSubscriptions } from "./pushSubscriptions";
import api from "./axios";

vi.mock("./axios", () => ({
  default: { post: vi.fn(), delete: vi.fn() },
}));

describe("pushSubscriptions", () => {
  it("subscribe POSTs payload to correct endpoint", async () => {
    const post = vi.mocked(api.post).mockResolvedValue({ data: null });
    await pushSubscriptions.subscribe({
      endpoint: "https://x",
      keys: { p256dh: "p", auth: "a" },
    });
    expect(post).toHaveBeenCalledWith("/api/notifications/push-subscribe", {
      endpoint: "https://x",
      keys: { p256dh: "p", auth: "a" },
    });
  });

  it("unsubscribe DELETEs with endpoint in body", async () => {
    const del = vi.mocked(api.delete).mockResolvedValue({ data: null });
    await pushSubscriptions.unsubscribe("https://x");
    expect(del).toHaveBeenCalledWith("/api/notifications/push-subscribe", {
      data: { endpoint: "https://x" },
    });
  });
});
```

- [ ] **Step 4: Run test**

```bash
cd bom-web && npm test -- pushSubscriptions
```

Expected: 2 pass.

- [ ] **Step 5: Commit**

```bash
git add bom-web/.env.example bom-web/src/api/pushSubscriptions.ts bom-web/src/api/pushSubscriptions.test.ts
git commit -m "feat(web): add push subscription API client + VAPID env var"
```

---

### Hook (U1)

| # | Task | Outputs |
|---|---|---|
| **U1** | `src/features/notifications/usePushSubscription.ts` — exposes `{ permission, isSubscribed, subscribe(), unsubscribe() }`. Uses browser PushManager + VAPID public key conversion. Vitest tests cover state transitions. | 1 hook, 1 test |

#### U1 detailed steps

- [ ] **Step 1: Add VAPID key utility**

`bom-web/src/utils/vapid.ts`:

```typescript
// Convert URL-safe base64 VAPID public key to Uint8Array (PushManager.subscribe requirement)
export function urlBase64ToUint8Array(base64String: string): Uint8Array {
  const padding = "=".repeat((4 - (base64String.length % 4)) % 4);
  const base64 = (base64String + padding).replace(/-/g, "+").replace(/_/g, "/");
  const rawData = atob(base64);
  return new Uint8Array([...rawData].map((c) => c.charCodeAt(0)));
}
```

- [ ] **Step 2: Write `usePushSubscription` hook**

`bom-web/src/features/notifications/usePushSubscription.ts`:

```typescript
import { useEffect, useState, useCallback } from "react";
import { pushSubscriptions } from "@/api/pushSubscriptions";
import { urlBase64ToUint8Array } from "@/utils/vapid";

const VAPID_KEY = import.meta.env.VITE_VAPID_PUBLIC_KEY as string | undefined;

interface UsePushSubscription {
  permission: NotificationPermission;
  isSubscribed: boolean;
  subscribe: () => Promise<void>;
  unsubscribe: () => Promise<void>;
}

export function usePushSubscription(): UsePushSubscription {
  const [permission, setPermission] = useState<NotificationPermission>(
    "Notification" in window ? Notification.permission : "default"
  );
  const [isSubscribed, setIsSubscribed] = useState(false);

  useEffect(() => {
    if (!("serviceWorker" in navigator)) return;
    navigator.serviceWorker.ready.then(async (reg) => {
      const sub = await reg.pushManager.getSubscription();
      setIsSubscribed(sub !== null);
    });
  }, []);

  const subscribe = useCallback(async () => {
    if (!VAPID_KEY) {
      console.warn("VITE_VAPID_PUBLIC_KEY not configured — cannot subscribe to push");
      return;
    }
    const result = await Notification.requestPermission();
    setPermission(result);
    if (result !== "granted") return;

    const reg = await navigator.serviceWorker.ready;
    const existing = await reg.pushManager.getSubscription();
    const sub = existing ?? await reg.pushManager.subscribe({
      userVisibleOnly: true,
      applicationServerKey: urlBase64ToUint8Array(VAPID_KEY),
    });

    const json = sub.toJSON();
    await pushSubscriptions.subscribe({
      endpoint: json.endpoint!,
      keys: { p256dh: json.keys!.p256dh, auth: json.keys!.auth },
      userAgent: navigator.userAgent,
    });
    setIsSubscribed(true);
  }, []);

  const unsubscribe = useCallback(async () => {
    if (!("serviceWorker" in navigator)) return;
    const reg = await navigator.serviceWorker.ready;
    const sub = await reg.pushManager.getSubscription();
    if (!sub) {
      setIsSubscribed(false);
      return;
    }
    await pushSubscriptions.unsubscribe(sub.endpoint).catch(() => {}); // best-effort
    await sub.unsubscribe();
    setIsSubscribed(false);
  }, []);

  return { permission, isSubscribed, subscribe, unsubscribe };
}
```

- [ ] **Step 3: Write test**

`bom-web/src/features/notifications/usePushSubscription.test.tsx`:

```typescript
import { describe, it, expect, vi, beforeEach } from "vitest";
import { renderHook, act, waitFor } from "@testing-library/react";
import { usePushSubscription } from "./usePushSubscription";
import { pushSubscriptions } from "@/api/pushSubscriptions";

vi.mock("@/api/pushSubscriptions", () => ({
  pushSubscriptions: { subscribe: vi.fn(), unsubscribe: vi.fn() },
}));

beforeEach(() => {
  vi.clearAllMocks();
  Object.defineProperty(window, "Notification", {
    value: Object.assign(
      vi.fn(),
      { permission: "default", requestPermission: vi.fn().mockResolvedValue("granted") }
    ),
    configurable: true,
  });
  const fakeSub = {
    endpoint: "https://x",
    toJSON: () => ({ endpoint: "https://x", keys: { p256dh: "p", auth: "a" } }),
    unsubscribe: vi.fn().mockResolvedValue(true),
  };
  Object.defineProperty(navigator, "serviceWorker", {
    value: {
      ready: Promise.resolve({
        pushManager: {
          getSubscription: vi.fn().mockResolvedValue(null),
          subscribe: vi.fn().mockResolvedValue(fakeSub),
        },
      }),
    },
    configurable: true,
  });
});

describe("usePushSubscription", () => {
  it("subscribe() requests permission, subscribes to PushManager, POSTs to backend", async () => {
    const { result } = renderHook(() => usePushSubscription());
    await act(async () => {
      await result.current.subscribe();
    });
    expect(Notification.requestPermission).toHaveBeenCalled();
    expect(pushSubscriptions.subscribe).toHaveBeenCalledWith(
      expect.objectContaining({
        endpoint: "https://x",
        keys: { p256dh: "p", auth: "a" },
      })
    );
    expect(result.current.isSubscribed).toBe(true);
  });

  it("subscribe() bails if permission denied", async () => {
    vi.mocked(Notification.requestPermission).mockResolvedValueOnce("denied");
    const { result } = renderHook(() => usePushSubscription());
    await act(async () => {
      await result.current.subscribe();
    });
    expect(pushSubscriptions.subscribe).not.toHaveBeenCalled();
    expect(result.current.isSubscribed).toBe(false);
  });
});
```

- [ ] **Step 4: Run tests**

```bash
npm test -- usePushSubscription
```

- [ ] **Step 5: Commit**

```bash
git add bom-web/src/utils/vapid.ts bom-web/src/features/notifications/usePushSubscription.ts bom-web/src/features/notifications/usePushSubscription.test.tsx
git commit -m "feat(web): add usePushSubscription hook + VAPID base64 util"
```

---

### Component (U2)

| # | Task | Outputs |
|---|---|---|
| **U2** | `src/components/pwa/NotificationPermissionPrompt.tsx` — shows Sonner toast on first login post-install when `permission === "default"`. 14-day localStorage suppression on dismiss. Vitest tests cover trigger conditions. | 1 component, 1 test |

#### U2 detailed steps

- [ ] **Step 1: Write component**

`bom-web/src/components/pwa/NotificationPermissionPrompt.tsx`:

```tsx
import { useEffect } from "react";
import { toast } from "sonner";
import { isStandalone } from "@/utils/platform";
import { usePushSubscription } from "@/features/notifications/usePushSubscription";

const SUPPRESS_KEY = "push-prompt-dismissed-at";
const SUPPRESS_TTL_MS = 14 * 24 * 60 * 60 * 1000;

export function NotificationPermissionPrompt() {
  const { permission, subscribe } = usePushSubscription();

  useEffect(() => {
    if (!isStandalone()) return;
    if (permission !== "default") return;
    const dismissedAt = Number(localStorage.getItem(SUPPRESS_KEY) ?? 0);
    if (Date.now() - dismissedAt < SUPPRESS_TTL_MS) return;

    const id = toast("🔔 Get notified when reqs need you?", {
      description: "Enable to receive approval requests + status updates.",
      duration: Infinity,
      action: {
        label: "Enable",
        onClick: async () => {
          await subscribe();
          toast.dismiss(id);
        },
      },
      cancel: {
        label: "Not now",
        onClick: () => {
          localStorage.setItem(SUPPRESS_KEY, String(Date.now()));
        },
      },
    });

    return () => toast.dismiss(id);
  }, [permission, subscribe]);

  return null;
}
```

- [ ] **Step 2: Write test**

`bom-web/src/components/pwa/NotificationPermissionPrompt.test.tsx`:

```tsx
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render } from "@testing-library/react";
import { NotificationPermissionPrompt } from "./NotificationPermissionPrompt";
import { toast } from "sonner";

vi.mock("sonner", () => ({ toast: Object.assign(vi.fn(), { dismiss: vi.fn() }) }));
vi.mock("@/features/notifications/usePushSubscription", () => ({
  usePushSubscription: vi.fn(),
}));
vi.mock("@/utils/platform", () => ({ isStandalone: vi.fn() }));

import { usePushSubscription } from "@/features/notifications/usePushSubscription";
import { isStandalone } from "@/utils/platform";

beforeEach(() => {
  localStorage.clear();
  vi.clearAllMocks();
});

describe("NotificationPermissionPrompt", () => {
  it("shows toast when standalone + permission default + not dismissed", () => {
    vi.mocked(isStandalone).mockReturnValue(true);
    vi.mocked(usePushSubscription).mockReturnValue({
      permission: "default",
      isSubscribed: false,
      subscribe: vi.fn(),
      unsubscribe: vi.fn(),
    });
    render(<NotificationPermissionPrompt />);
    expect(toast).toHaveBeenCalled();
  });

  it("does NOT show toast when not standalone", () => {
    vi.mocked(isStandalone).mockReturnValue(false);
    vi.mocked(usePushSubscription).mockReturnValue({
      permission: "default",
      isSubscribed: false,
      subscribe: vi.fn(),
      unsubscribe: vi.fn(),
    });
    render(<NotificationPermissionPrompt />);
    expect(toast).not.toHaveBeenCalled();
  });

  it("does NOT show toast when permission already granted", () => {
    vi.mocked(isStandalone).mockReturnValue(true);
    vi.mocked(usePushSubscription).mockReturnValue({
      permission: "granted",
      isSubscribed: true,
      subscribe: vi.fn(),
      unsubscribe: vi.fn(),
    });
    render(<NotificationPermissionPrompt />);
    expect(toast).not.toHaveBeenCalled();
  });

  it("does NOT show toast when dismissed within 14 days", () => {
    vi.mocked(isStandalone).mockReturnValue(true);
    vi.mocked(usePushSubscription).mockReturnValue({
      permission: "default",
      isSubscribed: false,
      subscribe: vi.fn(),
      unsubscribe: vi.fn(),
    });
    localStorage.setItem("push-prompt-dismissed-at", String(Date.now() - 7 * 24 * 60 * 60 * 1000));
    render(<NotificationPermissionPrompt />);
    expect(toast).not.toHaveBeenCalled();
  });
});
```

- [ ] **Step 3: Run tests**

```bash
npm test -- NotificationPermissionPrompt
```

Expected: 4 pass.

- [ ] **Step 4: Commit**

```bash
git add bom-web/src/components/pwa/NotificationPermissionPrompt.tsx bom-web/src/components/pwa/NotificationPermissionPrompt.test.tsx
git commit -m "feat(web): add notification permission prompt (post-install, 14-day suppression)"
```

---

### Service Worker push listener (S1)

| # | Task | Outputs |
|---|---|---|
| **S1** | Extend `src/sw.ts` (from P1) with `push` event listener that calls `showNotification` and `notificationclick` listener that opens app. | 1 sw.ts edit |

#### S1 detailed steps

- [ ] **Step 1: Append to `src/sw.ts`**

Below the existing route registrations + `message` listener (skipWaiting), append:

```typescript
self.addEventListener("push", (event: PushEvent) => {
  let title = "FPF Quotations";
  let body = "You have a new notification";
  if (event.data) {
    try {
      const data = event.data.json();
      title = data.title ?? title;
      body = data.body ?? body;
    } catch {
      body = event.data.text();
    }
  }
  event.waitUntil(
    self.registration.showNotification(title, {
      body,
      icon: "/pwa-192x192.png",
      badge: "/pwa-192x192.png",
      tag: "bom-notification",
    })
  );
});

self.addEventListener("notificationclick", (event: NotificationEvent) => {
  event.notification.close();
  event.waitUntil(
    self.clients.matchAll({ type: "window" }).then((clients) => {
      for (const client of clients) {
        if (client.url && "focus" in client) return client.focus();
      }
      if (self.clients.openWindow) return self.clients.openWindow("/");
    })
  );
});
```

- [ ] **Step 2: Build to verify SW emits**

```bash
cd bom-web && npm run build
```

Expected: build succeeds; `dist/sw.js` includes the push listener (grep for `addEventListener('push'`).

- [ ] **Step 3: Commit**

```bash
git add bom-web/src/sw.ts
git commit -m "feat(web): add SW push event + notification click listeners"
```

---

### Wiring (W1)

| # | Task | Outputs |
|---|---|---|
| **W1** | Mount `<NotificationPermissionPrompt>` in `App.tsx`. Extend logout in `src/store/auth.ts` (or wherever) to call `pushSubscriptions.unsubscribe(currentEndpoint)` before clearing tokens. Extend ProfilePage "App Settings" card with notifications row showing `permission` state + iOS settings hint. | 1 App.tsx edit, 1 auth store edit, 1 profile page edit |

#### W1 detailed steps

- [ ] **Step 1: Mount component in App.tsx**

Near other PWA components mounted in P1:

```tsx
import { NotificationPermissionPrompt } from "@/components/pwa/NotificationPermissionPrompt";

// inside main return:
<NotificationPermissionPrompt />
```

- [ ] **Step 2: Extend logout**

Find existing logout in `src/store/auth.ts`. Before clearing tokens:

```typescript
async function unsubscribeFromPush() {
  if (!("serviceWorker" in navigator)) return;
  try {
    const reg = await navigator.serviceWorker.ready;
    const sub = await reg.pushManager.getSubscription();
    if (sub) {
      await pushSubscriptions.unsubscribe(sub.endpoint).catch(() => {});
      await sub.unsubscribe();
    }
  } catch (e) {
    console.warn("Push unsubscribe failed during logout", e);
  }
}

async function logout() {
  await unsubscribeFromPush();
  await clearPwaCaches(); // existing from P1
  // existing token clear + redirect
}
```

- [ ] **Step 3: Extend ProfilePage App Settings card**

Add row to existing `<AppSettingsCard>` from P1:

```tsx
import { usePushSubscription } from "@/features/notifications/usePushSubscription";

function NotificationsRow() {
  const { permission, isSubscribed, subscribe, unsubscribe } = usePushSubscription();
  return (
    <div className="flex items-center justify-between">
      <span>Notifications</span>
      {permission === "denied" ? (
        <span className="text-sm text-orange-600">
          Disabled — open iOS Settings → FPF Quotations → Notifications to re-enable
        </span>
      ) : isSubscribed ? (
        <button onClick={unsubscribe} className="rounded border px-3 py-1 text-sm">
          Disable
        </button>
      ) : (
        <button onClick={subscribe} className="rounded bg-blue-700 px-3 py-1 text-sm text-white">
          Enable
        </button>
      )}
    </div>
  );
}
```

Insert `<NotificationsRow />` into the existing `AppSettingsCard` body.

- [ ] **Step 4: Run all web tests**

```bash
npm test
```

Expected: all P1 + P2-frontend-deps + P3 tests pass. (Note: P2 was backend, no web test impact.)

- [ ] **Step 5: Commit**

```bash
git add bom-web/src/App.tsx bom-web/src/store/auth.ts bom-web/src/features/auth/ProfilePage.tsx
git commit -m "feat(web): wire push permission prompt + logout unsubscribe + notifications profile row"
```

---

### Verification (V1)

| # | Task | Outputs |
|---|---|---|
| **V1** | Real-device end-to-end smoke on iPhone iOS 16.4+ (or BrowserStack iOS Safari). | Smoke checklist passing |

- [ ] **Step 1: Deploy preview**

Push branch, Cloudflare Pages auto-deploys.

- [ ] **Step 2: iPhone smoke (or BrowserStack)**

1. Open preview URL in Safari
2. Login as `ali@test.com / Test@1234`
3. Tap Share → Add to Home Screen → Add
4. Re-launch from home screen icon (standalone mode)
5. Observe: Sonner permission prompt appears on first login post-install
6. Tap "Enable" → iOS native permission dialog → tap Allow
7. Verify success state in Profile → App Settings → Notifications row shows "Disable" button (subscribed)
8. Verify backend has subscription row:
   ```bash
   psql -h <neon-host> -U <user> -d bom_price_approval -c "SELECT \"UserId\", \"Endpoint\", \"UserAgent\" FROM \"PushSubscriptions\";"
   ```
9. Trigger a notification: have BomCreator submit BOM for ali's req → ali (SP) should receive notification on iPhone lock screen even with PWA closed
10. Tap notification on lock screen → app opens to home

- [ ] **Step 3: iPad smoke**

Repeat steps 1-9 on iPad with iPadOS 16.4+. Verify modern iPadOS detection works (modal showed, install successful).

- [ ] **Step 4: Disable notifications smoke**

In Profile → App Settings → Notifications → tap Disable. Verify:
- Backend row deleted (psql query — empty)
- Browser permission still "granted" (iOS doesn't auto-revoke)
- Re-tap Enable → re-subscribes (fresh row in DB)

- [ ] **Step 5: Logout cleanup smoke**

While subscribed, logout. Verify:
- Backend row deleted before redirect
- After logout, login as different user → re-subscribe creates new row tied to new userId

- [ ] **Step 6: Permission denied path**

Fresh device, install PWA, on permission prompt tap Deny. Verify:
- Profile shows "Disabled — open iOS Settings..." message
- No backend row created
- 14-day localStorage suppression works (no re-prompt for 14 days)

---

### Close (C1)

| # | Task | Outputs |
|---|---|---|
| **C1** | Update memory `project_pwa_conversion.md` (mark P3 merged). Open PR via GitHub UI. | docs commit, PR opened by user |

- [ ] **Step 1: Update memory**

Edit `C:\Users\Administrator\.claude\projects\D--shan-projects-BOM-Price-Approval\memory\project_pwa_conversion.md`:

Replace `## P3 — Web Push Frontend (pending)` with:
```markdown
## P3 — Web Push Frontend (MERGED)

- Branch `feat/pwa-push-frontend` → master @ {SHA}
- Permission prompt shown post-install on first login (14-day suppression on dismiss)
- `usePushSubscription` hook owns lifecycle (browser PushManager + backend POST/DELETE)
- SW `push` + `notificationclick` listeners (P1 sw.ts extended)
- Logout unsubscribes from push before clearing tokens
- Profile page App Settings card has Notifications row (Enable / Disable / Settings hint)
- Real-device smoke passed: iPhone iOS 17.4 + iPad iPadOS 17.4 (via BrowserStack {OR real devices})

## Status
PWA conversion COMPLETE. Free iOS/iPadOS distribution path live for Fujairah staff.

## Memory hooks
- `feedback_no_play_store` — confirmed Android Play Store still out of scope; web/EAS APK + PWA cover all platforms
- Move iOS app placeholder out of `project_session_2026_04_28_full_cleanup.md` "pending" list
```

Update `MEMORY.md` index to reflect merged status.

- [ ] **Step 2: Commit + PR**

```bash
git add CLAUDE.md  # if any final docs touch
# (no source changes here beyond memory which is outside repo)
```

User opens PR titled `feat: PWA Phase 3 — Web Push frontend (permission prompt + SW listeners + lifecycle)`.

---

## Self-review

**Spec coverage:**
- ✅ `usePushSubscription` hook with subscribe/unsubscribe/permission state (U1)
- ✅ `NotificationPermissionPrompt` post-install with 14-day suppression (U2)
- ✅ SW `push` + `notificationclick` listeners (S1)
- ✅ Generic notification content (no entity data) — enforced by **backend** (`NotificationService` already keeps notif body generic in spec); frontend SW just renders what server sends, so this is a server-side invariant
- ✅ Logout unsubscribe before token clear (W1)
- ✅ Profile page Notifications row with iOS denial recovery hint (W1)
- ✅ Real-device end-to-end smoke (V1)

**Placeholder scan:** All file paths exact. Test bodies complete. `{P2_MERGE_SHA}` and `{SHA}` are intentional — filled at branch-off / merge time.

**Type consistency:**
- `usePushSubscription` returns `{ permission, isSubscribed, subscribe, unsubscribe }` — consumed by `NotificationPermissionPrompt` (uses `permission`, `subscribe`) and `NotificationsRow` (uses all four).
- `pushSubscriptions.subscribe(payload: PushSubscribePayload)` — consumed by `usePushSubscription`.
- `pushSubscriptions.unsubscribe(endpoint: string)` — consumed by `usePushSubscription` and `logout`.

**Out-of-scope:**
- Generic notification content enforcement is in P2 (backend). Frontend SW renders server-provided title/body verbatim.
- Mobile RN app push notifs (still SignalR-only per memory; not changing).
- Multi-device subscription management UI (e.g., "Sign out of all devices") — future polish.

---

## Execution mode

Recommend **inline execution** for entire P3 — patterns are well-established (axios client, hook, component, SW listener), individual tasks small. Single session with vitest after each commit.
