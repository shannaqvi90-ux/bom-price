# Notifications Design

## Goal

Surface real-time workflow notifications to all users — unread badge in the sidebar, full list at `/notifications` with Unread / All tabs, and click-to-navigate to the related requisition.

## Decisions Made

| Question | Decision |
|---|---|
| Real-time updates | Yes — SignalR (`"ReceiveNotification"` event) |
| Unread badge | Yes — on the bell icon in Sidebar, shows live count |
| Click behaviour | Navigate to `/requisitions/{referenceId}` and mark as read |
| Page layout | Tabs: Unread (N) / All |
| SignalR wiring | Global Zustand store (`notificationsStore`), connected on Sidebar mount |

---

## Backend (already complete)

All backend infrastructure exists and requires no changes:

| Endpoint | Description |
|---|---|
| `GET /api/notifications` | All notifications for current user, newest first |
| `PUT /api/notifications/{id}/read` | Mark single notification as read → 204 |
| `PUT /api/notifications/read-all` | Mark all as read → 204 |
| `GET /api/notifications/unread-count` | Returns `{ count: number }` |

**SignalR hub:** `/hubs/notifications` — authenticates via `?access_token=` query param. Emits `"ReceiveNotification"` to the user's group (`user_{userId}`) with payload:

```json
{
  "id": 12,
  "message": "BOM ready for costing: REQ-0042",
  "referenceId": 42,
  "referenceType": "QuotationRequest",
  "isRead": false,
  "createdAt": "2026-04-15T07:00:00Z"
}
```

---

## Frontend

### New Type (`bom-web/src/types/api.ts`)

```typescript
export interface Notification {
  id: number;
  message: string;
  referenceId: number;
  referenceType: string;
  isRead: boolean;
  createdAt: string;
}
```

### New Files

**`bom-web/src/store/notificationsStore.ts`**

Zustand store. Holds:
- `notifications: Notification[]`
- `unreadCount: number`
- `connected: boolean`

Actions:
- `connect(token: string)` — opens SignalR connection to `/hubs/notifications?access_token=<token>`, fetches initial unread count from `GET /api/notifications/unread-count`, registers `"ReceiveNotification"` handler. Guards against double-connection with `connected` flag.
- `disconnect()` — stops the SignalR connection, resets state
- `setNotifications(ns: Notification[])` — called by the page on initial load
- `prependNotification(n: Notification)` — called by SignalR handler; increments `unreadCount`
- `markRead(id: number)` — flips `isRead` on the notification, decrements `unreadCount`
- `markAllRead()` — sets all `isRead` to true, zeroes `unreadCount`

---

**`bom-web/src/features/notifications/notificationsApi.ts`**

TanStack Query hooks:
- `useNotifications()` — `GET /api/notifications`; on success, calls `notificationsStore.setNotifications(data)`
- `useMarkRead()` — `PUT /api/notifications/{id}/read` mutation; on success, calls `notificationsStore.markRead(id)`
- `useMarkAllRead()` — `PUT /api/notifications/read-all` mutation; on success, calls `notificationsStore.markAllRead()`

---

**`bom-web/src/features/notifications/NotificationsPage.tsx`**

- Calls `useNotifications()` on mount to populate the store
- Reads `notifications` and `unreadCount` from `notificationsStore`
- Local state: `activeTab: "unread" | "all"` (default `"unread"`)
- `filteredItems`:
  - `"unread"` tab → `notifications.filter(n => !n.isRead)`
  - `"all"` tab → all notifications
- Header: "Notifications" title + "Mark all read" button (visible only on Unread tab when `unreadCount > 0`)
- Tabs: **Unread (N)** | **All**
- Notification row:
  - Unread: `bg-muted/40` background + blue dot indicator
  - Read: normal background, `text-muted-foreground`
  - Message text + relative timestamp
  - `onClick`: calls `useMarkRead()` mutation, then `navigate(`/requisitions/${n.referenceId}`)`
- Empty states:
  - Unread tab: "You're all caught up."
  - All tab: "No notifications yet."
- New notifications from the store prepend to the list live (no refetch needed — store is the source of truth)

---

**`bom-web/src/features/notifications/NotificationsPage.test.tsx`**

Tests:
1. Renders "You're all caught up." when there are no unread notifications
2. Shows unread notifications on the Unread tab with highlighted background
3. Switching to All tab shows read notifications too
4. Clicking a notification calls mark-read mutation and navigates to the requisition
5. "Mark all read" button calls the mutation

### Modified Files

**`bom-web/src/components/layout/Sidebar.tsx`**

- Import `notificationsStore`
- In component: `const { connect, unreadCount } = notificationsStore()`
- `useEffect(() => { if (token) connect(token); }, [token])` — connects on mount
- Bell icon for the Notifications nav item gets a badge: red circle with white count text, positioned absolute top-right of the icon. Renders only when `unreadCount > 0`. Displays `unreadCount > 99 ? "99+" : unreadCount`.

**`bom-web/src/App.tsx` (router)**

Add route: `{ path: "/notifications", element: <NotificationsPage /> }`

---

## Dependency

`@microsoft/signalr` npm package must be installed in `bom-web/`:
```bash
cd bom-web && npm install @microsoft/signalr
```

---

## File Summary

| File | Change |
|---|---|
| `bom-web/src/types/api.ts` | Add `Notification` interface |
| `bom-web/src/store/notificationsStore.ts` | New — Zustand store with SignalR connection |
| `bom-web/src/features/notifications/notificationsApi.ts` | New — TanStack Query hooks |
| `bom-web/src/features/notifications/NotificationsPage.tsx` | New — page with Unread/All tabs |
| `bom-web/src/features/notifications/NotificationsPage.test.tsx` | New — 5 tests |
| `bom-web/src/components/layout/Sidebar.tsx` | Add badge + connect on mount |
| `bom-web/src/App.tsx` | Add `/notifications` route |
