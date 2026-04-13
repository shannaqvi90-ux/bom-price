# Plan 1: Foundation & Auth Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Scaffold the React 19 + Vite web frontend for BOM & Price Approval with a working authentication flow, JWT refresh, role-filtered app shell, dark/light theme, and empty role-specific dashboards.

**Architecture:** Single Vite project at `bom-web/` (sibling of `BomPriceApproval.API/`). TypeScript strict. Tailwind v4 with CSS-variable theming. Zustand for auth + theme (persisted to localStorage). TanStack Query for any server data later. Axios instance with a response interceptor that calls `/auth/refresh` on 401 and retries. React Router v6 with `<ProtectedRoute>` that reads the Zustand auth store and routes by role. Minimal unstyled UI primitives (Shadcn/UI will be introduced in Plan 2 when tables/dialogs are needed).

**Tech Stack:** Vite 6, React 19, TypeScript 5, Tailwind CSS v4, React Router v6, TanStack Query v5, Zustand 5, Axios 1, React Hook Form 7, Zod 3, Framer Motion 11, Vitest 2, React Testing Library 16, lucide-react (icons).

**Backend prerequisites (already satisfied):**
- API running at `http://localhost:7300/api`
- CORS allows origin `http://localhost:5300` with credentials
- `POST /api/auth/login` → `{ accessToken, refreshToken, role, userId, name, branchId }`
- `POST /api/auth/refresh` → same shape
- `POST /api/auth/logout` → 204
- Seeded admin: `admin@test.com` / check `Program.cs` for seed password

**End state:** A developer runs `npm run dev` in `bom-web/`, opens `http://localhost:5300`, is redirected to `/login`, logs in as any seeded role, lands on `/dashboard` which renders a role-specific placeholder inside the app shell (sidebar + topbar), can toggle dark/light theme, can log out, and is redirected back to `/login`. Refreshing the browser restores the session. An expired access token triggers a silent refresh.

---

## File Structure

```
bom-web/
  index.html
  package.json
  tsconfig.json
  tsconfig.node.json
  vite.config.ts
  .gitignore
  src/
    main.tsx
    App.tsx
    index.css
    vite-env.d.ts
    test/
      setup.ts
    types/
      api.ts
    store/
      authStore.ts
      authStore.test.ts
      themeStore.ts
    api/
      axios.ts
      axios.test.ts
      queryClient.ts
    features/
      auth/
        authApi.ts
        LoginPage.tsx
      dashboard/
        SalesDashboard.tsx
        BomDashboard.tsx
        AccountantDashboard.tsx
        MdDashboard.tsx
        AdminDashboard.tsx
        DashboardRouter.tsx
    components/
      layout/
        AppShell.tsx
        Sidebar.tsx
        Topbar.tsx
        ProtectedRoute.tsx
        ProtectedRoute.test.tsx
      ui/
        Button.tsx
        Input.tsx
        Label.tsx
        Card.tsx
    lib/
      cn.ts
```

Every file listed has exactly one responsibility. Stores are separated from API clients; layout components are separated from feature pages; tests live next to the file they cover.

---

## Task 1: Scaffold Vite + React 19 + TypeScript project

**Files:**
- Create: `bom-web/` (new subdirectory at repo root)

- [ ] **Step 1: Scaffold the project**

Run from the repo root (`D:\shan projects\BOM & Price Approval`):

```bash
npm create vite@latest bom-web -- --template react-ts
```

If prompted, confirm. This creates `bom-web/` with a default Vite + React + TS template.

- [ ] **Step 2: Install base dependencies**

```bash
cd bom-web
npm install
```

Expected: `added <N> packages` with no errors. `node_modules/` created.

- [ ] **Step 3: Delete default boilerplate**

Delete these files from `bom-web/src/`:
- `App.css`
- `assets/react.svg`
- (keep `index.css`, `main.tsx`, `App.tsx`, `vite-env.d.ts`)

Delete from `bom-web/public/`:
- `vite.svg`

- [ ] **Step 4: Replace `src/App.tsx` with an empty placeholder**

```tsx
export default function App() {
  return <div>BOM & Price Approval</div>;
}
```

- [ ] **Step 5: Replace `src/main.tsx`**

```tsx
import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import App from "./App";
import "./index.css";

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <App />
  </StrictMode>,
);
```

- [ ] **Step 6: Replace `src/index.css` with a minimal stub**

```css
body {
  margin: 0;
  font-family: system-ui, sans-serif;
}
```

- [ ] **Step 7: Verify it builds and runs**

```bash
npm run build
```

Expected: `built in ...ms` with no errors. Output in `bom-web/dist/`.

- [ ] **Step 8: Commit**

```bash
cd ..
git add bom-web/
git commit -m "feat(web): scaffold Vite + React 19 + TypeScript project"
```

---

## Task 2: Install runtime and dev dependencies

**Files:**
- Modify: `bom-web/package.json`

- [ ] **Step 1: Install runtime dependencies**

From `bom-web/`:

```bash
npm install react-router-dom@^6.27.0 @tanstack/react-query@^5.59.0 zustand@^5.0.0 axios@^1.7.7 react-hook-form@^7.53.0 zod@^3.23.8 @hookform/resolvers@^3.9.0 framer-motion@^11.11.0 lucide-react@^0.454.0 clsx@^2.1.1 tailwind-merge@^2.5.4
```

- [ ] **Step 2: Install Tailwind v4 and dev dependencies**

```bash
npm install -D tailwindcss@^4.0.0 @tailwindcss/vite@^4.0.0 vitest@^2.1.0 @testing-library/react@^16.0.1 @testing-library/jest-dom@^6.5.0 @testing-library/user-event@^14.5.2 jsdom@^25.0.1 @types/node@^22.7.0
```

Expected: all installs succeed. `package.json` should now list the new deps.

- [ ] **Step 3: Sanity-check the version file**

Open `bom-web/package.json`. Confirm `react` is `^19.x`, and all deps above are listed with expected major versions. If Vite scaffolded React 18, upgrade:

```bash
npm install react@^19.0.0 react-dom@^19.0.0 @types/react@^19.0.0 @types/react-dom@^19.0.0
```

- [ ] **Step 4: Commit**

```bash
cd ..
git add bom-web/package.json bom-web/package-lock.json
git commit -m "feat(web): add runtime and dev dependencies"
```

---

## Task 3: Configure Vite, TypeScript, Tailwind, and Vitest

**Files:**
- Modify: `bom-web/vite.config.ts`
- Modify: `bom-web/tsconfig.json`
- Modify: `bom-web/tsconfig.app.json` (if present from scaffold)
- Modify: `bom-web/src/index.css`
- Create: `bom-web/src/test/setup.ts`
- Modify: `bom-web/package.json` (scripts)
- Create: `bom-web/src/lib/cn.ts`

- [ ] **Step 1: Replace `bom-web/vite.config.ts`**

```ts
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";
import path from "node:path";

export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: {
      "@": path.resolve(__dirname, "./src"),
    },
  },
  server: {
    port: 5300,
    strictPort: true,
  },
  test: {
    globals: true,
    environment: "jsdom",
    setupFiles: ["./src/test/setup.ts"],
    css: true,
  },
});
```

Note: Vitest shares the Vite config via the `test` key. TypeScript will complain about `test` not being on the Vite config type; add a triple-slash reference in step 2 to satisfy it.

- [ ] **Step 2: Add Vitest types reference at the top of `vite.config.ts`**

Prepend this as the very first line:

```ts
/// <reference types="vitest/config" />
```

- [ ] **Step 3: Update `tsconfig.json` (root) to add path alias**

Replace the root `bom-web/tsconfig.json` contents with:

```json
{
  "files": [],
  "references": [
    { "path": "./tsconfig.app.json" },
    { "path": "./tsconfig.node.json" }
  ],
  "compilerOptions": {
    "baseUrl": ".",
    "paths": {
      "@/*": ["./src/*"]
    }
  }
}
```

- [ ] **Step 4: Update `tsconfig.app.json` to add path alias and Vitest globals**

Open `bom-web/tsconfig.app.json`. In `compilerOptions`, add:

```json
"baseUrl": ".",
"paths": { "@/*": ["./src/*"] },
"types": ["vitest/globals", "@testing-library/jest-dom"]
```

Keep all existing options (strict, module, target, etc.) intact.

- [ ] **Step 5: Replace `bom-web/src/index.css` with Tailwind v4 + theme CSS variables**

```css
@import "tailwindcss";

@custom-variant dark (&:where(.dark, .dark *));

:root {
  --background: #ffffff;
  --foreground: #0f172a;
  --card: #ffffff;
  --card-foreground: #0f172a;
  --primary: #4f46e5;
  --primary-foreground: #ffffff;
  --muted: #f1f5f9;
  --muted-foreground: #64748b;
  --border: #e2e8f0;
  --input: #e2e8f0;
  --ring: #4f46e5;
  --destructive: #dc2626;
  --sidebar: #f8fafc;
  --sidebar-foreground: #0f172a;
}

.dark {
  --background: #020617;
  --foreground: #f8fafc;
  --card: #0f172a;
  --card-foreground: #f8fafc;
  --primary: #6366f1;
  --primary-foreground: #ffffff;
  --muted: #1e293b;
  --muted-foreground: #94a3b8;
  --border: #1e293b;
  --input: #1e293b;
  --ring: #6366f1;
  --destructive: #ef4444;
  --sidebar: #0b1120;
  --sidebar-foreground: #f8fafc;
}

@theme inline {
  --color-background: var(--background);
  --color-foreground: var(--foreground);
  --color-card: var(--card);
  --color-card-foreground: var(--card-foreground);
  --color-primary: var(--primary);
  --color-primary-foreground: var(--primary-foreground);
  --color-muted: var(--muted);
  --color-muted-foreground: var(--muted-foreground);
  --color-border: var(--border);
  --color-input: var(--input);
  --color-ring: var(--ring);
  --color-destructive: var(--destructive);
  --color-sidebar: var(--sidebar);
  --color-sidebar-foreground: var(--sidebar-foreground);
}

html,
body,
#root {
  height: 100%;
}

body {
  margin: 0;
  background-color: var(--color-background);
  color: var(--color-foreground);
  font-family: system-ui, -apple-system, sans-serif;
  -webkit-font-smoothing: antialiased;
}
```

- [ ] **Step 6: Create `bom-web/src/test/setup.ts`**

```ts
import "@testing-library/jest-dom/vitest";
import { cleanup } from "@testing-library/react";
import { afterEach } from "vitest";

afterEach(() => {
  cleanup();
});
```

- [ ] **Step 7: Create `bom-web/src/lib/cn.ts`**

```ts
import { clsx, type ClassValue } from "clsx";
import { twMerge } from "tailwind-merge";

export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs));
}
```

- [ ] **Step 8: Add test script to `bom-web/package.json`**

Under `scripts`, add:

```json
"test": "vitest run",
"test:watch": "vitest"
```

Keep existing `dev`, `build`, `preview`, `lint` scripts.

- [ ] **Step 9: Verify build still passes**

```bash
cd bom-web && npm run build
```

Expected: build succeeds. Tailwind v4 plugin logs no errors.

- [ ] **Step 10: Commit**

```bash
cd ..
git add bom-web/
git commit -m "feat(web): configure Vite, Tailwind v4, Vitest, and path aliases"
```

---

## Task 4: Define API types mirroring backend DTOs

**Files:**
- Create: `bom-web/src/types/api.ts`

- [ ] **Step 1: Write `src/types/api.ts`**

```ts
export type UserRole =
  | "SalesPerson"
  | "BomCreator"
  | "Accountant"
  | "ManagingDirector"
  | "Admin";

export interface AuthUser {
  userId: number;
  name: string;
  role: UserRole;
  branchId: number | null;
}

export interface LoginRequest {
  email: string;
  password: string;
}

export interface LoginResponse {
  accessToken: string;
  refreshToken: string;
  role: UserRole;
  userId: number;
  name: string;
  branchId: number | null;
}

export interface RefreshRequest {
  refreshToken: string;
}

export interface ApiError {
  message: string;
}
```

- [ ] **Step 2: Commit**

```bash
cd ..
git add bom-web/src/types/api.ts
git commit -m "feat(web): add API types for auth DTOs"
```

---

## Task 5: Write failing test for authStore

**Files:**
- Create: `bom-web/src/store/authStore.test.ts`

- [ ] **Step 1: Write `src/store/authStore.test.ts`**

```ts
import { beforeEach, describe, expect, it } from "vitest";
import { useAuthStore } from "./authStore";

describe("authStore", () => {
  beforeEach(() => {
    useAuthStore.getState().logout();
    localStorage.clear();
  });

  it("starts unauthenticated", () => {
    const state = useAuthStore.getState();
    expect(state.user).toBeNull();
    expect(state.accessToken).toBeNull();
    expect(state.refreshToken).toBeNull();
    expect(state.isAuthenticated()).toBe(false);
  });

  it("setSession stores tokens and user and marks authenticated", () => {
    useAuthStore.getState().setSession({
      accessToken: "at.1",
      refreshToken: "rt.1",
      role: "SalesPerson",
      userId: 5,
      name: "Alice",
      branchId: 2,
    });

    const state = useAuthStore.getState();
    expect(state.accessToken).toBe("at.1");
    expect(state.refreshToken).toBe("rt.1");
    expect(state.user).toEqual({
      userId: 5,
      name: "Alice",
      role: "SalesPerson",
      branchId: 2,
    });
    expect(state.isAuthenticated()).toBe(true);
  });

  it("updateTokens replaces tokens without touching user", () => {
    useAuthStore.getState().setSession({
      accessToken: "at.1",
      refreshToken: "rt.1",
      role: "Admin",
      userId: 1,
      name: "Admin",
      branchId: null,
    });

    useAuthStore.getState().updateTokens("at.2", "rt.2");

    const state = useAuthStore.getState();
    expect(state.accessToken).toBe("at.2");
    expect(state.refreshToken).toBe("rt.2");
    expect(state.user?.userId).toBe(1);
  });

  it("logout clears everything", () => {
    useAuthStore.getState().setSession({
      accessToken: "at.1",
      refreshToken: "rt.1",
      role: "Admin",
      userId: 1,
      name: "Admin",
      branchId: null,
    });

    useAuthStore.getState().logout();

    const state = useAuthStore.getState();
    expect(state.user).toBeNull();
    expect(state.accessToken).toBeNull();
    expect(state.refreshToken).toBeNull();
    expect(state.isAuthenticated()).toBe(false);
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

```bash
cd bom-web && npm test
```

Expected: FAIL — `Cannot find module './authStore'`.

---

## Task 6: Implement authStore

**Files:**
- Create: `bom-web/src/store/authStore.ts`

- [ ] **Step 1: Write `src/store/authStore.ts`**

```ts
import { create } from "zustand";
import { persist, createJSONStorage } from "zustand/middleware";
import type { AuthUser, LoginResponse } from "@/types/api";

interface AuthState {
  user: AuthUser | null;
  accessToken: string | null;
  refreshToken: string | null;
  setSession: (res: LoginResponse) => void;
  updateTokens: (accessToken: string, refreshToken: string) => void;
  logout: () => void;
  isAuthenticated: () => boolean;
}

export const useAuthStore = create<AuthState>()(
  persist(
    (set, get) => ({
      user: null,
      accessToken: null,
      refreshToken: null,
      setSession: (res) =>
        set({
          accessToken: res.accessToken,
          refreshToken: res.refreshToken,
          user: {
            userId: res.userId,
            name: res.name,
            role: res.role,
            branchId: res.branchId,
          },
        }),
      updateTokens: (accessToken, refreshToken) =>
        set({ accessToken, refreshToken }),
      logout: () =>
        set({ user: null, accessToken: null, refreshToken: null }),
      isAuthenticated: () => get().accessToken !== null && get().user !== null,
    }),
    {
      name: "bom-auth",
      storage: createJSONStorage(() => localStorage),
      partialize: (state) => ({
        user: state.user,
        accessToken: state.accessToken,
        refreshToken: state.refreshToken,
      }),
    },
  ),
);
```

- [ ] **Step 2: Run test to verify it passes**

```bash
npm test
```

Expected: PASS — 4 tests green.

- [ ] **Step 3: Commit**

```bash
cd ..
git add bom-web/src/store/authStore.ts bom-web/src/store/authStore.test.ts
git commit -m "feat(web): add authStore with Zustand persist"
```

---

## Task 7: Implement themeStore

**Files:**
- Create: `bom-web/src/store/themeStore.ts`

- [ ] **Step 1: Write `src/store/themeStore.ts`**

```ts
import { create } from "zustand";
import { persist, createJSONStorage } from "zustand/middleware";

type Theme = "dark" | "light";

interface ThemeState {
  theme: Theme;
  toggle: () => void;
  apply: () => void;
}

function applyThemeClass(theme: Theme) {
  const root = document.documentElement;
  if (theme === "dark") root.classList.add("dark");
  else root.classList.remove("dark");
}

export const useThemeStore = create<ThemeState>()(
  persist(
    (set, get) => ({
      theme: "dark",
      toggle: () => {
        const next: Theme = get().theme === "dark" ? "light" : "dark";
        applyThemeClass(next);
        set({ theme: next });
      },
      apply: () => applyThemeClass(get().theme),
    }),
    {
      name: "bom-theme",
      storage: createJSONStorage(() => localStorage),
    },
  ),
);
```

- [ ] **Step 2: Commit**

```bash
cd ..
git add bom-web/src/store/themeStore.ts
git commit -m "feat(web): add themeStore with persisted dark/light toggle"
```

---

## Task 8: Write failing test for axios refresh interceptor

**Files:**
- Create: `bom-web/src/api/axios.test.ts`

- [ ] **Step 1: Write `src/api/axios.test.ts`**

```ts
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { AxiosAdapter, AxiosRequestConfig } from "axios";
import { useAuthStore } from "@/store/authStore";

vi.mock("axios", async () => {
  const actual = await vi.importActual<typeof import("axios")>("axios");
  return actual;
});

describe("axios client with refresh interceptor", () => {
  beforeEach(() => {
    useAuthStore.getState().logout();
    vi.resetModules();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("attaches Authorization header from authStore on requests", async () => {
    useAuthStore.getState().setSession({
      accessToken: "at.original",
      refreshToken: "rt.original",
      role: "Admin",
      userId: 1,
      name: "Admin",
      branchId: null,
    });

    let seenAuth: string | undefined;
    const adapter: AxiosAdapter = async (config: AxiosRequestConfig) => {
      seenAuth = config.headers?.Authorization as string | undefined;
      return {
        data: { ok: true },
        status: 200,
        statusText: "OK",
        headers: {},
        config: config as never,
      };
    };

    const { api } = await import("./axios");
    await api.get("/ping", { adapter });

    expect(seenAuth).toBe("Bearer at.original");
  });

  it("on 401 calls /auth/refresh, updates tokens, and retries the original request", async () => {
    useAuthStore.getState().setSession({
      accessToken: "at.expired",
      refreshToken: "rt.valid",
      role: "Admin",
      userId: 1,
      name: "Admin",
      branchId: null,
    });

    const calls: Array<{ url?: string; auth?: string; data?: unknown }> = [];
    let requestCount = 0;

    const adapter: AxiosAdapter = async (config: AxiosRequestConfig) => {
      requestCount += 1;
      calls.push({
        url: config.url,
        auth: config.headers?.Authorization as string | undefined,
        data: config.data,
      });

      if (config.url?.endsWith("/auth/refresh")) {
        return {
          data: {
            accessToken: "at.new",
            refreshToken: "rt.new",
            role: "Admin",
            userId: 1,
            name: "Admin",
            branchId: null,
          },
          status: 200,
          statusText: "OK",
          headers: {},
          config: config as never,
        };
      }

      if (requestCount === 1) {
        const err = new Error("Unauthorized") as Error & {
          response?: unknown;
          config?: unknown;
          isAxiosError?: boolean;
        };
        err.isAxiosError = true;
        err.config = config;
        err.response = {
          status: 401,
          statusText: "Unauthorized",
          data: { message: "expired" },
          headers: {},
          config,
        };
        throw err;
      }

      return {
        data: { ok: true },
        status: 200,
        statusText: "OK",
        headers: {},
        config: config as never,
      };
    };

    const { api } = await import("./axios");
    const result = await api.get("/items", { adapter });

    expect(result.data).toEqual({ ok: true });
    expect(useAuthStore.getState().accessToken).toBe("at.new");
    expect(useAuthStore.getState().refreshToken).toBe("rt.new");

    const refreshCall = calls.find((c) => c.url?.endsWith("/auth/refresh"));
    expect(refreshCall).toBeDefined();
    expect(refreshCall?.data).toContain("rt.valid");

    const retry = calls.find(
      (c) => c.url === "/items" && c.auth === "Bearer at.new",
    );
    expect(retry).toBeDefined();
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

```bash
cd bom-web && npm test
```

Expected: FAIL — `Cannot find module './axios'`.

---

## Task 9: Implement axios instance with JWT refresh interceptor

**Files:**
- Create: `bom-web/src/api/axios.ts`

- [ ] **Step 1: Write `src/api/axios.ts`**

```ts
import axios, {
  AxiosError,
  type AxiosRequestConfig,
  type InternalAxiosRequestConfig,
} from "axios";
import { useAuthStore } from "@/store/authStore";
import type { LoginResponse } from "@/types/api";

export const API_BASE_URL = "http://localhost:7300/api";

export const api = axios.create({
  baseURL: API_BASE_URL,
  headers: { "Content-Type": "application/json" },
});

api.interceptors.request.use((config) => {
  const token = useAuthStore.getState().accessToken;
  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

interface RetryConfig extends InternalAxiosRequestConfig {
  _retried?: boolean;
}

let refreshInFlight: Promise<string | null> | null = null;

async function performRefresh(): Promise<string | null> {
  const refreshToken = useAuthStore.getState().refreshToken;
  if (!refreshToken) return null;

  try {
    const resp = await axios.post<LoginResponse>(
      `${API_BASE_URL}/auth/refresh`,
      { refreshToken },
      { headers: { "Content-Type": "application/json" } },
    );
    useAuthStore
      .getState()
      .updateTokens(resp.data.accessToken, resp.data.refreshToken);
    return resp.data.accessToken;
  } catch {
    useAuthStore.getState().logout();
    return null;
  }
}

api.interceptors.response.use(
  (response) => response,
  async (error: AxiosError) => {
    const original = error.config as RetryConfig | undefined;

    if (
      error.response?.status !== 401 ||
      !original ||
      original._retried ||
      original.url?.endsWith("/auth/refresh") ||
      original.url?.endsWith("/auth/login")
    ) {
      return Promise.reject(error);
    }

    original._retried = true;

    if (!refreshInFlight) {
      refreshInFlight = performRefresh().finally(() => {
        refreshInFlight = null;
      });
    }

    const newToken = await refreshInFlight;
    if (!newToken) {
      return Promise.reject(error);
    }

    original.headers.Authorization = `Bearer ${newToken}`;
    return api.request(original as AxiosRequestConfig);
  },
);
```

**Note on the test adapter:** axios calls the adapter for every request, including retries and the refresh call. The test passes an adapter to the outer `api.get` call, but the interceptor's refresh goes through a separate `axios.post` — meaning the refresh bypasses the injected adapter. To make the test work, refactor the refresh to go through `api` (not standalone `axios`), guarded by the URL check that already skips the retry.

Replace `performRefresh` with this version:

```ts
async function performRefresh(): Promise<string | null> {
  const refreshToken = useAuthStore.getState().refreshToken;
  if (!refreshToken) return null;

  try {
    const resp = await api.post<LoginResponse>(
      `/auth/refresh`,
      { refreshToken },
    );
    useAuthStore
      .getState()
      .updateTokens(resp.data.accessToken, resp.data.refreshToken);
    return resp.data.accessToken;
  } catch {
    useAuthStore.getState().logout();
    return null;
  }
}
```

The interceptor's URL guard (`original.url?.endsWith("/auth/refresh")`) prevents infinite loops if the refresh itself returns 401.

- [ ] **Step 2: Run tests to verify both pass**

```bash
npm test
```

Expected: PASS — 2 axios tests + 4 authStore tests all green.

- [ ] **Step 3: Commit**

```bash
cd ..
git add bom-web/src/api/axios.ts bom-web/src/api/axios.test.ts
git commit -m "feat(web): add axios client with JWT refresh interceptor"
```

---

## Task 10: TanStack Query client and authApi hooks

**Files:**
- Create: `bom-web/src/api/queryClient.ts`
- Create: `bom-web/src/features/auth/authApi.ts`

- [ ] **Step 1: Write `src/api/queryClient.ts`**

```ts
import { QueryClient } from "@tanstack/react-query";

export const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      gcTime: 5 * 60_000,
      retry: 1,
      refetchOnWindowFocus: false,
    },
    mutations: {
      retry: 0,
    },
  },
});
```

- [ ] **Step 2: Write `src/features/auth/authApi.ts`**

```ts
import { useMutation } from "@tanstack/react-query";
import { api } from "@/api/axios";
import { useAuthStore } from "@/store/authStore";
import type { LoginRequest, LoginResponse } from "@/types/api";

async function loginRequest(req: LoginRequest): Promise<LoginResponse> {
  const resp = await api.post<LoginResponse>("/auth/login", req);
  return resp.data;
}

export function useLogin() {
  const setSession = useAuthStore((s) => s.setSession);
  return useMutation({
    mutationFn: loginRequest,
    onSuccess: (data) => setSession(data),
  });
}

async function logoutRequest(refreshToken: string): Promise<void> {
  await api.post("/auth/logout", { refreshToken });
}

export function useLogout() {
  const logout = useAuthStore((s) => s.logout);
  const refreshToken = useAuthStore((s) => s.refreshToken);
  return useMutation({
    mutationFn: async () => {
      if (refreshToken) {
        try {
          await logoutRequest(refreshToken);
        } catch {
          // best-effort; local logout still proceeds
        }
      }
    },
    onSettled: () => logout(),
  });
}
```

- [ ] **Step 3: Commit**

```bash
cd ..
git add bom-web/src/api/queryClient.ts bom-web/src/features/auth/authApi.ts
git commit -m "feat(web): add query client and auth mutations"
```

---

## Task 11: Minimal UI primitives (Button, Input, Label, Card)

**Files:**
- Create: `bom-web/src/components/ui/Button.tsx`
- Create: `bom-web/src/components/ui/Input.tsx`
- Create: `bom-web/src/components/ui/Label.tsx`
- Create: `bom-web/src/components/ui/Card.tsx`

- [ ] **Step 1: Write `src/components/ui/Button.tsx`**

```tsx
import { forwardRef, type ButtonHTMLAttributes } from "react";
import { cn } from "@/lib/cn";

type Variant = "primary" | "ghost" | "destructive";

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: Variant;
}

const styles: Record<Variant, string> = {
  primary:
    "bg-primary text-primary-foreground hover:opacity-90 disabled:opacity-50",
  ghost:
    "bg-transparent text-foreground hover:bg-muted disabled:opacity-50",
  destructive:
    "bg-destructive text-white hover:opacity-90 disabled:opacity-50",
};

export const Button = forwardRef<HTMLButtonElement, ButtonProps>(
  ({ className, variant = "primary", ...props }, ref) => (
    <button
      ref={ref}
      className={cn(
        "inline-flex items-center justify-center gap-2 rounded-md px-4 py-2 text-sm font-medium transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring",
        styles[variant],
        className,
      )}
      {...props}
    />
  ),
);
Button.displayName = "Button";
```

- [ ] **Step 2: Write `src/components/ui/Input.tsx`**

```tsx
import { forwardRef, type InputHTMLAttributes } from "react";
import { cn } from "@/lib/cn";

export const Input = forwardRef<
  HTMLInputElement,
  InputHTMLAttributes<HTMLInputElement>
>(({ className, ...props }, ref) => (
  <input
    ref={ref}
    className={cn(
      "flex h-10 w-full rounded-md border border-input bg-background px-3 py-2 text-sm text-foreground placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:cursor-not-allowed disabled:opacity-50",
      className,
    )}
    {...props}
  />
));
Input.displayName = "Input";
```

- [ ] **Step 3: Write `src/components/ui/Label.tsx`**

```tsx
import { forwardRef, type LabelHTMLAttributes } from "react";
import { cn } from "@/lib/cn";

export const Label = forwardRef<
  HTMLLabelElement,
  LabelHTMLAttributes<HTMLLabelElement>
>(({ className, ...props }, ref) => (
  <label
    ref={ref}
    className={cn(
      "text-sm font-medium text-foreground leading-none",
      className,
    )}
    {...props}
  />
));
Label.displayName = "Label";
```

- [ ] **Step 4: Write `src/components/ui/Card.tsx`**

```tsx
import { type HTMLAttributes } from "react";
import { cn } from "@/lib/cn";

export function Card({
  className,
  ...props
}: HTMLAttributes<HTMLDivElement>) {
  return (
    <div
      className={cn(
        "rounded-lg border border-border bg-card text-card-foreground shadow-sm",
        className,
      )}
      {...props}
    />
  );
}

export function CardHeader(props: HTMLAttributes<HTMLDivElement>) {
  return <div className={cn("p-6 pb-2", props.className)} {...props} />;
}

export function CardContent(props: HTMLAttributes<HTMLDivElement>) {
  return <div className={cn("p-6 pt-2", props.className)} {...props} />;
}

export function CardTitle(props: HTMLAttributes<HTMLHeadingElement>) {
  return (
    <h2
      className={cn("text-xl font-semibold tracking-tight", props.className)}
      {...props}
    />
  );
}
```

- [ ] **Step 5: Commit**

```bash
cd ..
git add bom-web/src/components/ui/
git commit -m "feat(web): add minimal UI primitives (Button, Input, Label, Card)"
```

---

## Task 12: LoginPage with React Hook Form and Zod

**Files:**
- Create: `bom-web/src/features/auth/LoginPage.tsx`

- [ ] **Step 1: Write `src/features/auth/LoginPage.tsx`**

```tsx
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { useNavigate, Navigate } from "react-router-dom";
import { useLogin } from "./authApi";
import { useAuthStore } from "@/store/authStore";
import { Button } from "@/components/ui/Button";
import { Input } from "@/components/ui/Input";
import { Label } from "@/components/ui/Label";
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
} from "@/components/ui/Card";

const schema = z.object({
  email: z.string().email("Enter a valid email"),
  password: z.string().min(1, "Password is required"),
});

type FormValues = z.infer<typeof schema>;

export default function LoginPage() {
  const isAuthed = useAuthStore((s) => s.isAuthenticated());
  const navigate = useNavigate();
  const login = useLogin();

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { email: "", password: "" },
  });

  if (isAuthed) return <Navigate to="/dashboard" replace />;

  const onSubmit = handleSubmit(async (values) => {
    try {
      await login.mutateAsync(values);
      navigate("/dashboard", { replace: true });
    } catch {
      // error surfaced via login.error below
    }
  });

  const serverError = login.error
    ? ((login.error as { response?: { data?: { message?: string } } })
        .response?.data?.message ?? "Login failed")
    : null;

  return (
    <div className="flex min-h-screen items-center justify-center bg-background px-4">
      <Card className="w-full max-w-sm">
        <CardHeader>
          <CardTitle>Sign in</CardTitle>
          <p className="text-sm text-muted-foreground mt-1">
            BOM & Price Approval
          </p>
        </CardHeader>
        <CardContent>
          <form onSubmit={onSubmit} className="space-y-4" noValidate>
            <div className="space-y-2">
              <Label htmlFor="email">Email</Label>
              <Input
                id="email"
                type="email"
                autoComplete="email"
                {...register("email")}
              />
              {errors.email && (
                <p className="text-xs text-destructive">
                  {errors.email.message}
                </p>
              )}
            </div>
            <div className="space-y-2">
              <Label htmlFor="password">Password</Label>
              <Input
                id="password"
                type="password"
                autoComplete="current-password"
                {...register("password")}
              />
              {errors.password && (
                <p className="text-xs text-destructive">
                  {errors.password.message}
                </p>
              )}
            </div>
            {serverError && (
              <p className="text-sm text-destructive">{serverError}</p>
            )}
            <Button
              type="submit"
              className="w-full"
              disabled={isSubmitting || login.isPending}
            >
              {login.isPending ? "Signing in…" : "Sign in"}
            </Button>
          </form>
        </CardContent>
      </Card>
    </div>
  );
}
```

- [ ] **Step 2: Commit**

```bash
cd ..
git add bom-web/src/features/auth/LoginPage.tsx
git commit -m "feat(web): add LoginPage with RHF + Zod validation"
```

---

## Task 13: Write failing test for ProtectedRoute

**Files:**
- Create: `bom-web/src/components/layout/ProtectedRoute.test.tsx`

- [ ] **Step 1: Write `src/components/layout/ProtectedRoute.test.tsx`**

```tsx
import { render, screen } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { beforeEach, describe, expect, it } from "vitest";
import { ProtectedRoute } from "./ProtectedRoute";
import { useAuthStore } from "@/store/authStore";
import type { UserRole } from "@/types/api";

function renderAt(path: string, allow?: UserRole[]) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/login" element={<div>LOGIN</div>} />
        <Route path="/dashboard" element={<div>DASH</div>} />
        <Route
          path="/admin"
          element={
            <ProtectedRoute allow={allow}>
              <div>ADMIN</div>
            </ProtectedRoute>
          }
        />
      </Routes>
    </MemoryRouter>,
  );
}

describe("ProtectedRoute", () => {
  beforeEach(() => {
    useAuthStore.getState().logout();
  });

  it("redirects to /login when unauthenticated", () => {
    renderAt("/admin");
    expect(screen.getByText("LOGIN")).toBeInTheDocument();
  });

  it("renders children when authenticated and no role restriction", () => {
    useAuthStore.getState().setSession({
      accessToken: "at",
      refreshToken: "rt",
      role: "Admin",
      userId: 1,
      name: "A",
      branchId: null,
    });
    renderAt("/admin");
    expect(screen.getByText("ADMIN")).toBeInTheDocument();
  });

  it("redirects to /dashboard when role not in allow list", () => {
    useAuthStore.getState().setSession({
      accessToken: "at",
      refreshToken: "rt",
      role: "SalesPerson",
      userId: 1,
      name: "A",
      branchId: 1,
    });
    renderAt("/admin", ["Admin"]);
    expect(screen.getByText("DASH")).toBeInTheDocument();
  });

  it("renders children when role is in allow list", () => {
    useAuthStore.getState().setSession({
      accessToken: "at",
      refreshToken: "rt",
      role: "Admin",
      userId: 1,
      name: "A",
      branchId: null,
    });
    renderAt("/admin", ["Admin"]);
    expect(screen.getByText("ADMIN")).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

```bash
cd bom-web && npm test
```

Expected: FAIL — `Cannot find module './ProtectedRoute'`.

---

## Task 14: Implement ProtectedRoute

**Files:**
- Create: `bom-web/src/components/layout/ProtectedRoute.tsx`

- [ ] **Step 1: Write `src/components/layout/ProtectedRoute.tsx`**

```tsx
import { type ReactNode } from "react";
import { Navigate } from "react-router-dom";
import { useAuthStore } from "@/store/authStore";
import type { UserRole } from "@/types/api";

interface Props {
  children: ReactNode;
  allow?: UserRole[];
}

export function ProtectedRoute({ children, allow }: Props) {
  const user = useAuthStore((s) => s.user);
  const isAuthed = useAuthStore((s) => s.isAuthenticated());

  if (!isAuthed || !user) {
    return <Navigate to="/login" replace />;
  }

  if (allow && !allow.includes(user.role)) {
    return <Navigate to="/dashboard" replace />;
  }

  return <>{children}</>;
}
```

- [ ] **Step 2: Run test to verify it passes**

```bash
npm test
```

Expected: PASS — all auth + axios + ProtectedRoute tests green.

- [ ] **Step 3: Commit**

```bash
cd ..
git add bom-web/src/components/layout/ProtectedRoute.tsx bom-web/src/components/layout/ProtectedRoute.test.tsx
git commit -m "feat(web): add ProtectedRoute with role gating"
```

---

## Task 15: Sidebar with role-filtered nav and collapse

**Files:**
- Create: `bom-web/src/components/layout/Sidebar.tsx`

- [ ] **Step 1: Write `src/components/layout/Sidebar.tsx`**

```tsx
import { useEffect, useState } from "react";
import { NavLink } from "react-router-dom";
import { motion } from "framer-motion";
import {
  LayoutDashboard,
  FileText,
  Bell,
  Users,
  Building2,
  Coins,
  Package,
  ChevronLeft,
  ChevronRight,
} from "lucide-react";
import { useAuthStore } from "@/store/authStore";
import type { UserRole } from "@/types/api";
import { cn } from "@/lib/cn";

interface NavItem {
  to: string;
  label: string;
  icon: React.ComponentType<{ className?: string }>;
  roles?: UserRole[];
}

const NAV_ITEMS: NavItem[] = [
  { to: "/dashboard", label: "Dashboard", icon: LayoutDashboard },
  {
    to: "/requisitions",
    label: "Requisitions",
    icon: FileText,
    roles: ["SalesPerson", "BomCreator", "Accountant", "ManagingDirector"],
  },
  { to: "/notifications", label: "Notifications", icon: Bell },
  { to: "/admin/users", label: "Users", icon: Users, roles: ["Admin"] },
  {
    to: "/admin/branches",
    label: "Branches",
    icon: Building2,
    roles: ["Admin"],
  },
  {
    to: "/admin/exchange-rates",
    label: "Exchange Rates",
    icon: Coins,
    roles: ["Admin", "Accountant"],
  },
  { to: "/admin/items", label: "Items", icon: Package, roles: ["Admin"] },
];

const STORAGE_KEY = "bom-sidebar-collapsed";

export function Sidebar() {
  const user = useAuthStore((s) => s.user);
  const [collapsed, setCollapsed] = useState<boolean>(() => {
    return localStorage.getItem(STORAGE_KEY) === "true";
  });

  useEffect(() => {
    localStorage.setItem(STORAGE_KEY, String(collapsed));
  }, [collapsed]);

  const visible = NAV_ITEMS.filter(
    (item) => !item.roles || (user && item.roles.includes(user.role)),
  );

  return (
    <motion.aside
      initial={false}
      animate={{ width: collapsed ? 60 : 220 }}
      transition={{ type: "spring", stiffness: 260, damping: 30 }}
      className="flex flex-col border-r border-border bg-sidebar text-sidebar-foreground"
    >
      <div className="flex h-14 items-center px-4 border-b border-border">
        {!collapsed && (
          <span className="text-sm font-semibold tracking-tight">
            BOM & Price
          </span>
        )}
      </div>
      <nav className="flex-1 overflow-y-auto py-2">
        {visible.map(({ to, label, icon: Icon }) => (
          <NavLink
            key={to}
            to={to}
            className={({ isActive }) =>
              cn(
                "flex items-center gap-3 px-4 py-2 mx-2 rounded-md text-sm transition-colors",
                isActive
                  ? "bg-primary text-primary-foreground"
                  : "hover:bg-muted",
              )
            }
            title={collapsed ? label : undefined}
          >
            <Icon className="h-4 w-4 shrink-0" />
            {!collapsed && <span>{label}</span>}
          </NavLink>
        ))}
      </nav>
      <button
        type="button"
        onClick={() => setCollapsed((c) => !c)}
        className="flex h-10 items-center justify-center border-t border-border hover:bg-muted"
        aria-label={collapsed ? "Expand sidebar" : "Collapse sidebar"}
      >
        {collapsed ? (
          <ChevronRight className="h-4 w-4" />
        ) : (
          <ChevronLeft className="h-4 w-4" />
        )}
      </button>
    </motion.aside>
  );
}
```

- [ ] **Step 2: Commit**

```bash
cd ..
git add bom-web/src/components/layout/Sidebar.tsx
git commit -m "feat(web): add collapsible Sidebar with role-filtered nav"
```

---

## Task 16: Topbar with theme toggle and logout

**Files:**
- Create: `bom-web/src/components/layout/Topbar.tsx`

- [ ] **Step 1: Write `src/components/layout/Topbar.tsx`**

```tsx
import { useNavigate } from "react-router-dom";
import { Moon, Sun, LogOut } from "lucide-react";
import { useAuthStore } from "@/store/authStore";
import { useThemeStore } from "@/store/themeStore";
import { useLogout } from "@/features/auth/authApi";
import { Button } from "@/components/ui/Button";

export function Topbar() {
  const user = useAuthStore((s) => s.user);
  const theme = useThemeStore((s) => s.theme);
  const toggleTheme = useThemeStore((s) => s.toggle);
  const logout = useLogout();
  const navigate = useNavigate();

  const onLogout = async () => {
    await logout.mutateAsync();
    navigate("/login", { replace: true });
  };

  return (
    <header className="flex h-14 items-center justify-between border-b border-border px-6 bg-background">
      <div className="text-sm text-muted-foreground">
        {user && (
          <>
            Signed in as <span className="text-foreground">{user.name}</span>
            <span className="mx-2">·</span>
            <span>{user.role}</span>
          </>
        )}
      </div>
      <div className="flex items-center gap-2">
        <Button
          variant="ghost"
          onClick={toggleTheme}
          aria-label="Toggle theme"
        >
          {theme === "dark" ? (
            <Sun className="h-4 w-4" />
          ) : (
            <Moon className="h-4 w-4" />
          )}
        </Button>
        <Button variant="ghost" onClick={onLogout} aria-label="Log out">
          <LogOut className="h-4 w-4" />
          <span className="ml-1 text-sm">Log out</span>
        </Button>
      </div>
    </header>
  );
}
```

- [ ] **Step 2: Commit**

```bash
cd ..
git add bom-web/src/components/layout/Topbar.tsx
git commit -m "feat(web): add Topbar with theme toggle and logout"
```

---

## Task 17: AppShell layout

**Files:**
- Create: `bom-web/src/components/layout/AppShell.tsx`

- [ ] **Step 1: Write `src/components/layout/AppShell.tsx`**

```tsx
import { Outlet } from "react-router-dom";
import { Sidebar } from "./Sidebar";
import { Topbar } from "./Topbar";

export function AppShell() {
  return (
    <div className="flex h-screen bg-background text-foreground">
      <Sidebar />
      <div className="flex flex-1 flex-col overflow-hidden">
        <Topbar />
        <main className="flex-1 overflow-y-auto p-6">
          <Outlet />
        </main>
      </div>
    </div>
  );
}
```

- [ ] **Step 2: Commit**

```bash
cd ..
git add bom-web/src/components/layout/AppShell.tsx
git commit -m "feat(web): add AppShell layout with sidebar + topbar + outlet"
```

---

## Task 18: Role dashboard placeholders and router

**Files:**
- Create: `bom-web/src/features/dashboard/SalesDashboard.tsx`
- Create: `bom-web/src/features/dashboard/BomDashboard.tsx`
- Create: `bom-web/src/features/dashboard/AccountantDashboard.tsx`
- Create: `bom-web/src/features/dashboard/MdDashboard.tsx`
- Create: `bom-web/src/features/dashboard/AdminDashboard.tsx`
- Create: `bom-web/src/features/dashboard/DashboardRouter.tsx`

- [ ] **Step 1: Write five placeholder dashboard files**

Each dashboard follows the same structure. Create them with these exact contents:

`src/features/dashboard/SalesDashboard.tsx`:

```tsx
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/Card";

export default function SalesDashboard() {
  return (
    <Card>
      <CardHeader>
        <CardTitle>Sales Dashboard</CardTitle>
      </CardHeader>
      <CardContent>
        <p className="text-muted-foreground">
          Your requisitions and quick actions will appear here.
        </p>
      </CardContent>
    </Card>
  );
}
```

`src/features/dashboard/BomDashboard.tsx`:

```tsx
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/Card";

export default function BomDashboard() {
  return (
    <Card>
      <CardHeader>
        <CardTitle>BOM Creator Dashboard</CardTitle>
      </CardHeader>
      <CardContent>
        <p className="text-muted-foreground">
          Requisitions awaiting BOM will appear here.
        </p>
      </CardContent>
    </Card>
  );
}
```

`src/features/dashboard/AccountantDashboard.tsx`:

```tsx
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/Card";

export default function AccountantDashboard() {
  return (
    <Card>
      <CardHeader>
        <CardTitle>Accountant Dashboard</CardTitle>
      </CardHeader>
      <CardContent>
        <p className="text-muted-foreground">
          Requisitions awaiting costing will appear here.
        </p>
      </CardContent>
    </Card>
  );
}
```

`src/features/dashboard/MdDashboard.tsx`:

```tsx
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/Card";

export default function MdDashboard() {
  return (
    <Card>
      <CardHeader>
        <CardTitle>Managing Director Dashboard</CardTitle>
      </CardHeader>
      <CardContent>
        <p className="text-muted-foreground">
          Requisitions awaiting approval will appear here.
        </p>
      </CardContent>
    </Card>
  );
}
```

`src/features/dashboard/AdminDashboard.tsx`:

```tsx
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/Card";

export default function AdminDashboard() {
  return (
    <Card>
      <CardHeader>
        <CardTitle>Admin Dashboard</CardTitle>
      </CardHeader>
      <CardContent>
        <p className="text-muted-foreground">
          User, branch, and master data management will appear here.
        </p>
      </CardContent>
    </Card>
  );
}
```

- [ ] **Step 2: Write `src/features/dashboard/DashboardRouter.tsx`**

```tsx
import { useAuthStore } from "@/store/authStore";
import SalesDashboard from "./SalesDashboard";
import BomDashboard from "./BomDashboard";
import AccountantDashboard from "./AccountantDashboard";
import MdDashboard from "./MdDashboard";
import AdminDashboard from "./AdminDashboard";

export default function DashboardRouter() {
  const role = useAuthStore((s) => s.user?.role);

  switch (role) {
    case "SalesPerson":
      return <SalesDashboard />;
    case "BomCreator":
      return <BomDashboard />;
    case "Accountant":
      return <AccountantDashboard />;
    case "ManagingDirector":
      return <MdDashboard />;
    case "Admin":
      return <AdminDashboard />;
    default:
      return null;
  }
}
```

- [ ] **Step 3: Commit**

```bash
cd ..
git add bom-web/src/features/dashboard/
git commit -m "feat(web): add role-specific dashboard placeholders and router"
```

---

## Task 19: Wire App.tsx router

**Files:**
- Modify: `bom-web/src/App.tsx`

- [ ] **Step 1: Replace `src/App.tsx`**

```tsx
import { createBrowserRouter, Navigate, RouterProvider } from "react-router-dom";
import LoginPage from "@/features/auth/LoginPage";
import { AppShell } from "@/components/layout/AppShell";
import { ProtectedRoute } from "@/components/layout/ProtectedRoute";
import DashboardRouter from "@/features/dashboard/DashboardRouter";

const router = createBrowserRouter([
  { path: "/login", element: <LoginPage /> },
  {
    path: "/",
    element: (
      <ProtectedRoute>
        <AppShell />
      </ProtectedRoute>
    ),
    children: [
      { index: true, element: <Navigate to="/dashboard" replace /> },
      { path: "dashboard", element: <DashboardRouter /> },
    ],
  },
  { path: "*", element: <Navigate to="/dashboard" replace /> },
]);

export default function App() {
  return <RouterProvider router={router} />;
}
```

- [ ] **Step 2: Commit**

```bash
cd ..
git add bom-web/src/App.tsx
git commit -m "feat(web): wire router with login, protected shell, dashboard"
```

---

## Task 20: Wire main.tsx with providers and initial theme

**Files:**
- Modify: `bom-web/src/main.tsx`

- [ ] **Step 1: Replace `src/main.tsx`**

```tsx
import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { QueryClientProvider } from "@tanstack/react-query";
import App from "./App";
import "./index.css";
import { queryClient } from "@/api/queryClient";
import { useThemeStore } from "@/store/themeStore";

useThemeStore.getState().apply();

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <App />
    </QueryClientProvider>
  </StrictMode>,
);
```

- [ ] **Step 2: Run the full test suite one more time**

```bash
cd bom-web && npm test
```

Expected: PASS — authStore (4) + axios (2) + ProtectedRoute (4) = 10 tests.

- [ ] **Step 3: Run a production build**

```bash
npm run build
```

Expected: succeeds with no TypeScript errors. Report any warnings and fix before committing.

- [ ] **Step 4: Commit**

```bash
cd ..
git add bom-web/src/main.tsx
git commit -m "feat(web): wire QueryClientProvider and theme init in main"
```

---

## Task 21: Manual smoke verification

**Files:** none (manual test)

- [ ] **Step 1: Start the API**

In one terminal:

```bash
dotnet run --project BomPriceApproval.API
```

Wait for `Now listening on: http://localhost:7300`.

- [ ] **Step 2: Start the web app**

In a second terminal:

```bash
cd bom-web && npm run dev
```

Expected: Vite banner shows `Local: http://localhost:5300/`.

- [ ] **Step 3: Verify the login flow**

Open `http://localhost:5300` in a browser. Expected: redirected to `/login`.

Check the seeded admin credentials in `BomPriceApproval.API/Program.cs` (search for `admin@test.com`) and log in with them.

Expected: redirect to `/dashboard`, Admin dashboard card visible inside the shell, sidebar shows Dashboard + Notifications + Users + Branches + Exchange Rates + Items, topbar shows "Signed in as Admin · Admin".

- [ ] **Step 4: Verify theme toggle**

Click the sun/moon icon in the topbar. Expected: colours flip instantly (dark ↔ light). Reload the page. Expected: theme persists.

- [ ] **Step 5: Verify sidebar collapse**

Click the chevron at the bottom of the sidebar. Expected: sidebar animates to 60px, labels disappear, icons remain. Reload. Expected: collapsed state persists.

- [ ] **Step 6: Verify session persistence**

With the admin still logged in, close and reopen the browser tab at `http://localhost:5300`. Expected: still on the dashboard, no redirect to `/login`.

- [ ] **Step 7: Verify logout**

Click Log out in the topbar. Expected: redirect to `/login`. Attempt to visit `http://localhost:5300/dashboard` directly. Expected: redirected back to `/login`.

- [ ] **Step 8: Verify role filtering**

Log in as a non-Admin user. If no non-Admin user is seeded, use the API directly with Swagger to create one, or skip this step and record it as a follow-up.

Expected: Admin-only nav items (Users, Branches, Items) are hidden from the sidebar for non-Admin roles.

- [ ] **Step 9: Verify refresh interceptor (optional)**

Open DevTools → Application → Local Storage. Edit `bom-auth` and replace `accessToken` with an obviously bad value (e.g. `"broken"`) while keeping `refreshToken` valid. Reload and navigate such that an API call fires (future plans; in Plan 1 this is not observable without an authed GET endpoint). Record as verified in Plan 2 instead.

- [ ] **Step 10: Final commit (plan tracking only, no code changes)**

No commit needed. Update the plan checklist in your tracking system to mark Plan 1 complete.

---

## Self-Review Results

**Spec coverage:**
- Section 1 (Tech Stack): React 19, Vite, TS, Tailwind v4, React Router v6, TanStack Query v5, Zustand, Axios, RHF + Zod, Framer Motion — all installed in Task 2 and used in Tasks 3–20. Shadcn/UI is deferred to Plan 2 (noted in plan header). SignalR and Recharts are deferred to Plans 3 & 4 (they don't belong in Plan 1).
- Section 2 (Project structure): File tree in this plan matches the spec for every directory touched by Plan 1. Feature folders not yet built (requisitions, bom, costing, etc.) are left to later plans.
- Section 3 (Routing): `/login`, `/dashboard`, protected shell implemented in Task 19. Other routes deferred to their respective plans.
- Section 4 (State management): authStore (Task 6), themeStore (Task 7), axios refresh interceptor (Task 9), TanStack Query client (Task 10). SignalR deferred to Plan 4.
- Section 5 (Key screens): LoginPage + AppShell + Sidebar + Topbar + role dashboard placeholders all present. Feature screens deferred.
- Section 6 (UX): dark/light theme + sidebar collapse + collapsed-sidebar tooltips covered. Framer Motion used for sidebar. Page transitions deferred to Plan 2 when multiple routes exist.
- Section 7 (API integration): axios base URL, refresh interceptor, TanStack Query defaults all in place.

**Placeholder scan:** none. Every code block is concrete. The only deferrals are explicitly scoped out with a reason.

**Type consistency:** `AuthUser`, `LoginResponse`, `UserRole` used identically across authStore, axios refresh, authApi, ProtectedRoute, Sidebar, DashboardRouter, LoginPage.

**Ambiguity check:** one gotcha was flagged inline in Task 9 (refresh must go through `api` not raw `axios` so test adapters apply). The plan addresses it with a rewrite and an explanation.

---

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-04-13-plan-01-foundation-auth.md`. Two execution options:

1. **Subagent-Driven (recommended)** — I dispatch a fresh subagent per task, review between tasks, fast iteration.
2. **Inline Execution** — Execute tasks in this session using executing-plans, batch execution with checkpoints.

Which approach?
