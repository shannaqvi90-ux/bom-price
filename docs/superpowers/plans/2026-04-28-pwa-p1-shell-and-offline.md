# PWA Phase 1 — Shell & Offline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Status:** Draft, awaiting plan approval before execute.
**Spec:** `docs/superpowers/specs/2026-04-28-pwa-conversion-design.md` (P1 section)
**Branch:** `feat/pwa-shell-and-offline` (off `master @ 4a38503`)
**Goal:** Make `bom-web` installable as PWA on iOS/iPadOS/Android with offline-first read cache, branded with FPF mobile app icons, with install + update + offline UX.
**Architecture:** Vite + `vite-plugin-pwa` (Workbox `injectManifest` mode) + `@vite-pwa/assets-generator`. Custom service worker source at `src/sw.ts`. NetworkFirst caching for API GET, NetworkOnly for mutations + auth + notifications. React 19 hooks + Sonner toasts for install/update UX.
**Tech Stack:** React 19, Vite 8, TypeScript, Tailwind 4, Sonner, Workbox.
**Total tasks:** 14. **Estimated:** 3-4 hr.

---

## Phase ordering rationale

Foundation (deps, icons, manifest) must land before UX hooks. Hooks must exist before components consume them. Service worker requires platform detection and update hook. Profile/wire-up at the end. Verification last.

```
F1-F3 foundation → H1-H2 hooks → U1-U4 components → S1 service worker → W1-W2 wiring → V1 verification → C1 close
```

---

## Tasks

### Foundation (F1-F3)

| # | Task | Outputs |
|---|---|---|
| **F1** | Install deps: `npm i -D vite-plugin-pwa @vite-pwa/assets-generator workbox-window` in `bom-web/`. Verify `package.json` updated. | 1 package.json edit |
| **F2** | Copy mobile RN icon to web public dir + run assets-generator to produce all PWA icon sizes. Delete boilerplate `favicon.svg` + `icons.svg`. | 1 source PNG copied, ~10 generated PNGs in `bom-web/public/icons/`, 2 boilerplate files deleted |
| **F3** | Create `public/manifest.webmanifest` and update `index.html` with theme-color, apple-touch-icon, manifest link, and standalone-mode meta tags. | 1 manifest, 1 index.html edit |

#### F2 detailed steps

- [ ] **Step 1: Copy source icon**

```bash
cp bom-mobile/assets/icon.png bom-web/public/icon-source.png
```

- [ ] **Step 2: Add assets-generator config**

Create `bom-web/pwa-assets.config.ts`:

```typescript
import { defineConfig, minimal2023Preset as preset } from "@vite-pwa/assets-generator/config";

export default defineConfig({
  preset,
  images: ["public/icon-source.png"],
});
```

- [ ] **Step 3: Generate icons**

```bash
cd bom-web && npx pwa-assets-generator
```

Expected: ~6 PNGs created in `public/` (favicon variants, apple-touch-icon, maskable + transparent variants).

- [ ] **Step 4: Verify generated icons exist**

```bash
ls bom-web/public/*.png
```

Expected files: `pwa-64x64.png`, `pwa-192x192.png`, `pwa-512x512.png`, `maskable-icon-512x512.png`, `apple-touch-icon.png`, `favicon.ico`.

- [ ] **Step 5: Delete boilerplate**

```bash
rm bom-web/public/favicon.svg bom-web/public/icons.svg
```

- [ ] **Step 6: Commit**

```bash
git add bom-web/package.json bom-web/package-lock.json bom-web/pwa-assets.config.ts bom-web/public/
git commit -m "feat(web): add PWA icon assets from mobile RN source"
```

#### F3 detailed steps

- [ ] **Step 1: Create `public/manifest.webmanifest`**

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
    { "src": "/pwa-192x192.png", "sizes": "192x192", "type": "image/png" },
    { "src": "/pwa-512x512.png", "sizes": "512x512", "type": "image/png" },
    { "src": "/maskable-icon-512x512.png", "sizes": "512x512", "type": "image/png", "purpose": "maskable" },
    { "src": "/apple-touch-icon.png", "sizes": "180x180", "type": "image/png" }
  ]
}
```

- [ ] **Step 2: Update `index.html`**

Replace existing head section with:

```html
<!doctype html>
<html lang="en">
  <head>
    <meta charset="UTF-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0, viewport-fit=cover" />
    <link rel="manifest" href="/manifest.webmanifest" />
    <meta name="theme-color" content="#1e40af" />
    <meta name="apple-mobile-web-app-capable" content="yes" />
    <meta name="apple-mobile-web-app-status-bar-style" content="black-translucent" />
    <meta name="apple-mobile-web-app-title" content="FPF Quotations" />
    <link rel="apple-touch-icon" href="/apple-touch-icon.png" />
    <link rel="icon" type="image/png" sizes="32x32" href="/pwa-64x64.png" />
    <title>FPF Quotations</title>
  </head>
  <body>
    <div id="root"></div>
    <script type="module" src="/src/main.tsx"></script>
  </body>
</html>
```

- [ ] **Step 3: Commit**

```bash
git add bom-web/public/manifest.webmanifest bom-web/index.html
git commit -m "feat(web): add PWA manifest and Apple meta tags"
```

---

### Hooks + Utils (H1-H2)

| # | Task | Outputs |
|---|---|---|
| **H1** | `src/utils/platform.ts` — iPadOS-aware platform detection (4 helpers). Vitest tests covering iPhone, iPad-modern, iPad-old, Android Chrome, Desktop Chrome, Safari Mac. | 1 util, 1 test file |
| **H2** | `src/hooks/usePwaInstall.ts` — captures `beforeinstallprompt`, exposes platform-aware install state + trigger. Vitest tests. `src/hooks/useServiceWorker.ts` — Workbox `Workbox` lifecycle wrapping, exposes `updateAvailable` + `applyUpdate()`. Vitest tests. | 2 hooks, 2 test files |

#### H1 detailed steps

- [ ] **Step 1: Write failing test**

Create `bom-web/src/utils/platform.test.ts`:

```typescript
import { describe, it, expect, beforeEach, vi } from "vitest";
import { isIOSorIPadOS, isSafari, isStandalone, isAndroidChrome } from "./platform";

const setUA = (ua: string, maxTouch = 0) => {
  Object.defineProperty(navigator, "userAgent", { value: ua, configurable: true });
  Object.defineProperty(navigator, "maxTouchPoints", { value: maxTouch, configurable: true });
};

describe("platform helpers", () => {
  describe("isIOSorIPadOS", () => {
    it("detects iPhone", () => {
      setUA("Mozilla/5.0 (iPhone; CPU iPhone OS 17_4 like Mac OS X) Safari");
      expect(isIOSorIPadOS()).toBe(true);
    });
    it("detects pre-iPadOS-13 iPad (legacy UA)", () => {
      setUA("Mozilla/5.0 (iPad; CPU OS 12_4 like Mac OS X) Safari");
      expect(isIOSorIPadOS()).toBe(true);
    });
    it("detects modern iPadOS (Mac-disguised UA + touch)", () => {
      setUA("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) Safari", 5);
      expect(isIOSorIPadOS()).toBe(true);
    });
    it("rejects desktop Mac (no touch)", () => {
      setUA("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) Safari", 0);
      expect(isIOSorIPadOS()).toBe(false);
    });
    it("rejects Android", () => {
      setUA("Mozilla/5.0 (Linux; Android 14; Pixel 8) Chrome", 5);
      expect(isIOSorIPadOS()).toBe(false);
    });
  });

  describe("isSafari", () => {
    it("detects Safari iPhone", () => {
      setUA("Mozilla/5.0 (iPhone; CPU iPhone OS 17_4 like Mac OS X) Version/17.4 Safari");
      expect(isSafari()).toBe(true);
    });
    it("rejects Chrome iOS", () => {
      setUA("Mozilla/5.0 (iPhone; CPU iPhone OS 17_4) CriOS/120 Safari");
      expect(isSafari()).toBe(false);
    });
  });

  describe("isAndroidChrome", () => {
    it("detects Android Chrome", () => {
      setUA("Mozilla/5.0 (Linux; Android 14; Pixel 8) Chrome/120");
      expect(isAndroidChrome()).toBe(true);
    });
    it("rejects iOS Chrome (CriOS)", () => {
      setUA("Mozilla/5.0 (iPhone) CriOS/120");
      expect(isAndroidChrome()).toBe(false);
    });
  });
});
```

- [ ] **Step 2: Run test, verify FAIL**

```bash
cd bom-web && npm test -- platform.test
```

Expected: `Cannot find module './platform'`

- [ ] **Step 3: Implement `src/utils/platform.ts`**

```typescript
export const isIOSorIPadOS = (): boolean => {
  if (/iPad|iPhone|iPod/.test(navigator.userAgent)) return true;
  return /Macintosh/.test(navigator.userAgent) && navigator.maxTouchPoints > 1;
};

export const isSafari = (): boolean =>
  /Safari/.test(navigator.userAgent) && !/Chrome|CriOS|FxiOS|EdgiOS/.test(navigator.userAgent);

export const isStandalone = (): boolean =>
  window.matchMedia("(display-mode: standalone)").matches ||
  (navigator as { standalone?: boolean }).standalone === true;

export const isAndroidChrome = (): boolean =>
  /Android/.test(navigator.userAgent) && /Chrome/.test(navigator.userAgent);
```

- [ ] **Step 4: Run tests, verify PASS**

```bash
npm test -- platform.test
```

Expected: all 8+ tests pass.

- [ ] **Step 5: Commit**

```bash
git add bom-web/src/utils/platform.ts bom-web/src/utils/platform.test.ts
git commit -m "feat(web): add iPadOS-aware platform detection helpers"
```

#### H2 detailed steps

- [ ] **Step 1: Write `usePwaInstall.ts`**

`bom-web/src/hooks/usePwaInstall.ts`:

```typescript
import { useEffect, useState } from "react";
import { isIOSorIPadOS, isSafari, isStandalone, isAndroidChrome } from "@/utils/platform";

interface BeforeInstallPromptEvent extends Event {
  prompt(): Promise<void>;
  userChoice: Promise<{ outcome: "accepted" | "dismissed" }>;
}

interface PwaInstallState {
  canPromptInstall: boolean;          // Android Chrome with deferred prompt
  shouldShowIosModal: boolean;        // iOS/iPadOS Safari, not yet installed, not dismissed recently
  isInstalled: boolean;
  promptInstall: () => Promise<void>;
  dismissIosModal: () => void;
}

const DISMISS_KEY = "pwa-install-modal-dismissed";
const DISMISS_TTL_MS = 30 * 24 * 60 * 60 * 1000;

export function usePwaInstall(): PwaInstallState {
  const [deferredPrompt, setDeferredPrompt] = useState<BeforeInstallPromptEvent | null>(null);
  const [isInstalled, setIsInstalled] = useState(isStandalone());

  useEffect(() => {
    const beforeHandler = (e: Event) => {
      e.preventDefault();
      setDeferredPrompt(e as BeforeInstallPromptEvent);
    };
    const installedHandler = () => setIsInstalled(true);
    window.addEventListener("beforeinstallprompt", beforeHandler);
    window.addEventListener("appinstalled", installedHandler);
    return () => {
      window.removeEventListener("beforeinstallprompt", beforeHandler);
      window.removeEventListener("appinstalled", installedHandler);
    };
  }, []);

  const dismissedAt = Number(localStorage.getItem(DISMISS_KEY) ?? 0);
  const dismissedRecently = Date.now() - dismissedAt < DISMISS_TTL_MS;

  const shouldShowIosModal =
    isIOSorIPadOS() && isSafari() && !isInstalled && !dismissedRecently;

  const canPromptInstall = isAndroidChrome() && !isInstalled && deferredPrompt !== null;

  const promptInstall = async () => {
    if (!deferredPrompt) return;
    await deferredPrompt.prompt();
    const result = await deferredPrompt.userChoice;
    if (result.outcome === "accepted") setIsInstalled(true);
    setDeferredPrompt(null);
  };

  const dismissIosModal = () => {
    localStorage.setItem(DISMISS_KEY, String(Date.now()));
  };

  return { canPromptInstall, shouldShowIosModal, isInstalled, promptInstall, dismissIosModal };
}
```

- [ ] **Step 2: Write tests**

`bom-web/src/hooks/usePwaInstall.test.tsx`:

```typescript
import { describe, it, expect, beforeEach, vi } from "vitest";
import { renderHook, act } from "@testing-library/react";
import { usePwaInstall } from "./usePwaInstall";

const setUA = (ua: string, maxTouch = 0) => {
  Object.defineProperty(navigator, "userAgent", { value: ua, configurable: true });
  Object.defineProperty(navigator, "maxTouchPoints", { value: maxTouch, configurable: true });
};

beforeEach(() => {
  localStorage.clear();
  Object.defineProperty(window, "matchMedia", {
    value: () => ({ matches: false, addEventListener: () => {}, removeEventListener: () => {} }),
    configurable: true,
  });
});

describe("usePwaInstall", () => {
  it("shouldShowIosModal=true on iPhone Safari, not installed, not dismissed", () => {
    setUA("Mozilla/5.0 (iPhone) Version/17.4 Safari");
    const { result } = renderHook(() => usePwaInstall());
    expect(result.current.shouldShowIosModal).toBe(true);
    expect(result.current.canPromptInstall).toBe(false);
  });

  it("shouldShowIosModal=false after dismissIosModal", () => {
    setUA("Mozilla/5.0 (iPhone) Version/17.4 Safari");
    const { result } = renderHook(() => usePwaInstall());
    act(() => result.current.dismissIosModal());
    const { result: result2 } = renderHook(() => usePwaInstall());
    expect(result2.current.shouldShowIosModal).toBe(false);
  });

  it("canPromptInstall=true on Android Chrome with deferred prompt", () => {
    setUA("Mozilla/5.0 (Linux; Android 14) Chrome/120", 5);
    const { result } = renderHook(() => usePwaInstall());
    act(() => {
      const e = new Event("beforeinstallprompt") as Event & { prompt?: () => void };
      e.prompt = vi.fn();
      window.dispatchEvent(e);
    });
    expect(result.current.canPromptInstall).toBe(true);
  });
});
```

- [ ] **Step 3: Run tests, verify PASS**

```bash
npm test -- usePwaInstall
```

- [ ] **Step 4: Write `useServiceWorker.ts`**

`bom-web/src/hooks/useServiceWorker.ts`:

```typescript
import { useEffect, useState } from "react";
import { Workbox } from "workbox-window";

interface ServiceWorkerState {
  updateAvailable: boolean;
  applyUpdate: () => void;
}

let wbInstance: Workbox | null = null;

export function useServiceWorker(): ServiceWorkerState {
  const [updateAvailable, setUpdateAvailable] = useState(false);

  useEffect(() => {
    if (!("serviceWorker" in navigator)) return;
    if (import.meta.env.DEV) return;

    if (!wbInstance) {
      wbInstance = new Workbox("/sw.js", { scope: "/" });
      wbInstance.addEventListener("waiting", () => setUpdateAvailable(true));
      wbInstance.addEventListener("controlling", () => window.location.reload());
      wbInstance.register().catch((err) => console.warn("SW registration failed", err));
    }
  }, []);

  const applyUpdate = () => {
    if (!wbInstance) return;
    wbInstance.messageSkipWaiting();
  };

  return { updateAvailable, applyUpdate };
}
```

- [ ] **Step 5: Write tests**

`bom-web/src/hooks/useServiceWorker.test.tsx`:

```typescript
import { describe, it, expect, vi } from "vitest";
import { renderHook } from "@testing-library/react";
import { useServiceWorker } from "./useServiceWorker";

vi.mock("workbox-window", () => ({
  Workbox: vi.fn().mockImplementation(() => ({
    addEventListener: vi.fn(),
    register: vi.fn().mockResolvedValue(undefined),
    messageSkipWaiting: vi.fn(),
  })),
}));

describe("useServiceWorker", () => {
  it("returns updateAvailable=false initially", () => {
    const { result } = renderHook(() => useServiceWorker());
    expect(result.current.updateAvailable).toBe(false);
  });

  it("exposes applyUpdate function", () => {
    const { result } = renderHook(() => useServiceWorker());
    expect(typeof result.current.applyUpdate).toBe("function");
  });
});
```

- [ ] **Step 6: Run tests, verify PASS**

```bash
npm test -- useServiceWorker
```

- [ ] **Step 7: Commit**

```bash
git add bom-web/src/hooks/usePwaInstall.ts bom-web/src/hooks/usePwaInstall.test.tsx bom-web/src/hooks/useServiceWorker.ts bom-web/src/hooks/useServiceWorker.test.tsx
git commit -m "feat(web): add usePwaInstall + useServiceWorker hooks"
```

---

### UI Components (U1-U4)

| # | Task | Outputs |
|---|---|---|
| **U1** | `src/components/pwa/InstallModal.tsx` — fullscreen iOS/iPadOS install modal with step-by-step Safari Share→Add-to-Home-Screen guidance, single "I'll do it later" dismiss. Vitest tests cover render conditions. | 1 component, 1 test file |
| **U2** | `src/components/pwa/InstallBanner.tsx` — small dismissible Android Chrome top banner with native install button. Vitest tests. | 1 component, 1 test file |
| **U3** | `src/components/pwa/UpdateToast.tsx` — Sonner toast on `updateAvailable` from `useServiceWorker`, with "Refresh now" / "Later" actions. Vitest tests. | 1 component, 1 test file |
| **U4** | `src/components/pwa/OfflineBanner.tsx` — top sticky red banner on `navigator.onLine === false`. Auto-hides on `online` event. Vitest tests. | 1 component, 1 test file |

#### U1 — InstallModal (key code)

`bom-web/src/components/pwa/InstallModal.tsx`:

```tsx
import { usePwaInstall } from "@/hooks/usePwaInstall";
import { Share, Plus } from "lucide-react";

export function InstallModal() {
  const { shouldShowIosModal, dismissIosModal } = usePwaInstall();

  if (!shouldShowIosModal) return null;

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/70 px-4">
      <div className="max-w-md w-full rounded-2xl bg-white p-6 shadow-2xl">
        <div className="flex items-center gap-3 mb-4">
          <div className="rounded-xl bg-blue-700 p-2">
            <img src="/apple-touch-icon.png" alt="" className="h-10 w-10 rounded-lg" />
          </div>
          <h2 className="text-xl font-semibold">Install FPF Quotations</h2>
        </div>

        <p className="text-sm text-gray-600 mb-4">
          Get the full app experience on your home screen — faster access, notifications, and offline support.
        </p>

        <div className="space-y-3 mb-6">
          <Step n={1} icon={<Share className="h-5 w-5 text-blue-600" />}>
            Tap the <b>Share</b> button at the bottom of Safari
          </Step>
          <Step n={2} icon={<Plus className="h-5 w-5 text-blue-600" />}>
            Scroll and tap <b>Add to Home Screen</b>
          </Step>
          <Step n={3}>
            Tap <b>Add</b> in the top right
          </Step>
        </div>

        <button
          onClick={dismissIosModal}
          className="w-full rounded-lg border border-gray-300 px-4 py-2 text-sm font-medium hover:bg-gray-50"
        >
          I'll do it later
        </button>
      </div>
    </div>
  );
}

function Step({ n, icon, children }: { n: number; icon?: React.ReactNode; children: React.ReactNode }) {
  return (
    <div className="flex items-start gap-3">
      <div className="flex h-7 w-7 shrink-0 items-center justify-center rounded-full bg-blue-100 text-sm font-semibold text-blue-700">
        {n}
      </div>
      <div className="flex-1 flex items-center gap-2 pt-0.5 text-sm text-gray-800">
        {icon} <span>{children}</span>
      </div>
    </div>
  );
}
```

Tests `InstallModal.test.tsx`:

```tsx
import { describe, it, expect, beforeEach, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { InstallModal } from "./InstallModal";

vi.mock("@/hooks/usePwaInstall", () => ({
  usePwaInstall: vi.fn(),
}));
import { usePwaInstall } from "@/hooks/usePwaInstall";

describe("InstallModal", () => {
  it("renders when shouldShowIosModal=true", () => {
    vi.mocked(usePwaInstall).mockReturnValue({
      shouldShowIosModal: true,
      dismissIosModal: vi.fn(),
      canPromptInstall: false,
      isInstalled: false,
      promptInstall: vi.fn(),
    });
    render(<InstallModal />);
    expect(screen.getByText(/Install FPF Quotations/)).toBeInTheDocument();
    expect(screen.getByText(/Add to Home Screen/)).toBeInTheDocument();
  });

  it("renders nothing when shouldShowIosModal=false", () => {
    vi.mocked(usePwaInstall).mockReturnValue({
      shouldShowIosModal: false,
      dismissIosModal: vi.fn(),
      canPromptInstall: false,
      isInstalled: true,
      promptInstall: vi.fn(),
    });
    const { container } = render(<InstallModal />);
    expect(container.firstChild).toBeNull();
  });
});
```

#### U2 — InstallBanner (key code)

`bom-web/src/components/pwa/InstallBanner.tsx`:

```tsx
import { useState } from "react";
import { usePwaInstall } from "@/hooks/usePwaInstall";
import { Download, X } from "lucide-react";

export function InstallBanner() {
  const { canPromptInstall, promptInstall } = usePwaInstall();
  const [dismissed, setDismissed] = useState(false);
  if (!canPromptInstall || dismissed) return null;
  return (
    <div className="fixed top-3 right-3 z-40 flex items-center gap-2 rounded-lg bg-blue-700 px-3 py-2 text-sm text-white shadow-lg">
      <Download className="h-4 w-4" />
      <span>Install FPF Quotations</span>
      <button onClick={promptInstall} className="rounded bg-white px-2 py-0.5 text-xs font-medium text-blue-700">
        Install
      </button>
      <button onClick={() => setDismissed(true)} className="ml-1 opacity-70 hover:opacity-100">
        <X className="h-4 w-4" />
      </button>
    </div>
  );
}
```

#### U3 — UpdateToast (key code)

`bom-web/src/components/pwa/UpdateToast.tsx`:

```tsx
import { useEffect } from "react";
import { toast } from "sonner";
import { useServiceWorker } from "@/hooks/useServiceWorker";

export function UpdateToast() {
  const { updateAvailable, applyUpdate } = useServiceWorker();

  useEffect(() => {
    if (!updateAvailable) return;
    toast("Naya version available", {
      description: "Refresh karne par new version active hoga.",
      action: { label: "Refresh now", onClick: applyUpdate },
      duration: Infinity,
    });
  }, [updateAvailable, applyUpdate]);

  return null;
}
```

#### U4 — OfflineBanner (key code)

`bom-web/src/components/pwa/OfflineBanner.tsx`:

```tsx
import { useEffect, useState } from "react";
import { WifiOff } from "lucide-react";

export function OfflineBanner() {
  const [isOffline, setIsOffline] = useState(!navigator.onLine);
  useEffect(() => {
    const onOnline = () => setIsOffline(false);
    const onOffline = () => setIsOffline(true);
    window.addEventListener("online", onOnline);
    window.addEventListener("offline", onOffline);
    return () => {
      window.removeEventListener("online", onOnline);
      window.removeEventListener("offline", onOffline);
    };
  }, []);

  if (!isOffline) return null;
  return (
    <div className="sticky top-0 z-40 flex items-center justify-center gap-2 bg-red-600 px-4 py-2 text-sm font-medium text-white">
      <WifiOff className="h-4 w-4" />
      Offline — showing last cached data. New requisitions will fail until reconnected.
    </div>
  );
}
```

Each component task ends with: write component → write its `.test.tsx` → run vitest until green → commit.

```bash
git add bom-web/src/components/pwa/
git commit -m "feat(web): add PWA UX components (InstallModal, InstallBanner, UpdateToast, OfflineBanner)"
```

---

### Service Worker (S1)

| # | Task | Outputs |
|---|---|---|
| **S1** | Create `src/sw.ts` with Workbox routes (precache + 2 NetworkFirst caches). Register `vite-plugin-pwa` in `vite.config.ts` with `injectManifest` strategy. Verify production build emits `dist/sw.js`. | 1 SW source, 1 vite config edit, manual verify |

#### S1 detailed steps

- [ ] **Step 1: Create `src/sw.ts`**

```typescript
/// <reference lib="webworker" />
import { precacheAndRoute } from "workbox-precaching";
import { registerRoute } from "workbox-routing";
import { NetworkFirst } from "workbox-strategies";
import { ExpirationPlugin } from "workbox-expiration";
import { CacheableResponsePlugin } from "workbox-cacheable-response";

declare const self: ServiceWorkerGlobalScope;

precacheAndRoute(self.__WB_MANIFEST);

const apiListPattern = /\/api\/(requisitions|customers|items|branches|users|groups)$/;
const apiDetailPattern = /\/api\/(requisitions|customers|items|bom|costing|approvals)\/\d+/;

registerRoute(
  ({ url }) => apiListPattern.test(url.pathname),
  new NetworkFirst({
    cacheName: "bom-api-list-cache",
    networkTimeoutSeconds: 5,
    plugins: [
      new CacheableResponsePlugin({ statuses: [200] }),
      new ExpirationPlugin({ maxEntries: 100, maxAgeSeconds: 60 * 60 * 24 }),
    ],
  })
);

registerRoute(
  ({ url }) => apiDetailPattern.test(url.pathname),
  new NetworkFirst({
    cacheName: "bom-api-detail-cache",
    networkTimeoutSeconds: 5,
    plugins: [
      new CacheableResponsePlugin({ statuses: [200] }),
      new ExpirationPlugin({ maxEntries: 200, maxAgeSeconds: 60 * 60 * 24 }),
    ],
  })
);

self.addEventListener("message", (event) => {
  if (event.data?.type === "SKIP_WAITING") {
    self.skipWaiting();
  }
});
```

- [ ] **Step 2: Update `vite.config.ts` to register VitePWA**

```typescript
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";
import { VitePWA } from "vite-plugin-pwa";
import path from "node:path";

export default defineConfig({
  plugins: [
    react(),
    tailwindcss(),
    VitePWA({
      strategies: "injectManifest",
      srcDir: "src",
      filename: "sw.ts",
      registerType: "prompt",
      injectRegister: false,
      manifest: false, // we provide our own public/manifest.webmanifest
      injectManifest: {
        globPatterns: ["**/*.{js,css,html,svg,png,ico,woff2}"],
        maximumFileSizeToCacheInBytes: 5 * 1024 * 1024,
      },
      devOptions: {
        enabled: false,
      },
    }),
  ],
  resolve: {
    alias: {
      "@": path.resolve(__dirname, "./src"),
    },
  },
  server: {
    port: 5300,
    strictPort: true,
    proxy: {
      "/api": { target: "http://localhost:7300", changeOrigin: true },
      "/hubs": { target: "http://localhost:7300", changeOrigin: true, ws: true },
    },
  },
  test: {
    globals: true,
    environment: "jsdom",
    setupFiles: ["./src/test/setup.ts"],
    css: true,
  },
});
```

- [ ] **Step 3: Verify build emits sw.js**

```bash
cd bom-web && npm run build
ls dist/sw.js dist/manifest.webmanifest
```

Expected: both files exist. `dist/sw.js` references precached assets via `__WB_MANIFEST`.

- [ ] **Step 4: Commit**

```bash
git add bom-web/src/sw.ts bom-web/vite.config.ts
git commit -m "feat(web): add custom service worker with NetworkFirst API caching"
```

---

### Wiring + Profile (W1-W2)

| # | Task | Outputs |
|---|---|---|
| **W1** | Mount `<InstallModal>`, `<InstallBanner>`, `<UpdateToast>`, `<OfflineBanner>` into `App.tsx` (or root layout). Order: OfflineBanner top, then content, then modals/toasts overlaid. | 1 App.tsx edit |
| **W2** | Extend logout flow in `src/store/auth.ts` (or wherever logout lives) to delete `bom-api-list-cache` + `bom-api-detail-cache` before clearing tokens. Add "App Settings" card to profile page with install button + install state. Vitest test for logout cache clear. | 1 store edit, 1 profile page edit, 1 test |

#### W1 detailed steps

- [ ] **Step 1: Locate root layout file**

```bash
grep -rn "Toaster\|Sonner" bom-web/src/App.tsx bom-web/src/main.tsx 2>/dev/null
```

Find where the existing `<Toaster />` is mounted. Add PWA components nearby.

- [ ] **Step 2: Update `App.tsx`**

Insert at the same level as existing top-level providers:

```tsx
import { OfflineBanner } from "@/components/pwa/OfflineBanner";
import { InstallModal } from "@/components/pwa/InstallModal";
import { InstallBanner } from "@/components/pwa/InstallBanner";
import { UpdateToast } from "@/components/pwa/UpdateToast";

// inside the main App return tree, near <Toaster />:
<>
  <OfflineBanner />
  <InstallBanner />
  {/* ... existing routes ... */}
  <InstallModal />
  <UpdateToast />
  <Toaster />
</>
```

- [ ] **Step 3: Build + run dev**

```bash
cd bom-web && npm run dev
```

Expected: dev server starts, no SW registered (devOptions disabled), no console errors.

- [ ] **Step 4: Commit**

```bash
git add bom-web/src/App.tsx
git commit -m "feat(web): wire PWA components into root layout"
```

#### W2 detailed steps

- [ ] **Step 1: Locate logout function**

```bash
grep -rn "logout" bom-web/src/store/ bom-web/src/api/ 2>/dev/null | head -20
```

- [ ] **Step 2: Extend logout to clear caches**

Edit logout function (likely in `src/store/auth.ts` or `src/api/axios.ts`):

```typescript
async function clearPwaCaches() {
  if (!("caches" in window)) return;
  await Promise.all([
    caches.delete("bom-api-list-cache").catch(() => {}),
    caches.delete("bom-api-detail-cache").catch(() => {}),
  ]);
}

// inside existing logout:
async function logout() {
  await clearPwaCaches();
  // existing: clear tokens, redirect, etc.
}
```

- [ ] **Step 3: Write test for cache-clear behavior**

`bom-web/src/store/auth.test.ts` (or extend existing) — mock `caches.delete`, call logout, assert both deletes invoked.

```typescript
it("logout clears API caches", async () => {
  const deleteFn = vi.fn().mockResolvedValue(true);
  Object.defineProperty(window, "caches", {
    value: { delete: deleteFn },
    configurable: true,
  });
  await useAuthStore.getState().logout();
  expect(deleteFn).toHaveBeenCalledWith("bom-api-list-cache");
  expect(deleteFn).toHaveBeenCalledWith("bom-api-detail-cache");
});
```

- [ ] **Step 4: Add App Settings card to profile page**

Find profile page. Add card:

```tsx
import { usePwaInstall } from "@/hooks/usePwaInstall";

function AppSettingsCard() {
  const { isInstalled, canPromptInstall, promptInstall } = usePwaInstall();
  return (
    <section className="rounded-lg border bg-white p-4">
      <h3 className="mb-3 font-semibold">App Settings</h3>
      <div className="space-y-2 text-sm">
        <div className="flex items-center justify-between">
          <span>Install on this device</span>
          {isInstalled ? (
            <span className="text-green-600">Installed</span>
          ) : canPromptInstall ? (
            <button onClick={promptInstall} className="rounded bg-blue-700 px-3 py-1 text-white">
              Install Now
            </button>
          ) : (
            <span className="text-gray-500">Use Safari Share menu</span>
          )}
        </div>
      </div>
    </section>
  );
}
```

- [ ] **Step 5: Run all tests**

```bash
npm test
```

Expected: all 263+ existing tests + new PWA tests pass.

- [ ] **Step 6: Commit**

```bash
git add bom-web/src/store/auth.ts bom-web/src/features/auth/ProfilePage.tsx bom-web/src/store/auth.test.ts
git commit -m "feat(web): clear PWA caches on logout + add App Settings profile card"
```

---

### Verification (V1)

| # | Task | Outputs |
|---|---|---|
| **V1** | Build production bundle, verify Lighthouse PWA audit ≥ 90, manual smoke on Cloudflare Pages preview deploy: install on iPhone Safari, install on iPad Safari, install on Android Chrome, verify offline cached reqs list, verify update toast on next deploy. | Lighthouse report, smoke checklist passing |

- [ ] **Step 1: Build**

```bash
cd bom-web && npm run build
```

Expected: build succeeds, `dist/sw.js`, `dist/manifest.webmanifest`, `dist/pwa-*.png` all present.

- [ ] **Step 2: Local preview**

```bash
npm run preview
```

Open `http://localhost:4173` (or whatever Vite preview port). Open DevTools → Application → Manifest → verify icons + theme + start_url. Application → Service Workers → verify SW registered + active.

- [ ] **Step 3: Lighthouse audit (Chrome DevTools)**

DevTools → Lighthouse → check Progressive Web App → Run analysis. Target: PWA score ≥ 90.

If failing, common fixes:
- Missing apple-touch-icon → check `index.html`
- Manifest missing fields → check `manifest.webmanifest`
- SW not controlling page → check `start_url` matches scope

- [ ] **Step 4: Deploy to Cloudflare Pages preview**

Push branch + open PR. Cloudflare auto-deploys to preview URL.

- [ ] **Step 5: iPhone Safari smoke**

On real iPhone (or BrowserStack iOS Safari):
1. Open preview URL
2. Login as `ali@test.com / Test@1234`
3. Verify Install Modal appears
4. Tap "I'll do it later" → modal dismissed
5. Manual: Share → Add to Home Screen → confirm icon shows FPF logo
6. Re-launch from icon → standalone fullscreen
7. Verify Install Modal does NOT re-appear (isStandalone now true)

- [ ] **Step 6: iPad Safari smoke**

Same as Step 5 on iPad. Specifically verify modern iPadOS (Mac-disguised UA + touch) is detected as iOS — modal should appear.

- [ ] **Step 7: Android Chrome smoke**

On Android device:
1. Open preview URL
2. Verify Install Banner appears top-right
3. Tap Install → native dialog → Add → confirm icon on home screen
4. Re-launch from icon → standalone

- [ ] **Step 8: Offline smoke**

After install on iPhone:
1. Browse requisitions list, view a couple of detail pages
2. DevTools throttle → Offline (or airplane mode on real device)
3. Reload app → cached data should still render
4. Verify Offline Banner appears
5. Reconnect → banner auto-hides

- [ ] **Step 9: Update toast smoke**

1. Make a trivial UI change (e.g., add a comment to a file), commit, push
2. Cloudflare Pages auto-redeploys
3. Open existing installed PWA → wait or refresh → Update toast should appear
4. Click Refresh now → page reloads with new build

---

### Close (C1)

| # | Task | Outputs |
|---|---|---|
| **C1** | Update `CLAUDE.md` with PWA section. Update memory `project_pwa_conversion.md` (new). Open PR via GitHub UI per repo push hook. | docs commit, PR opened by user |

- [ ] **Step 1: Update CLAUDE.md**

Add a new section under "Project Overview" or near the mobile architecture section:

```markdown
### PWA architecture (`bom-web` post-2026-04-28)

`bom-web` is now an installable PWA. iOS/iPadOS staff install via Safari "Add to Home Screen" → home screen icon → standalone fullscreen launch.

- **Manifest** at `public/manifest.webmanifest` (name: FPF Quotations, theme: #1e40af).
- **Icons** auto-generated by `@vite-pwa/assets-generator` from `public/icon-source.png` (copied from `bom-mobile/assets/icon.png`).
- **Service worker** at `src/sw.ts` (Workbox `injectManifest` mode). NetworkFirst (5s timeout, 24h TTL) for `/api/(requisitions|customers|items|branches|users|groups)` lists + detail patterns. NetworkOnly for `/api/auth/*`, `/api/notifications`, mutations, SignalR.
- **Install UX:** `<InstallModal>` (iOS/iPadOS Safari, fullscreen, dismissible 30 days) + `<InstallBanner>` (Android Chrome, native prompt). Profile page has permanent "Install" link.
- **Update UX:** `<UpdateToast>` Sonner toast on new SW waiting → "Refresh now" triggers `skipWaiting` + reload.
- **Offline UX:** `<OfflineBanner>` on `navigator.onLine === false`.
- **Logout:** clears `bom-api-list-cache` + `bom-api-detail-cache` to prevent next user seeing prior user's data.
- **Web Push:** P2 + P3 (separate PRs) — VAPID + `PushSubscription` table + permission flow.
```

- [ ] **Step 2: Add memory file**

Create `C:\Users\Administrator\.claude\projects\D--shan-projects-BOM-Price-Approval\memory\project_pwa_conversion.md`:

```markdown
---
name: PWA conversion 2026-04-28 — P1 shipped
description: bom-web converted to installable PWA. P1 (shell + offline) merged. P2 (web push backend) + P3 (web push frontend) pending.
type: project
---

## P1 — PWA Shell & Offline (MERGED)

- Branch `feat/pwa-shell-and-offline` → master @ {SHA}
- Files added: vite-plugin-pwa, manifest.webmanifest, src/sw.ts, src/utils/platform.ts, src/hooks/usePwaInstall.ts, src/hooks/useServiceWorker.ts, 4 PWA components.
- Boilerplate `favicon.svg` + `icons.svg` deleted (replaced with FPF mobile-derived icons).
- Cloudflare Pages preview verified install on iPhone Safari + iPad Safari + Android Chrome.

## P2 — Web Push Backend (pending)
See plan `docs/superpowers/plans/2026-04-28-pwa-p2-web-push-backend.md`.

## P3 — Web Push Frontend (pending)
See plan `docs/superpowers/plans/2026-04-28-pwa-p3-web-push-frontend.md`.

## Related
- Spec: `docs/superpowers/specs/2026-04-28-pwa-conversion-design.md`
- Why: free iOS/iPadOS distribution; no Apple Developer account.
```

Update `MEMORY.md` index with one-line pointer.

- [ ] **Step 3: Open PR**

User opens PR on GitHub UI with title `feat: PWA Phase 1 — shell + offline + install + update UX` and body containing brief summary of changes + the smoke checklist results.

---

## Self-review

**Spec coverage:**
- ✅ Manifest + icons + meta tags (F2-F3)
- ✅ Service worker NetworkFirst caching (S1)
- ✅ Install UX (U1, U2, profile card in W2)
- ✅ Update UX (U3)
- ✅ Offline UX (U4)
- ✅ iPadOS detection (H1)
- ✅ Logout cache clear (W2)
- ✅ Lighthouse + smoke (V1)
- ✅ Boilerplate deletion (F2)
- ✅ Memory + CLAUDE.md update (C1)

**Placeholder scan:** Plan contains exact file paths, complete code blocks, exact test commands. The only `{SHA}` placeholder in C1 step 2 is intentional — gets filled at merge time.

**Type consistency:** `usePwaInstall` returns `{ canPromptInstall, shouldShowIosModal, isInstalled, promptInstall, dismissIosModal }` — used identically by `InstallModal`, `InstallBanner`, profile card. `useServiceWorker` returns `{ updateAvailable, applyUpdate }` — consumed by `UpdateToast`.

**Out-of-scope deferred:**
- VAPID + push subscription endpoints → P2 plan
- Push permission prompt + SW push event listener → P3 plan

---

## Execution mode

Recommend **subagent-driven-development** for F1, S1, V1 (foundation + SW + Lighthouse — high blast radius). **Inline batched** for U1-U4 (similar component pattern, fast iteration).

Subagent dispatch protocol:
- F1, F2, F3, S1, V1: dispatch with strict spec + code review
- H1, H2: single-reviewer (hooks pattern is small + tested)
- U1-U4: batch all 4 components in single inline session, run vitest after each
- W1, W2: inline (small wiring tasks)
- C1: inline (docs only)
