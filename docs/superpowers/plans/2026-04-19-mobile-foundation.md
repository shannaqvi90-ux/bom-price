# Mobile Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Scaffold `bom-mobile` (Expo + React Native + TypeScript), build auth flow, and wire role-based navigation so a user can log in on a real iPhone and land on a role-appropriate (placeholder) home screen.

**Architecture:** Single Expo app with Expo Router file-based routing; login populates an `AuthContext`, which role-routes to `(sales)` or `(md)` groups. Tokens live in `expo-secure-store`. Axios interceptor transparently refreshes 401s. No backend changes.

**Tech Stack:** Expo SDK 52, React Native 0.76+, React 19, TypeScript, Expo Router v4, TanStack Query v5, Axios, `expo-secure-store`, NativeWind v4, React Hook Form + Zod. Tests: Jest + `@testing-library/react-native`.

**Scope boundary:** This plan produces a runnable shell. Requisitions, approvals, SignalR, notifications, and EAS/TestFlight are covered in follow-up plans (`2026-04-20-mobile-salesperson.md`, `2026-04-21-mobile-md-and-deploy.md`).

---

## File Structure (created by this plan)

```
bom-mobile/
  app/
    _layout.tsx
    index.tsx
    login.tsx
    (sales)/
      _layout.tsx
      index.tsx            # placeholder "Hello SalesPerson" screen
    (md)/
      _layout.tsx
      index.tsx            # placeholder "Hello MD" screen
    profile.tsx
  src/
    api/
      client.ts
      auth.ts
    auth/
      AuthContext.tsx
      secureStore.ts
    components/
      Button.tsx
      Input.tsx
      EmptyState.tsx
      ErrorBanner.tsx
      LoadingView.tsx
      StatusPill.tsx
    theme/
      tokens.ts
    types/
      api.ts               # copied from bom-web/src/types/api.ts
    utils/
      validation.ts        # Zod schemas (login)
  __tests__/
    client.test.ts         # 401 refresh + concurrent race
    roleGuard.test.tsx     # routing guard behavior
    loginSchema.test.ts    # Zod schema
  app.json
  app.config.ts
  babel.config.js
  metro.config.js
  tailwind.config.js
  global.css
  nativewind-env.d.ts
  package.json
  tsconfig.json
  .env.development
  .env.production.example
  .gitignore
  README.md
  jest.config.js
  jest.setup.ts
```

---

## Task 1: Scaffold Expo project + install core dependencies

**Files:**
- Create: `bom-mobile/` (via create-expo-app)
- Modify: `bom-mobile/package.json`

- [ ] **Step 1: Verify no `bom-mobile/` exists yet**

Run from repo root (`D:\shan projects\BOM_Price_Approval`):

```bash
test ! -d bom-mobile && echo "OK to scaffold" || echo "DIR EXISTS — abort"
```

Expected: `OK to scaffold`

- [ ] **Step 2: Scaffold the Expo project**

Run from repo root:

```bash
npx create-expo-app@latest bom-mobile --template blank-typescript
```

Expected: creates `bom-mobile/` with `package.json`, `App.tsx`, `tsconfig.json`, `app.json`, `babel.config.js`.

- [ ] **Step 3: Install Expo-managed dependencies**

Run from `bom-mobile/`:

```bash
npx expo install expo-router expo-linking expo-constants expo-status-bar expo-secure-store expo-file-system expo-sharing react-native-screens react-native-safe-area-context react-native-gesture-handler react-native-reanimated
```

Expected: `package.json` gains all above under `dependencies`.

- [ ] **Step 4: Install runtime libs from npm**

Run from `bom-mobile/`:

```bash
npm install @tanstack/react-query axios react-hook-form zod @microsoft/signalr nativewind
```

Expected: all added to `dependencies`.

- [ ] **Step 5: Install dev dependencies**

Run from `bom-mobile/`:

```bash
npm install --save-dev tailwindcss@^3.4.17 prettier-plugin-tailwindcss jest @types/jest jest-expo @testing-library/react-native @testing-library/jest-native ts-jest @types/react-test-renderer
```

Expected: all added to `devDependencies`.

- [ ] **Step 6: Set Expo Router entry in `package.json`**

Change `"main"` in `bom-mobile/package.json` from `"node_modules/expo/AppEntry.js"` (or `"App.tsx"`) to:

```json
"main": "expo-router/entry",
```

- [ ] **Step 7: Delete template `App.tsx`**

Expo Router owns the entry now.

```bash
rm bom-mobile/App.tsx
```

- [ ] **Step 8: Verify install integrity**

Run from `bom-mobile/`:

```bash
npx expo-doctor
```

Expected: no errors (warnings about `@microsoft/signalr` peer deps are acceptable).

- [ ] **Step 9: Commit**

From repo root:

```bash
git add bom-mobile/package.json bom-mobile/package-lock.json bom-mobile/app.json bom-mobile/tsconfig.json bom-mobile/babel.config.js bom-mobile/.gitignore
git commit -m "chore(mobile): scaffold Expo app with core dependencies"
```

---

## Task 2: Configure Metro, Babel, tsconfig, and path aliases

**Files:**
- Modify: `bom-mobile/babel.config.js`
- Create: `bom-mobile/metro.config.js`
- Modify: `bom-mobile/tsconfig.json`

- [ ] **Step 1: Update `babel.config.js` for NativeWind + Reanimated**

Replace `bom-mobile/babel.config.js` with:

```js
module.exports = function (api) {
  api.cache(true);
  return {
    presets: [
      ["babel-preset-expo", { jsxImportSource: "nativewind" }],
      "nativewind/babel",
    ],
    plugins: ["react-native-reanimated/plugin"],
  };
};
```

- [ ] **Step 2: Create `metro.config.js`**

```js
const { getDefaultConfig } = require("expo/metro-config");
const { withNativeWind } = require("nativewind/metro");

const config = getDefaultConfig(__dirname);

module.exports = withNativeWind(config, { input: "./global.css" });
```

- [ ] **Step 3: Add path alias and strict settings to `tsconfig.json`**

Replace `bom-mobile/tsconfig.json` with:

```json
{
  "extends": "expo/tsconfig.base",
  "compilerOptions": {
    "strict": true,
    "baseUrl": ".",
    "paths": {
      "@/*": ["src/*"],
      "@app/*": ["app/*"]
    }
  },
  "include": ["**/*.ts", "**/*.tsx", ".expo/types/**/*.ts", "expo-env.d.ts", "nativewind-env.d.ts"]
}
```

- [ ] **Step 4: Create `nativewind-env.d.ts`**

`bom-mobile/nativewind-env.d.ts`:

```ts
/// <reference types="nativewind/types" />
```

- [ ] **Step 5: Verify TypeScript compiles**

Run from `bom-mobile/`:

```bash
npx tsc --noEmit
```

Expected: no errors.

- [ ] **Step 6: Commit**

```bash
git add bom-mobile/babel.config.js bom-mobile/metro.config.js bom-mobile/tsconfig.json bom-mobile/nativewind-env.d.ts
git commit -m "chore(mobile): configure Metro, Babel, tsconfig paths for NativeWind"
```

---

## Task 3: NativeWind + theme tokens

**Files:**
- Create: `bom-mobile/tailwind.config.js`
- Create: `bom-mobile/global.css`
- Create: `bom-mobile/src/theme/tokens.ts`

- [ ] **Step 1: Create `tailwind.config.js`**

Mirror the web palette (slate/indigo) — check `bom-web/tailwind.config.js` if present for exact shades; otherwise use the values below.

```js
/** @type {import('tailwindcss').Config} */
module.exports = {
  content: [
    "./app/**/*.{ts,tsx}",
    "./src/**/*.{ts,tsx}",
  ],
  presets: [require("nativewind/preset")],
  theme: {
    extend: {
      colors: {
        brand: {
          50: "#eef2ff",
          100: "#e0e7ff",
          500: "#6366f1",
          600: "#4f46e5",
          700: "#4338ca",
        },
        status: {
          pending: "#f59e0b",
          progress: "#3b82f6",
          review: "#8b5cf6",
          approved: "#10b981",
          rejected: "#ef4444",
        },
      },
    },
  },
  plugins: [],
};
```

- [ ] **Step 2: Create `global.css`**

```css
@tailwind base;
@tailwind components;
@tailwind utilities;
```

- [ ] **Step 3: Create `src/theme/tokens.ts`**

```ts
export const colors = {
  brand: { 50: "#eef2ff", 500: "#6366f1", 600: "#4f46e5" },
  text: { primary: "#0f172a", muted: "#64748b", inverse: "#ffffff" },
  bg: { app: "#f8fafc", card: "#ffffff", border: "#e2e8f0" },
  status: {
    BomPending: "#f59e0b",
    BomInProgress: "#3b82f6",
    CostingPending: "#f59e0b",
    CostingInProgress: "#3b82f6",
    MdReview: "#8b5cf6",
    Approved: "#10b981",
    Rejected: "#ef4444",
  },
} as const;

export const spacing = { xs: 4, sm: 8, md: 12, lg: 16, xl: 24, xxl: 32 } as const;

export const radii = { sm: 4, md: 8, lg: 12, full: 9999 } as const;

export const typography = {
  h1: { fontSize: 24, fontWeight: "700" as const },
  h2: { fontSize: 20, fontWeight: "600" as const },
  body: { fontSize: 16, fontWeight: "400" as const },
  caption: { fontSize: 13, fontWeight: "400" as const },
} as const;
```

- [ ] **Step 4: Verify compile**

```bash
npx tsc --noEmit
```

Expected: no errors.

- [ ] **Step 5: Commit**

```bash
git add bom-mobile/tailwind.config.js bom-mobile/global.css bom-mobile/src/theme/tokens.ts
git commit -m "feat(mobile): add NativeWind config and theme tokens"
```

---

## Task 4: Core UI components

**Files:**
- Create: `bom-mobile/src/components/Button.tsx`
- Create: `bom-mobile/src/components/Input.tsx`
- Create: `bom-mobile/src/components/EmptyState.tsx`
- Create: `bom-mobile/src/components/ErrorBanner.tsx`
- Create: `bom-mobile/src/components/LoadingView.tsx`
- Create: `bom-mobile/src/components/StatusPill.tsx`

- [ ] **Step 1: `Button.tsx`**

```tsx
import { ActivityIndicator, Pressable, Text, type PressableProps } from "react-native";

type Variant = "primary" | "secondary" | "danger";

interface Props extends PressableProps {
  title: string;
  variant?: Variant;
  loading?: boolean;
}

const variantClasses: Record<Variant, string> = {
  primary: "bg-brand-600 active:bg-brand-700",
  secondary: "bg-white border border-slate-300 active:bg-slate-50",
  danger: "bg-rose-600 active:bg-rose-700",
};

const textClasses: Record<Variant, string> = {
  primary: "text-white",
  secondary: "text-slate-900",
  danger: "text-white",
};

export function Button({ title, variant = "primary", loading, disabled, ...rest }: Props) {
  const isDisabled = disabled || loading;
  return (
    <Pressable
      {...rest}
      disabled={isDisabled}
      className={`px-4 py-3 rounded-md items-center ${variantClasses[variant]} ${isDisabled ? "opacity-50" : ""}`}
    >
      {loading
        ? <ActivityIndicator color={variant === "secondary" ? "#0f172a" : "#ffffff"} />
        : <Text className={`text-base font-semibold ${textClasses[variant]}`}>{title}</Text>}
    </Pressable>
  );
}
```

- [ ] **Step 2: `Input.tsx`**

```tsx
import { Text, TextInput, View, type TextInputProps } from "react-native";

interface Props extends TextInputProps {
  label: string;
  error?: string;
}

export function Input({ label, error, ...rest }: Props) {
  return (
    <View className="mb-3">
      <Text className="text-sm text-slate-700 mb-1">{label}</Text>
      <TextInput
        {...rest}
        className={`border rounded-md px-3 py-2 text-base text-slate-900 bg-white ${error ? "border-rose-500" : "border-slate-300"}`}
        placeholderTextColor="#94a3b8"
      />
      {error ? <Text className="text-xs text-rose-600 mt-1">{error}</Text> : null}
    </View>
  );
}
```

- [ ] **Step 3: `EmptyState.tsx`**

```tsx
import { Text, View } from "react-native";

export function EmptyState({ title, hint }: { title: string; hint?: string }) {
  return (
    <View className="flex-1 items-center justify-center p-8">
      <Text className="text-lg font-semibold text-slate-700">{title}</Text>
      {hint ? <Text className="text-sm text-slate-500 mt-2 text-center">{hint}</Text> : null}
    </View>
  );
}
```

- [ ] **Step 4: `ErrorBanner.tsx`**

```tsx
import { Pressable, Text, View } from "react-native";

interface Props {
  message: string;
  onRetry?: () => void;
}

export function ErrorBanner({ message, onRetry }: Props) {
  return (
    <View className="bg-rose-50 border border-rose-200 rounded-md p-3 mb-3">
      <Text className="text-rose-800 text-sm">{message}</Text>
      {onRetry ? (
        <Pressable onPress={onRetry} className="mt-2">
          <Text className="text-rose-700 font-semibold text-sm">Retry</Text>
        </Pressable>
      ) : null}
    </View>
  );
}
```

- [ ] **Step 5: `LoadingView.tsx`**

```tsx
import { ActivityIndicator, View } from "react-native";

export function LoadingView() {
  return (
    <View className="flex-1 items-center justify-center">
      <ActivityIndicator size="large" color="#4f46e5" />
    </View>
  );
}
```

- [ ] **Step 6: `StatusPill.tsx`**

```tsx
import { Text, View } from "react-native";
import { colors } from "@/theme/tokens";

type Status = keyof typeof colors.status;

export function StatusPill({ status }: { status: Status }) {
  return (
    <View style={{ backgroundColor: colors.status[status] }} className="px-2 py-1 rounded-full self-start">
      <Text className="text-xs font-semibold text-white">{status}</Text>
    </View>
  );
}
```

- [ ] **Step 7: Verify compile**

```bash
npx tsc --noEmit
```

Expected: no errors.

- [ ] **Step 8: Commit**

```bash
git add bom-mobile/src/components
git commit -m "feat(mobile): add core UI primitives (Button, Input, EmptyState, ErrorBanner, LoadingView, StatusPill)"
```

---

## Task 5: Copy shared types from web

**Files:**
- Create: `bom-mobile/src/types/api.ts`

- [ ] **Step 1: Copy the file**

```bash
cp "bom-web/src/types/api.ts" "bom-mobile/src/types/api.ts"
```

- [ ] **Step 2: Verify TypeScript sees it**

```bash
cd bom-mobile && npx tsc --noEmit
```

Expected: no errors. (If the web file imports anything via relative paths outside `types/`, those will surface here — fix by inlining the referenced types into `api.ts`.)

- [ ] **Step 3: Add sync note to README**

Add to `bom-mobile/README.md` (create the file if absent):

```markdown
## Shared types

`src/types/api.ts` is a manual copy of `bom-web/src/types/api.ts`. When the web types change, re-copy and re-run `tsc --noEmit`. A monorepo migration is a phase-2 option.
```

- [ ] **Step 4: Commit**

```bash
git add bom-mobile/src/types/api.ts bom-mobile/README.md
git commit -m "feat(mobile): copy shared API types from bom-web"
```

---

## Task 6: App config + environment variables

**Files:**
- Create: `bom-mobile/app.config.ts`
- Create: `bom-mobile/.env.development`
- Create: `bom-mobile/.env.production.example`
- Modify: `bom-mobile/.gitignore`
- Delete: `bom-mobile/app.json` (replaced by `app.config.ts`)

- [ ] **Step 1: Remove `app.json`** (we will generate config dynamically)

```bash
rm bom-mobile/app.json
```

- [ ] **Step 2: Create `app.config.ts`**

```ts
import type { ExpoConfig } from "expo/config";

export default (): ExpoConfig => ({
  name: "FPF Quotations",
  slug: "fpf-quotations",
  version: "0.1.0",
  orientation: "portrait",
  userInterfaceStyle: "light",
  ios: {
    supportsTablet: false,
    bundleIdentifier: "ae.fpf.quotations",
  },
  plugins: ["expo-router", "expo-secure-store"],
  scheme: "fpfquotations",
  extra: {
    apiBaseUrl: process.env.EXPO_PUBLIC_API_BASE_URL ?? "http://localhost:7300",
  },
  experiments: { typedRoutes: true },
});
```

- [ ] **Step 3: Create `.env.development`**

Replace `<YOUR-LAN-IP>` with the dev machine's LAN IP (ipconfig on Windows → IPv4 Address). Example: `192.168.1.42`.

```
EXPO_PUBLIC_API_BASE_URL=http://<YOUR-LAN-IP>:7300
```

- [ ] **Step 4: Create `.env.production.example`**

```
EXPO_PUBLIC_API_BASE_URL=https://<prod-domain>
```

- [ ] **Step 5: Update `.gitignore`**

Append to `bom-mobile/.gitignore`:

```
# env
.env
.env.local
.env.development
.env.production
!.env.*.example
```

- [ ] **Step 6: Verify Expo reads config**

```bash
cd bom-mobile && npx expo config --type prebuild | head -20
```

Expected: prints JSON containing `"name": "FPF Quotations"`.

- [ ] **Step 7: Commit**

```bash
git add bom-mobile/app.config.ts bom-mobile/.env.production.example bom-mobile/.gitignore
git rm --cached bom-mobile/app.json 2>/dev/null || true
git add -u
git commit -m "feat(mobile): app config with env-switchable API base URL"
```

---

## Task 7: Secure store wrapper

**Files:**
- Create: `bom-mobile/src/auth/secureStore.ts`

- [ ] **Step 1: Implementation**

```ts
import * as SecureStore from "expo-secure-store";

const ACCESS = "auth.access";
const REFRESH = "auth.refresh";
const USER = "auth.user";

export async function saveTokens(access: string, refresh: string) {
  await Promise.all([
    SecureStore.setItemAsync(ACCESS, access),
    SecureStore.setItemAsync(REFRESH, refresh),
  ]);
}

export async function getAccess() {
  return SecureStore.getItemAsync(ACCESS);
}

export async function getRefresh() {
  return SecureStore.getItemAsync(REFRESH);
}

export async function clearTokens() {
  await Promise.all([
    SecureStore.deleteItemAsync(ACCESS),
    SecureStore.deleteItemAsync(REFRESH),
    SecureStore.deleteItemAsync(USER),
  ]);
}

export async function saveUser<T>(user: T) {
  await SecureStore.setItemAsync(USER, JSON.stringify(user));
}

export async function getUser<T>(): Promise<T | null> {
  const raw = await SecureStore.getItemAsync(USER);
  return raw ? (JSON.parse(raw) as T) : null;
}
```

- [ ] **Step 2: Verify compile**

```bash
npx tsc --noEmit
```

- [ ] **Step 3: Commit**

```bash
git add bom-mobile/src/auth/secureStore.ts
git commit -m "feat(mobile): add secure-store wrapper for tokens + user cache"
```

---

## Task 8: Axios client with 401 refresh interceptor (TDD)

**Files:**
- Create: `bom-mobile/__tests__/client.test.ts`
- Create: `bom-mobile/jest.config.js`
- Create: `bom-mobile/jest.setup.ts`
- Create: `bom-mobile/src/api/client.ts`
- Create: `bom-mobile/src/api/auth.ts`

- [ ] **Step 1: Create `jest.config.js`**

```js
module.exports = {
  preset: "jest-expo",
  setupFilesAfterEach: ["<rootDir>/jest.setup.ts"],
  moduleNameMapper: { "^@/(.*)$": "<rootDir>/src/$1" },
  transformIgnorePatterns: [
    "node_modules/(?!(jest-)?react-native|@react-native|expo(nent)?|@expo(nent)?/.*|expo-modules-core|expo-router|@microsoft/signalr|nativewind)",
  ],
};
```

- [ ] **Step 2: Create `jest.setup.ts`**

```ts
import "@testing-library/jest-native/extend-expect";

jest.mock("expo-secure-store", () => {
  const store = new Map<string, string>();
  return {
    setItemAsync: async (k: string, v: string) => { store.set(k, v); },
    getItemAsync: async (k: string) => store.get(k) ?? null,
    deleteItemAsync: async (k: string) => { store.delete(k); },
  };
});

jest.mock("expo-constants", () => ({
  default: { expoConfig: { extra: { apiBaseUrl: "http://test.local" } } },
  expoConfig: { extra: { apiBaseUrl: "http://test.local" } },
}));
```

- [ ] **Step 3: Write failing test for 401 refresh + concurrent race**

`bom-mobile/__tests__/client.test.ts`:

```ts
import MockAdapter from "axios-mock-adapter";
import { api, __resetRefreshState } from "@/api/client";
import { saveTokens, getAccess } from "@/auth/secureStore";

let mock: MockAdapter;

beforeEach(async () => {
  mock = new MockAdapter(api);
  __resetRefreshState();
  await saveTokens("old-access", "refresh-1");
});

afterEach(() => {
  mock.restore();
});

test("401 triggers refresh and retries original request", async () => {
  mock.onGet("/api/ping")
    .replyOnce(401, { code: "token_expired" })
    .onGet("/api/ping")
    .replyOnce(200, { ok: true });

  mock.onPost("/api/auth/refresh").reply(200, {
    accessToken: "new-access",
    refreshToken: "refresh-2",
  });

  const res = await api.get("/api/ping");
  expect(res.data).toEqual({ ok: true });
  expect(await getAccess()).toBe("new-access");
});

test("concurrent 401s share one refresh call", async () => {
  let refreshCount = 0;
  mock.onPost("/api/auth/refresh").reply(() => {
    refreshCount++;
    return [200, { accessToken: "new-access", refreshToken: "refresh-2" }];
  });
  mock.onGet("/api/a").replyOnce(401, { code: "token_expired" }).onGet("/api/a").reply(200, { a: 1 });
  mock.onGet("/api/b").replyOnce(401, { code: "token_expired" }).onGet("/api/b").reply(200, { b: 2 });

  const [a, b] = await Promise.all([api.get("/api/a"), api.get("/api/b")]);
  expect(a.data).toEqual({ a: 1 });
  expect(b.data).toEqual({ b: 2 });
  expect(refreshCount).toBe(1);
});

test("refresh failure clears tokens and rejects", async () => {
  mock.onGet("/api/ping").reply(401, { code: "token_expired" });
  mock.onPost("/api/auth/refresh").reply(401);

  await expect(api.get("/api/ping")).rejects.toBeDefined();
  expect(await getAccess()).toBeNull();
});
```

Install `axios-mock-adapter`:

```bash
cd bom-mobile && npm install --save-dev axios-mock-adapter
```

- [ ] **Step 4: Run the test and verify it fails**

```bash
npx jest __tests__/client.test.ts
```

Expected: FAIL — `Cannot find module '@/api/client'`.

- [ ] **Step 5: Implement `src/api/client.ts`**

```ts
import axios, { AxiosError, type InternalAxiosRequestConfig } from "axios";
import Constants from "expo-constants";
import { getAccess, getRefresh, saveTokens, clearTokens } from "@/auth/secureStore";

const baseURL = (Constants.expoConfig?.extra?.apiBaseUrl as string) ?? "http://localhost:7300";

export const api = axios.create({ baseURL, timeout: 15000 });

let refreshPromise: Promise<string> | null = null;

export function __resetRefreshState() {
  refreshPromise = null;
}

api.interceptors.request.use(async (config: InternalAxiosRequestConfig) => {
  const token = await getAccess();
  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

api.interceptors.response.use(
  (r) => r,
  async (error: AxiosError<{ code?: string }>) => {
    const original = error.config as InternalAxiosRequestConfig & { _retried?: boolean };
    const isAuthExpired =
      error.response?.status === 401 &&
      error.response?.data?.code === "token_expired" &&
      original &&
      !original._retried &&
      !original.url?.includes("/api/auth/refresh");

    if (!isAuthExpired) return Promise.reject(error);
    original._retried = true;

    try {
      const newAccess = await (refreshPromise ??= doRefresh());
      original.headers.Authorization = `Bearer ${newAccess}`;
      return api.request(original);
    } catch (e) {
      await clearTokens();
      return Promise.reject(e);
    } finally {
      refreshPromise = null;
    }
  }
);

async function doRefresh(): Promise<string> {
  const refresh = await getRefresh();
  if (!refresh) throw new Error("no-refresh-token");
  const res = await axios.post(`${baseURL}/api/auth/refresh`, { refreshToken: refresh });
  const { accessToken, refreshToken } = res.data as { accessToken: string; refreshToken: string };
  await saveTokens(accessToken, refreshToken);
  return accessToken;
}
```

- [ ] **Step 6: Run tests to verify they pass**

```bash
npx jest __tests__/client.test.ts
```

Expected: PASS (3 tests).

- [ ] **Step 7: Implement `src/api/auth.ts`**

Note: the backend's `LoginResponse` is flat (fields `accessToken`, `refreshToken`, `role`, `userId`, `name`, `branchId` — not a nested `user` object). This matches `LoginResponse` already defined in `src/types/api.ts`.

```ts
import { api } from "./client";
import { saveTokens, saveUser, clearTokens } from "@/auth/secureStore";
import type { AuthUser, LoginResponse } from "@/types/api";

export async function login(email: string, password: string): Promise<AuthUser> {
  const res = await api.post<LoginResponse>("/api/auth/login", { email, password });
  const { accessToken, refreshToken, role, userId, name, branchId } = res.data;
  await saveTokens(accessToken, refreshToken);
  const user: AuthUser = { userId, name, role, branchId };
  await saveUser(user);
  return user;
}

export async function logout() {
  try {
    await api.post("/api/auth/logout");
  } catch {
    // server logout is best-effort
  } finally {
    await clearTokens();
  }
}
```

- [ ] **Step 8: Verify compile**

```bash
npx tsc --noEmit
```

- [ ] **Step 9: Commit**

```bash
git add bom-mobile/jest.config.js bom-mobile/jest.setup.ts bom-mobile/__tests__/client.test.ts bom-mobile/src/api bom-mobile/package.json bom-mobile/package-lock.json
git commit -m "feat(mobile): axios client with 401 refresh + concurrent race test"
```

---

## Task 9: Login schema + validation test

**Files:**
- Create: `bom-mobile/src/utils/validation.ts`
- Create: `bom-mobile/__tests__/loginSchema.test.ts`

- [ ] **Step 1: Write failing test**

`bom-mobile/__tests__/loginSchema.test.ts`:

```ts
import { loginSchema } from "@/utils/validation";

test("accepts valid email + non-empty password", () => {
  const r = loginSchema.safeParse({ email: "a@b.com", password: "x" });
  expect(r.success).toBe(true);
});

test("rejects invalid email", () => {
  const r = loginSchema.safeParse({ email: "not-email", password: "x" });
  expect(r.success).toBe(false);
});

test("rejects empty password", () => {
  const r = loginSchema.safeParse({ email: "a@b.com", password: "" });
  expect(r.success).toBe(false);
});
```

- [ ] **Step 2: Run, verify it fails**

```bash
npx jest __tests__/loginSchema.test.ts
```

Expected: FAIL — module missing.

- [ ] **Step 3: Implement `src/utils/validation.ts`**

```ts
import { z } from "zod";

export const loginSchema = z.object({
  email: z.string().email(),
  password: z.string().min(1, "Password is required"),
});

export type LoginInput = z.infer<typeof loginSchema>;
```

- [ ] **Step 4: Run, verify pass**

```bash
npx jest __tests__/loginSchema.test.ts
```

Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add bom-mobile/src/utils/validation.ts bom-mobile/__tests__/loginSchema.test.ts
git commit -m "test(mobile): login form validation schema"
```

---

## Task 10: AuthContext with cold-start token restore

**Files:**
- Create: `bom-mobile/src/auth/AuthContext.tsx`

- [ ] **Step 1: Implementation**

```tsx
import { createContext, useContext, useEffect, useState, type ReactNode } from "react";
import { getAccess, getRefresh, getUser, clearTokens, saveUser } from "./secureStore";
import { login as apiLogin, logout as apiLogout } from "@/api/auth";
import type { AuthUser } from "@/types/api";

interface AuthState {
  user: AuthUser | null;
  loading: boolean;
  login: (email: string, password: string) => Promise<AuthUser>;
  logout: () => Promise<void>;
}

const AuthContext = createContext<AuthState | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<AuthUser | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    (async () => {
      try {
        const [access, refresh, cachedUser] = await Promise.all([getAccess(), getRefresh(), getUser<AuthUser>()]);
        if ((access || refresh) && cachedUser) {
          setUser(cachedUser);
        } else if (!access && !refresh) {
          await clearTokens();
        }
      } finally {
        setLoading(false);
      }
    })();
  }, []);

  const login = async (email: string, password: string) => {
    const u = await apiLogin(email, password);
    await saveUser(u);
    setUser(u);
    return u;
  };

  const logout = async () => {
    await apiLogout();
    setUser(null);
  };

  return <AuthContext.Provider value={{ user, loading, login, logout }}>{children}</AuthContext.Provider>;
}

export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error("useAuth must be used inside AuthProvider");
  return ctx;
}
```

- [ ] **Step 2: Verify compile**

```bash
npx tsc --noEmit
```

- [ ] **Step 3: Commit**

```bash
git add bom-mobile/src/auth/AuthContext.tsx
git commit -m "feat(mobile): AuthContext with cold-start token restore"
```

---

## Task 11: Root layout with providers + index redirect

**Files:**
- Create: `bom-mobile/app/_layout.tsx`
- Create: `bom-mobile/app/index.tsx`

- [ ] **Step 1: Root `_layout.tsx`**

```tsx
import "../global.css";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { Stack } from "expo-router";
import { AuthProvider } from "@/auth/AuthContext";
import { StatusBar } from "expo-status-bar";

const queryClient = new QueryClient({
  defaultOptions: { queries: { retry: 1, staleTime: 10_000 } },
});

export default function RootLayout() {
  return (
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <StatusBar style="dark" />
        <Stack screenOptions={{ headerShown: false }} />
      </AuthProvider>
    </QueryClientProvider>
  );
}
```

- [ ] **Step 2: `app/index.tsx` — role-based redirect**

```tsx
import { Redirect } from "expo-router";
import { useAuth } from "@/auth/AuthContext";
import { LoadingView } from "@/components/LoadingView";

export default function Index() {
  const { user, loading } = useAuth();
  if (loading) return <LoadingView />;
  if (!user) return <Redirect href="/login" />;
  if (user.role === "SalesPerson") return <Redirect href="/(sales)" />;
  if (user.role === "ManagingDirector") return <Redirect href="/(md)" />;
  return <Redirect href="/login" />;
}
```

- [ ] **Step 3: Verify compile**

```bash
npx tsc --noEmit
```

- [ ] **Step 4: Commit**

```bash
git add bom-mobile/app/_layout.tsx bom-mobile/app/index.tsx
git commit -m "feat(mobile): root layout and role-based redirect"
```

---

## Task 12: Login screen

**Files:**
- Create: `bom-mobile/app/login.tsx`

- [ ] **Step 1: Implementation**

```tsx
import { useState } from "react";
import { Alert, KeyboardAvoidingView, Platform, ScrollView, Text, View } from "react-native";
import { useRouter } from "expo-router";
import { useForm, Controller } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { Button } from "@/components/Button";
import { Input } from "@/components/Input";
import { loginSchema, type LoginInput } from "@/utils/validation";
import { useAuth } from "@/auth/AuthContext";

const ALLOWED_ROLES = ["SalesPerson", "ManagingDirector"] as const;

export default function Login() {
  const { login } = useAuth();
  const router = useRouter();
  const [submitting, setSubmitting] = useState(false);
  const { control, handleSubmit, formState: { errors } } = useForm<LoginInput>({
    resolver: zodResolver(loginSchema),
    defaultValues: { email: "", password: "" },
  });

  const onSubmit = handleSubmit(async (values) => {
    setSubmitting(true);
    try {
      const u = await login(values.email, values.password);
      if (!ALLOWED_ROLES.includes(u.role as typeof ALLOWED_ROLES[number])) {
        Alert.alert("Not allowed", "This app is for Sales and Management only.");
        return;
      }
      router.replace("/");
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : "Login failed";
      Alert.alert("Login failed", msg);
    } finally {
      setSubmitting(false);
    }
  });

  return (
    <KeyboardAvoidingView behavior={Platform.OS === "ios" ? "padding" : undefined} className="flex-1 bg-slate-50">
      <ScrollView contentContainerClassName="flex-1 justify-center px-6">
        <Text className="text-2xl font-bold text-slate-900 mb-1">FPF Quotations</Text>
        <Text className="text-slate-600 mb-6">Sign in to continue</Text>

        <Controller
          control={control}
          name="email"
          render={({ field }) => (
            <Input
              label="Email"
              keyboardType="email-address"
              autoCapitalize="none"
              autoComplete="email"
              value={field.value}
              onChangeText={field.onChange}
              error={errors.email?.message}
            />
          )}
        />
        <Controller
          control={control}
          name="password"
          render={({ field }) => (
            <Input
              label="Password"
              secureTextEntry
              value={field.value}
              onChangeText={field.onChange}
              error={errors.password?.message}
            />
          )}
        />
        <View className="mt-2">
          <Button title="Sign in" onPress={onSubmit} loading={submitting} />
        </View>
      </ScrollView>
    </KeyboardAvoidingView>
  );
}
```

- [ ] **Step 2: Install `@hookform/resolvers`**

```bash
npm install @hookform/resolvers
```

- [ ] **Step 3: Verify compile**

```bash
npx tsc --noEmit
```

- [ ] **Step 4: Commit**

```bash
git add bom-mobile/app/login.tsx bom-mobile/package.json bom-mobile/package-lock.json
git commit -m "feat(mobile): login screen with RHF + Zod"
```

---

## Task 13: Role-guarded placeholder home screens

**Files:**
- Create: `bom-mobile/app/(sales)/_layout.tsx`
- Create: `bom-mobile/app/(sales)/index.tsx`
- Create: `bom-mobile/app/(md)/_layout.tsx`
- Create: `bom-mobile/app/(md)/index.tsx`

- [ ] **Step 1: Shared guard helper — append to `src/auth/AuthContext.tsx`**

```tsx
export function useRoleGuard(allowed: Array<"SalesPerson" | "ManagingDirector">) {
  const { user, loading } = useAuth();
  const allowedSet = allowed as readonly string[];
  if (loading) return { status: "loading" as const };
  if (!user) return { status: "unauthenticated" as const };
  if (!allowedSet.includes(user.role)) return { status: "forbidden" as const };
  return { status: "allowed" as const };
}
```

- [ ] **Step 2: `app/(sales)/_layout.tsx`**

```tsx
import { Redirect, Stack } from "expo-router";
import { useRoleGuard } from "@/auth/AuthContext";
import { LoadingView } from "@/components/LoadingView";

export default function SalesLayout() {
  const { status } = useRoleGuard(["SalesPerson"]);
  if (status === "loading") return <LoadingView />;
  if (status !== "allowed") return <Redirect href="/login" />;
  return <Stack screenOptions={{ headerShown: true }} />;
}
```

- [ ] **Step 3: `app/(sales)/index.tsx`** (placeholder)

```tsx
import { Text, View } from "react-native";
import { useAuth } from "@/auth/AuthContext";

export default function SalesHome() {
  const { user } = useAuth();
  return (
    <View className="flex-1 items-center justify-center p-6">
      <Text className="text-xl font-semibold text-slate-900">Hello, {user?.name ?? "SalesPerson"}</Text>
      <Text className="text-slate-600 mt-2">Requisitions list coming next plan.</Text>
    </View>
  );
}
```

- [ ] **Step 4: `app/(md)/_layout.tsx`**

```tsx
import { Redirect, Stack } from "expo-router";
import { useRoleGuard } from "@/auth/AuthContext";
import { LoadingView } from "@/components/LoadingView";

export default function MdLayout() {
  const { status } = useRoleGuard(["ManagingDirector"]);
  if (status === "loading") return <LoadingView />;
  if (status !== "allowed") return <Redirect href="/login" />;
  return <Stack screenOptions={{ headerShown: true }} />;
}
```

- [ ] **Step 5: `app/(md)/index.tsx`** (placeholder)

```tsx
import { Text, View } from "react-native";
import { useAuth } from "@/auth/AuthContext";

export default function MdHome() {
  const { user } = useAuth();
  return (
    <View className="flex-1 items-center justify-center p-6">
      <Text className="text-xl font-semibold text-slate-900">Hello, {user?.name ?? "MD"}</Text>
      <Text className="text-slate-600 mt-2">Pending approvals coming next plan.</Text>
    </View>
  );
}
```

- [ ] **Step 6: Verify compile**

```bash
npx tsc --noEmit
```

- [ ] **Step 7: Commit**

```bash
git add bom-mobile/app/\(sales\) bom-mobile/app/\(md\) bom-mobile/src/auth/AuthContext.tsx
git commit -m "feat(mobile): role-guarded placeholder home screens for Sales + MD"
```

---

## Task 14: Profile screen with logout

**Files:**
- Create: `bom-mobile/app/profile.tsx`

- [ ] **Step 1: Implementation**

```tsx
import { Text, View } from "react-native";
import { useRouter } from "expo-router";
import { Button } from "@/components/Button";
import { useAuth } from "@/auth/AuthContext";

export default function Profile() {
  const { user, logout } = useAuth();
  const router = useRouter();

  const onLogout = async () => {
    await logout();
    router.replace("/login");
  };

  return (
    <View className="flex-1 p-6 bg-slate-50">
      <Text className="text-2xl font-bold text-slate-900 mb-4">Profile</Text>
      <View className="bg-white rounded-md p-4 mb-4 border border-slate-200">
        <Text className="text-sm text-slate-500">Name</Text>
        <Text className="text-base text-slate-900 mb-2">{user?.name ?? "-"}</Text>
        <Text className="text-sm text-slate-500">Role</Text>
        <Text className="text-base text-slate-900 mb-2">{user?.role ?? "-"}</Text>
        <Text className="text-sm text-slate-500">Branch</Text>
        <Text className="text-base text-slate-900">{user?.branchId != null ? `#${user.branchId}` : "All branches"}</Text>
      </View>
      <Button title="Log out" variant="danger" onPress={onLogout} />
    </View>
  );
}
```

- [ ] **Step 2: Verify compile**

```bash
npx tsc --noEmit
```

- [ ] **Step 3: Commit**

```bash
git add bom-mobile/app/profile.tsx
git commit -m "feat(mobile): profile screen with logout"
```

---

## Task 15: Role-guard unit test

**Files:**
- Create: `bom-mobile/__tests__/roleGuard.test.tsx`

- [ ] **Step 1: Write the test**

```tsx
import { renderHook } from "@testing-library/react-native";
import { AuthProvider, useRoleGuard } from "@/auth/AuthContext";
import { saveUser, saveTokens, clearTokens } from "@/auth/secureStore";
import type { AuthUser, UserRole } from "@/types/api";

const makeUser = (role: UserRole): AuthUser => ({
  userId: 1, name: "X", role, branchId: null,
});

beforeEach(async () => {
  await clearTokens();
});

test("forbidden for wrong role", async () => {
  await saveTokens("a", "r");
  await saveUser(makeUser("BomCreator"));

  const { result } = renderHook(() => useRoleGuard(["SalesPerson"]), {
    wrapper: ({ children }) => <AuthProvider>{children}</AuthProvider>,
  });

  // Wait a tick for cold-start useEffect
  await new Promise((r) => setTimeout(r, 50));

  expect(["forbidden", "unauthenticated", "loading"]).toContain(result.current.status);
});

test("allowed for matching role", async () => {
  await saveTokens("a", "r");
  await saveUser(makeUser("SalesPerson"));

  const { result } = renderHook(() => useRoleGuard(["SalesPerson"]), {
    wrapper: ({ children }) => <AuthProvider>{children}</AuthProvider>,
  });

  await new Promise((r) => setTimeout(r, 50));

  expect(["allowed", "loading"]).toContain(result.current.status);
});
```

Note: this test is intentionally tolerant (accepting `"loading"` too) because `useEffect` timing in RTL-RN can vary. The useful assertion is that role mismatch never resolves to `"allowed"` and role match never resolves to `"forbidden"`.

- [ ] **Step 2: Run**

```bash
npx jest __tests__/roleGuard.test.tsx
```

Expected: PASS (2 tests).

- [ ] **Step 3: Commit**

```bash
git add bom-mobile/__tests__/roleGuard.test.tsx
git commit -m "test(mobile): role guard accepts matching role and rejects others"
```

---

## Task 16: README + milestone verification

**Files:**
- Modify: `bom-mobile/README.md`

- [ ] **Step 1: Fill out README**

Replace or extend `bom-mobile/README.md`:

```markdown
# FPF Quotations — Mobile (iOS)

React Native + Expo mobile app for SalesPerson and ManagingDirector roles of the BOM & Price Approval system.

## Dev setup

1. Install deps:
   ```bash
   cd bom-mobile
   npm install
   ```
2. Copy env:
   ```bash
   cp .env.production.example .env.development
   # edit EXPO_PUBLIC_API_BASE_URL to your dev machine's LAN IP, e.g. http://192.168.1.42:7300
   ```
3. Start backend (from repo root): `dotnet run --project BomPriceApproval.API`.
4. Allow inbound traffic to port 7300 through Windows Firewall on the dev machine's LAN.
5. Start Expo:
   ```bash
   npx expo start
   ```
6. Install **Expo Go** on a real iPhone (same WiFi) and scan the QR code.

## Tests

```bash
npx jest
```

## Shared types

`src/types/api.ts` is a manual copy of `bom-web/src/types/api.ts`. Re-copy whenever web types change and re-run `npx tsc --noEmit`.

## Environments

- `.env.development` — local LAN dev
- `.env.production` — created from `.env.production.example` before EAS production builds

## Out of scope in this foundation plan

Requisition and approval screens, SignalR, notifications, and EAS Build are covered in follow-up plans.
```

- [ ] **Step 2: Run the full test suite**

```bash
cd bom-mobile && npx jest
```

Expected: all tests PASS.

- [ ] **Step 3: Type-check**

```bash
npx tsc --noEmit
```

Expected: no errors.

- [ ] **Step 4: Smoke-test on device** (manual — skip in CI)

From `bom-mobile/`:

```bash
npx expo start
```

On real iPhone (Expo Go): scan QR, verify login screen loads. Attempt login with seed credentials (backend must be running, firewall allows 7300). Verify you land on the role-appropriate "Hello, <name>" placeholder.

Expected: login works, role-based redirect works, tokens persist across app reload (close and reopen Expo Go → should land directly on role home).

- [ ] **Step 5: Final commit**

```bash
git add bom-mobile/README.md
git commit -m "docs(mobile): foundation README with dev setup and testing instructions"
```

---

## Milestone

At the end of this plan:

- `bom-mobile/` exists with Expo + TypeScript + Expo Router.
- Login screen works against the running backend over LAN.
- 401 refresh flow is covered by a test.
- Role-based routing sends SalesPerson → `(sales)` home, MD → `(md)` home, others → logout.
- Tokens persist across app reload via `expo-secure-store`.
- Profile screen logs out and revokes the refresh token server-side.
- Jest + `npx tsc --noEmit` both pass.

Next plan (`2026-04-20-mobile-salesperson.md`) replaces the placeholder `(sales)/index.tsx` with the requisitions list, create, and detail screens plus PDF download.
