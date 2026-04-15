# Users Management — Design Spec

**Date:** 2026-04-15
**Feature:** Admin Users page — list, add, edit, deactivate

---

## Overview

An Admin-only page at `/admin/users` for managing user accounts. Admins can view all users, create new users with an initial password, edit existing users, and deactivate users. No branch assignment — all roles operate without branch restriction.

---

## Backend

All endpoints already exist under `GET/POST/PUT/DELETE /api/users` (Admin-only). No backend changes required.

**Relevant DTOs:**
- `UserResponse(Id, Name, Email, Role, BranchId, BranchName, IsActive)`
- `CreateUserRequest(Name, Email, Password, UserRole, BranchId?)` — BranchId always null
- `UpdateUserRequest(Name, Email, UserRole, BranchId?, IsActive)` — BranchId always null

**Roles enum:** `Admin`, `SalesPerson`, `BomCreator`, `Accountant`, `ManagingDirector`

---

## Frontend Architecture

### New files
```
bom-web/src/features/users/usersApi.ts          — TanStack Query hooks
bom-web/src/features/users/UsersPage.tsx         — page with DataTable + modals
bom-web/src/features/users/AddUserModal.tsx      — create user form
bom-web/src/features/users/EditUserModal.tsx     — edit user form
bom-web/src/features/users/UsersPage.test.tsx    — tests
```

### Modified files
```
bom-web/src/types/api.ts      — add User, CreateUserRequest, UpdateUserRequest types
bom-web/src/App.tsx           — add /admin/users route
```

---

## Data Layer — `usersApi.ts`

Four hooks, all using query key `["users"]`:

| Hook | Method | Endpoint | On success |
|------|--------|----------|------------|
| `useUsers()` | GET | `/users` | — |
| `useCreateUser()` | POST | `/users` | invalidate `["users"]` |
| `useUpdateUser()` | PUT | `/users/:id` | invalidate `["users"]` |
| `useDeactivateUser()` | DELETE | `/users/:id` | invalidate `["users"]` |

---

## UsersPage

- Fetches users via `useUsers()`
- DataTable with columns: Name, Email, Role, Active badge, Actions
- **Active badge:** green "Active" / grey "Inactive"
- **Actions column:** Edit button (always visible) + Deactivate button (only when `isActive === true`)
- **Add User button** in page header (Admin always has this role to reach the page)
- State: `addOpen: boolean`, `editTarget: UserResponse | null`

---

## Table Columns

| Column | Value |
|--------|-------|
| Name | `user.name` |
| Email | `user.email` |
| Role | `user.role` (string as-is from API) |
| Active | Green "Active" badge if `isActive`, grey "Inactive" if not |
| Actions | Edit button + Deactivate button (hidden when inactive) |

---

## Add User Modal

**Fields:**
- Name — required, text input
- Email — required, email format (zod: `z.string().email()`)
- Password — required, min 8 characters
- Role — required, select: `Admin | SalesPerson | BomCreator | Accountant | ManagingDirector`

`BranchId` is always sent as `null`.

**Behaviour:**
- Submit calls `useCreateUser()`
- On success: modal closes, form resets
- On error: inline server error message (e.g. "Email already exists")
- Cancel: form resets, modal closes

---

## Edit User Modal

**Fields (pre-populated from selected row):**
- Name — required, text input
- Email — required, email format
- Role — required, select (same values as Add)
- Is Active — checkbox (allows reactivating a deactivated user)

No password field — password is set at creation only.

**Behaviour:**
- Submit calls `useUpdateUser()` with `{ name, email, role, branchId: null, isActive }`
- On success: modal closes
- On error: inline server error message
- Cancel: modal closes

---

## Deactivate Action

- Deactivate button visible only when `user.isActive === true`
- Clicking opens a confirmation dialog: *"Are you sure you want to deactivate [user name]?"* with Confirm and Cancel buttons
- Confirm calls `useDeactivateUser(id)`
- Cancel dismisses without action
- Confirmation state held in component: `deactivateTarget: UserResponse | null`

---

## Routing

Add to `App.tsx` inside the authenticated layout:

```tsx
{ path: "admin/users", element: <RoleGuard roles={["Admin"]}><UsersPage /></RoleGuard> }
```

The `/admin/users` link already exists in `Sidebar.tsx`.

---

## Testing — `UsersPage.test.tsx`

Pattern follows `CustomerListPage.test.tsx` (mock `api`, `useAuthStore.setSession`).

**Test cases:**

| Suite | Test |
|-------|------|
| UsersPage | Renders user rows (name, email, role, active badge) |
| UsersPage | Deactivate button hidden for inactive users |
| UsersPage | Opens AddUserModal on "Add User" click |
| UsersPage | Opens EditUserModal pre-populated when Edit clicked |
| UsersPage | Deactivate shows confirmation dialog; cancel dismisses |
| UsersPage | Deactivate confirm calls DELETE and refreshes list |
| AddUserModal | Shows validation errors for empty required fields |
| AddUserModal | Submits correct payload (with branchId: null) and closes |
| AddUserModal | Shows server error on failure |
| EditUserModal | Pre-populates fields from row data |
| EditUserModal | Submits correct payload and closes |
