# Notifications Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Surface real-time workflow notifications — unread badge in the sidebar, a `/notifications` page with Unread/All tabs, live SignalR push, and click-to-navigate to the related requisition.

**Architecture:** A Zustand store (`notificationsStore`) owns all notification state and holds the SignalR connection; it is connected once on Sidebar mount and stays live for the whole session. TanStack Query hooks in `notificationsApi.ts` handle the REST calls (fetch list, mark-read, mark-all-read) and call back into the store on success. The page reads purely from the store — no extra data-fetching beyond the initial list load.

**Tech Stack:** React 19, TypeScript 5, Zustand v5, TanStack Query v5, `@microsoft/signalr`, Vitest + React Testing Library, react-router-dom v6.

---

## File Map

| File | Action | Purpose |
|---|---|---|
| `bom-web/src/types/api.ts` | Modify | Add `Notification` interface |
| `bom-web/src/store/notificationsStore.ts` | Create | Zustand store + SignalR connection logic |
| `bom-web/src/features/notifications/notificationsApi.ts` | Create | TanStack Query hooks for notifications REST calls |
| `bom-web/src/features/notifications/NotificationsPage.tsx` | Create | Unread/All tabs page |
| `bom-web/src/features/notifications/NotificationsPage.test.tsx` | Create | 5 unit tests |
| `bom-web/src/components/layout/Sidebar.tsx` | Modify | Connect on mount + unread badge on Bell icon |
| `bom-web/src/App.tsx` | Modify | Add `/notifications` route |

---

## Task 1: Install @microsoft/signalr + Add Notification type

**Files:**
- Modify: `bom-web/package.json` (via npm install)
- Modify: `bom-web/src/types/api.ts`

- [ ] **Step 1: Install the SignalR client package**

```bash
cd bom-web && npm install @microsoft/signalr
```

Expected: package appears in `package.json` dependencies and `node_modules/@microsoft/signalr` exists.

- [ ] **Step 2: Add Notification interface to types/api.ts**

Open `bom-web/src/types/api.ts`. Append this block at the very end of the file:

```typescript
// ─── Notifications ────────────────────────────────────────────────────────────

export interface Notification {
  id: number;
  message: string;
  referenceId: number;
  referenceType: string;
  isRead: boolean;
  createdAt: string;
}
```

- [ ] **Step 3: Verify TypeScript compiles**

```bash
cd bom-web && npx tsc --noEmit
```

Expected: no errors.

- [ ] **Step 4: Commit**

```bash
cd bom-web && git add package.json package-lock.json src/types/api.ts
git commit -m "feat(notifications): install signalr client, add Notification type"
```

---

## Task 2: Create notificationsStore

**Files:**
- Create: `bom-web/src/store/notificationsStore.ts`

The store is a Zustand plain store (no persist — notifications are session-only). It owns the SignalR `HubConnection` instance privately and exposes only the actions the UI needs.

- [ ] **Step 1: Create the store file**

Create `bom-web/src/store/notificationsStore.ts` with:

```typescript
import { create } from "zustand";
import * as signalR from "@microsoft/signalr";
import { api } from "@/api/axios";
import type { Notification } from "@/types/api";

interface NotificationsState {
  notifications: Notification[];
  unreadCount: number;
  connected: boolean;
  _connection: signalR.HubConnection | null;
  connect: (token: string) => Promise<void>;
  disconnect: () => Promise<void>;
  setNotifications: (ns: Notification[]) => void;
  prependNotification: (n: Notification) => void;
  markRead: (id: number) => void;
  markAllRead: () => void;
}

export const notificationsStore = create<NotificationsState>()((set, get) => ({
  notifications: [],
  unreadCount: 0,
  connected: false,
  _connection: null,

  connect: async (token: string) => {
    if (get().connected) return;

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`/hubs/notifications?access_token=${token}`)
      .withAutomaticReconnect()
      .build();

    connection.on("ReceiveNotification", (n: Notification) => {
      get().prependNotification(n);
    });

    await connection.start();

    const { data } = await api.get<{ count: number }>("/notifications/unread-count");
    set({ connected: true, _connection: connection, unreadCount: data.count });
  },

  disconnect: async () => {
    const conn = get()._connection;
    if (conn) await conn.stop();
    set({ connected: false, _connection: null, notifications: [], unreadCount: 0 });
  },

  setNotifications: (ns: Notification[]) => set({ notifications: ns }),

  prependNotification: (n: Notification) =>
    set((state) => ({
      notifications: [n, ...state.notifications],
      unreadCount: state.unreadCount + 1,
    })),

  markRead: (id: number) =>
    set((state) => ({
      notifications: state.notifications.map((n) =>
        n.id === id ? { ...n, isRead: true } : n,
      ),
      unreadCount: Math.max(0, state.unreadCount - 1),
    })),

  markAllRead: () =>
    set((state) => ({
      notifications: state.notifications.map((n) => ({ ...n, isRead: true })),
      unreadCount: 0,
    })),
}));
```

- [ ] **Step 2: Verify TypeScript compiles**

```bash
cd bom-web && npx tsc --noEmit
```

Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add bom-web/src/store/notificationsStore.ts
git commit -m "feat(notifications): add notificationsStore with SignalR connection"
```

---

## Task 3: Create notificationsApi

**Files:**
- Create: `bom-web/src/features/notifications/notificationsApi.ts`

These hooks are the only place that calls the notifications REST endpoints. Each hook calls back into the Zustand store on success so the store stays as the single source of truth.

- [ ] **Step 1: Create the API hooks file**

Create `bom-web/src/features/notifications/notificationsApi.ts` with:

```typescript
import { useMutation, useQuery } from "@tanstack/react-query";
import { api } from "@/api/axios";
import { notificationsStore } from "@/store/notificationsStore";
import type { Notification } from "@/types/api";

export function useNotifications() {
  const setNotifications = notificationsStore((s) => s.setNotifications);
  return useQuery({
    queryKey: ["notifications"],
    queryFn: async () => {
      const { data } = await api.get<Notification[]>("/notifications");
      setNotifications(data);
      return data;
    },
  });
}

export function useMarkRead() {
  const markRead = notificationsStore((s) => s.markRead);
  return useMutation({
    mutationFn: (id: number) =>
      api.put(`/notifications/${id}/read`).then(() => id),
    onSuccess: (id: number) => markRead(id),
  });
}

export function useMarkAllRead() {
  const markAllRead = notificationsStore((s) => s.markAllRead);
  return useMutation({
    mutationFn: () => api.put("/notifications/read-all"),
    onSuccess: () => markAllRead(),
  });
}
```

- [ ] **Step 2: Verify TypeScript compiles**

```bash
cd bom-web && npx tsc --noEmit
```

Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add bom-web/src/features/notifications/notificationsApi.ts
git commit -m "feat(notifications): add notificationsApi TanStack Query hooks"
```

---

## Task 4: Write failing tests for NotificationsPage

**Files:**
- Create: `bom-web/src/features/notifications/NotificationsPage.test.tsx`

Write 5 tests that test the NotificationsPage behaviour. The page doesn't exist yet so all tests will fail.

- [ ] **Step 1: Create the test file**

Create `bom-web/src/features/notifications/NotificationsPage.test.tsx` with:

```typescript
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { MemoryRouter } from "react-router-dom";
import type { ReactNode } from "react";
import type { Notification } from "@/types/api";

// ── mock the API hooks ──────────────────────────────────────────────────────
const mockMarkReadMutate = vi.fn();
const mockMarkAllReadMutate = vi.fn();

vi.mock("./notificationsApi", () => ({
  useNotifications: vi.fn(() => ({ isLoading: false })),
  useMarkRead: vi.fn(() => ({ mutate: mockMarkReadMutate, isPending: false })),
  useMarkAllRead: vi.fn(() => ({ mutate: mockMarkAllReadMutate, isPending: false })),
}));

// ── mock the store ──────────────────────────────────────────────────────────
let mockNotifications: Notification[] = [];
let mockUnreadCount = 0;

vi.mock("@/store/notificationsStore", () => ({
  notificationsStore: vi.fn((selector?: (s: unknown) => unknown) => {
    const state = {
      notifications: mockNotifications,
      unreadCount: mockUnreadCount,
      connect: vi.fn(),
      disconnect: vi.fn(),
      setNotifications: vi.fn(),
      prependNotification: vi.fn(),
      markRead: vi.fn(),
      markAllRead: vi.fn(),
      connected: false,
      _connection: null,
    };
    return selector ? selector(state) : state;
  }),
}));

// ── mock navigate ───────────────────────────────────────────────────────────
const mockNavigate = vi.fn();
vi.mock("react-router-dom", async (importOriginal) => {
  const actual = await importOriginal<typeof import("react-router-dom")>();
  return { ...actual, useNavigate: () => mockNavigate };
});

import NotificationsPage from "./NotificationsPage";

function wrap(ui: ReactNode) {
  return <MemoryRouter>{ui}</MemoryRouter>;
}

const unread: Notification = {
  id: 1,
  message: "BOM ready for costing: REQ-0042",
  referenceId: 42,
  referenceType: "QuotationRequest",
  isRead: false,
  createdAt: new Date().toISOString(),
};

const read: Notification = {
  id: 2,
  message: "Quotation approved: REQ-0038",
  referenceId: 38,
  referenceType: "QuotationRequest",
  isRead: true,
  createdAt: new Date().toISOString(),
};

describe("NotificationsPage", () => {
  beforeEach(() => {
    mockNotifications = [];
    mockUnreadCount = 0;
    mockMarkReadMutate.mockReset();
    mockMarkAllReadMutate.mockReset();
    mockNavigate.mockReset();
  });

  it("shows empty state on Unread tab when there are no unread notifications", () => {
    mockNotifications = [read];
    mockUnreadCount = 0;
    render(wrap(<NotificationsPage />));
    expect(screen.getByText("You're all caught up.")).toBeInTheDocument();
  });

  it("shows unread notification with highlighted background on Unread tab", () => {
    mockNotifications = [unread];
    mockUnreadCount = 1;
    render(wrap(<NotificationsPage />));
    const row = screen.getByText("BOM ready for costing: REQ-0042").closest("button");
    expect(row).toHaveClass("bg-muted/40");
  });

  it("shows read notifications when All tab is selected", () => {
    mockNotifications = [unread, read];
    mockUnreadCount = 1;
    render(wrap(<NotificationsPage />));
    fireEvent.click(screen.getByRole("button", { name: /^All$/ }));
    expect(screen.getByText("Quotation approved: REQ-0038")).toBeInTheDocument();
  });

  it("calls mark-read mutation and navigates to requisition on notification click", async () => {
    mockNotifications = [unread];
    mockUnreadCount = 1;
    // Simulate mutation calling onSuccess
    mockMarkReadMutate.mockImplementation((_id: number, opts?: { onSuccess?: () => void }) => {
      opts?.onSuccess?.();
    });
    render(wrap(<NotificationsPage />));
    fireEvent.click(screen.getByText("BOM ready for costing: REQ-0042").closest("button")!);
    expect(mockMarkReadMutate).toHaveBeenCalledWith(1, expect.objectContaining({ onSuccess: expect.any(Function) }));
    await waitFor(() => expect(mockNavigate).toHaveBeenCalledWith("/requisitions/42"));
  });

  it("calls mark-all-read mutation when Mark all read button is clicked", () => {
    mockNotifications = [unread];
    mockUnreadCount = 1;
    render(wrap(<NotificationsPage />));
    fireEvent.click(screen.getByRole("button", { name: /mark all read/i }));
    expect(mockMarkAllReadMutate).toHaveBeenCalled();
  });
});
```

- [ ] **Step 2: Run tests to confirm they fail (page not yet implemented)**

```bash
cd bom-web && npx vitest run src/features/notifications/NotificationsPage.test.tsx
```

Expected: 5 failures — `Cannot find module './NotificationsPage'` or similar.

---

## Task 5: Implement NotificationsPage

**Files:**
- Create: `bom-web/src/features/notifications/NotificationsPage.tsx`

Implement the page so all 5 tests pass. The page reads from the store and calls API hooks.

- [ ] **Step 1: Create the page component**

Create `bom-web/src/features/notifications/NotificationsPage.tsx` with:

```typescript
import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { Button } from "@/components/ui/Button";
import { Card, CardContent } from "@/components/ui/Card";
import { notificationsStore } from "@/store/notificationsStore";
import { useNotifications, useMarkRead, useMarkAllRead } from "./notificationsApi";
import type { Notification } from "@/types/api";
import { cn } from "@/lib/cn";

function relativeTime(iso: string): string {
  const diff = Date.now() - new Date(iso).getTime();
  const mins = Math.floor(diff / 60000);
  if (mins < 1) return "just now";
  if (mins < 60) return `${mins}m ago`;
  const hrs = Math.floor(mins / 60);
  if (hrs < 24) return `${hrs}h ago`;
  return `${Math.floor(hrs / 24)}d ago`;
}

export default function NotificationsPage() {
  const navigate = useNavigate();
  const { notifications, unreadCount } = notificationsStore();
  const [activeTab, setActiveTab] = useState<"unread" | "all">("unread");

  useNotifications();

  const markRead = useMarkRead();
  const markAllRead = useMarkAllRead();

  const filteredItems =
    activeTab === "unread" ? notifications.filter((n) => !n.isRead) : notifications;

  const handleClick = (n: Notification) => {
    markRead.mutate(n.id, {
      onSuccess: () => navigate(`/requisitions/${n.referenceId}`),
    });
  };

  return (
    <div className="p-6 max-w-2xl mx-auto">
      <div className="flex items-center justify-between mb-4">
        <h1 className="text-xl font-semibold">Notifications</h1>
        {activeTab === "unread" && unreadCount > 0 && (
          <Button variant="ghost" size="sm" onClick={() => markAllRead.mutate()}>
            Mark all read
          </Button>
        )}
      </div>

      {/* Tabs */}
      <div className="flex border-b border-border mb-4">
        <button
          type="button"
          className={cn(
            "px-4 py-2 text-sm font-medium border-b-2 -mb-px transition-colors",
            activeTab === "unread"
              ? "border-primary text-primary"
              : "border-transparent text-muted-foreground hover:text-foreground",
          )}
          onClick={() => setActiveTab("unread")}
        >
          Unread ({unreadCount})
        </button>
        <button
          type="button"
          className={cn(
            "px-4 py-2 text-sm font-medium border-b-2 -mb-px transition-colors",
            activeTab === "all"
              ? "border-primary text-primary"
              : "border-transparent text-muted-foreground hover:text-foreground",
          )}
          onClick={() => setActiveTab("all")}
        >
          All
        </button>
      </div>

      {/* Notification list */}
      <Card>
        <CardContent className="p-0">
          {filteredItems.length === 0 ? (
            <p className="p-6 text-center text-muted-foreground text-sm">
              {activeTab === "unread" ? "You're all caught up." : "No notifications yet."}
            </p>
          ) : (
            filteredItems.map((n) => (
              <button
                key={n.id}
                type="button"
                className={cn(
                  "w-full flex items-start justify-between gap-4 px-4 py-3 border-b border-border last:border-0 text-left hover:bg-muted/60 transition-colors",
                  !n.isRead && "bg-muted/40",
                )}
                onClick={() => handleClick(n)}
              >
                <div className="flex items-start gap-3 min-w-0">
                  {!n.isRead && (
                    <span className="mt-1.5 h-2 w-2 shrink-0 rounded-full bg-primary" />
                  )}
                  <span
                    className={cn(
                      "text-sm truncate",
                      n.isRead ? "text-muted-foreground pl-5" : "",
                    )}
                  >
                    {n.message}
                  </span>
                </div>
                <span className="text-xs text-muted-foreground shrink-0 whitespace-nowrap">
                  {relativeTime(n.createdAt)}
                </span>
              </button>
            ))
          )}
        </CardContent>
      </Card>
    </div>
  );
}
```

- [ ] **Step 2: Run the tests and verify all 5 pass**

```bash
cd bom-web && npx vitest run src/features/notifications/NotificationsPage.test.tsx
```

Expected: 5 tests pass, 0 failures.

- [ ] **Step 3: Run the full test suite to check for regressions**

```bash
cd bom-web && npx vitest run
```

Expected: all tests pass.

- [ ] **Step 4: Commit**

```bash
git add bom-web/src/features/notifications/NotificationsPage.tsx \
        bom-web/src/features/notifications/NotificationsPage.test.tsx
git commit -m "feat(notifications): add NotificationsPage with Unread/All tabs"
```

---

## Task 6: Sidebar badge + SignalR connect

**Files:**
- Modify: `bom-web/src/components/layout/Sidebar.tsx`

Wire the SignalR connection on mount and show an unread badge on the Bell icon.

The current Sidebar renders nav items in a loop using `Icon` components. The bell icon for `/notifications` gets special treatment: a red badge when `unreadCount > 0`.

- [ ] **Step 1: Read the current Sidebar to understand exact render structure**

Open `bom-web/src/components/layout/Sidebar.tsx` and note:
- The `visible` array is filtered from `NAV_ITEMS`
- Each item renders `<Icon className="h-4 w-4 shrink-0" />`
- The Bell item has `to: "/notifications"`

- [ ] **Step 2: Replace Sidebar.tsx with the updated version**

Replace the entire content of `bom-web/src/components/layout/Sidebar.tsx` with:

```typescript
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
  Contact,
  ChevronLeft,
  ChevronRight,
} from "lucide-react";
import { useAuthStore } from "@/store/authStore";
import { notificationsStore } from "@/store/notificationsStore";
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
    roles: ["Admin", "SalesPerson", "BomCreator", "Accountant", "ManagingDirector"],
  },
  { to: "/notifications", label: "Notifications", icon: Bell },
  {
    to: "/customers",
    label: "Customers",
    icon: Contact,
    roles: ["Admin", "SalesPerson"],
  },
  {
    to: "/items",
    label: "Items",
    icon: Package,
    roles: ["Admin", "SalesPerson", "BomCreator", "Accountant", "ManagingDirector"],
  },
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
];

const STORAGE_KEY = "bom-sidebar-collapsed";
const NARROW_BREAKPOINT = 1024;

export function Sidebar() {
  const user = useAuthStore((s) => s.user);
  const accessToken = useAuthStore((s) => s.accessToken);
  const { connect, unreadCount } = notificationsStore();

  const [userCollapsed, setUserCollapsed] = useState<boolean>(() => {
    return localStorage.getItem(STORAGE_KEY) === "true";
  });

  const [isNarrow, setIsNarrow] = useState<boolean>(
    () => window.innerWidth < NARROW_BREAKPOINT,
  );

  useEffect(() => {
    const onResize = () => setIsNarrow(window.innerWidth < NARROW_BREAKPOINT);
    window.addEventListener("resize", onResize);
    return () => window.removeEventListener("resize", onResize);
  }, []);

  useEffect(() => {
    localStorage.setItem(STORAGE_KEY, String(userCollapsed));
  }, [userCollapsed]);

  // eslint-disable-next-line react-hooks/exhaustive-deps
  useEffect(() => {
    if (accessToken) connect(accessToken);
  }, [accessToken]);

  const collapsed = isNarrow || userCollapsed;

  const visible = NAV_ITEMS.filter(
    (item) => !item.roles || (user && item.roles.includes(user.role)),
  );

  const badgeText = unreadCount > 99 ? "99+" : String(unreadCount);

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
            <span className="relative shrink-0">
              <Icon className="h-4 w-4" />
              {to === "/notifications" && unreadCount > 0 && (
                <span className="absolute -top-1.5 -right-1.5 flex h-4 min-w-[1rem] items-center justify-center rounded-full bg-destructive px-0.5 text-[10px] font-bold text-destructive-foreground leading-none">
                  {badgeText}
                </span>
              )}
            </span>
            {!collapsed && <span>{label}</span>}
          </NavLink>
        ))}
      </nav>
      <button
        type="button"
        onClick={() => setUserCollapsed((c) => !c)}
        disabled={isNarrow}
        className="flex h-10 items-center justify-center border-t border-border hover:bg-muted disabled:opacity-40 disabled:cursor-not-allowed"
        aria-label={collapsed ? "Expand sidebar" : "Collapse sidebar"}
        title={isNarrow ? "Sidebar auto-collapses on narrow screens" : undefined}
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

- [ ] **Step 3: Verify TypeScript compiles**

```bash
cd bom-web && npx tsc --noEmit
```

Expected: no errors.

- [ ] **Step 4: Run tests to confirm nothing regressed**

```bash
cd bom-web && npx vitest run
```

Expected: all tests pass.

- [ ] **Step 5: Commit**

```bash
git add bom-web/src/components/layout/Sidebar.tsx
git commit -m "feat(notifications): add unread badge + SignalR connect in Sidebar"
```

---

## Task 7: Add /notifications route to App.tsx

**Files:**
- Modify: `bom-web/src/App.tsx`

Add the `/notifications` route inside the protected shell. All authenticated roles can see notifications.

- [ ] **Step 1: Add the import and route**

In `bom-web/src/App.tsx`:

Add import at the top (after the existing imports):

```typescript
import NotificationsPage from "@/features/notifications/NotificationsPage";
```

Add the route inside the `children` array, after the `dashboard` route:

```typescript
{
  path: "notifications",
  element: (
    <ProtectedRoute
      allow={["Admin", "SalesPerson", "BomCreator", "Accountant", "ManagingDirector"]}
    >
      <NotificationsPage />
    </ProtectedRoute>
  ),
},
```

The full `children` array after the change:

```typescript
children: [
  { index: true, element: <Navigate to="/dashboard" replace /> },
  { path: "dashboard", element: <DashboardRouter /> },
  {
    path: "notifications",
    element: (
      <ProtectedRoute
        allow={["Admin", "SalesPerson", "BomCreator", "Accountant", "ManagingDirector"]}
      >
        <NotificationsPage />
      </ProtectedRoute>
    ),
  },
  {
    path: "customers",
    element: (
      <ProtectedRoute allow={["Admin", "SalesPerson"]}>
        <CustomerListPage />
      </ProtectedRoute>
    ),
  },
  // ... rest unchanged
```

- [ ] **Step 2: Verify TypeScript compiles**

```bash
cd bom-web && npx tsc --noEmit
```

Expected: no errors.

- [ ] **Step 3: Run full test suite**

```bash
cd bom-web && npx vitest run
```

Expected: all tests pass.

- [ ] **Step 4: Commit**

```bash
git add bom-web/src/App.tsx
git commit -m "feat(notifications): add /notifications route to App router"
```

---

## Self-Review Notes

**Spec coverage check:**
- ✅ `Notification` type in `types/api.ts` → Task 1
- ✅ `notificationsStore` with all 6 actions → Task 2
- ✅ `connect(token)` guards against double-connection → Task 2 (`if (get().connected) return`)
- ✅ `useNotifications()` fetches list + calls `setNotifications` → Task 3
- ✅ `useMarkRead()` calls store `markRead` on success → Task 3
- ✅ `useMarkAllRead()` calls store `markAllRead` on success → Task 3
- ✅ NotificationsPage: Unread/All tabs, empty states, row highlight, timestamp → Task 5
- ✅ Click: `markRead.mutate(id, { onSuccess: () => navigate(...) })` → Task 5
- ✅ "Mark all read" visible only on Unread tab when `unreadCount > 0` → Task 5
- ✅ 5 tests matching spec → Task 4
- ✅ Sidebar: `connect(token)` on mount, badge with `unreadCount > 99 ? "99+" : count` → Task 6
- ✅ `/notifications` route in App.tsx → Task 7
- ✅ `@microsoft/signalr` installed → Task 1

**Type consistency:**
- `Notification` defined once in Task 1, imported everywhere else
- `notificationsStore` exported from Task 2, consumed in Tasks 3, 5, 6
- `useMarkRead`, `useMarkAllRead`, `useNotifications` defined in Task 3, consumed in Tasks 4 and 5

**No placeholders:** All steps include complete code.
