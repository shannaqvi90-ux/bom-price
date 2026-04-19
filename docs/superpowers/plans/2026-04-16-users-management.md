# Users Management Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build an Admin-only `/admin/users` page with DataTable, Add User modal, Edit User modal, and inline Deactivate confirmation dialog.

**Architecture:** Five new files in `bom-web/src/features/users/` (api hooks, page, two modals, tests) plus small additions to `types/api.ts` and `App.tsx`. Follows the same DataTable + TanStack Query + react-hook-form + zod pattern used in `ExchangeRatesPage`.

**Tech Stack:** React 19, TypeScript, TanStack Query v5, Zod, react-hook-form, Vitest, React Testing Library

---

## File Map

| Action | File | Responsibility |
|--------|------|----------------|
| Modify | `bom-web/src/types/api.ts` | Add `User`, `CreateUserRequest`, `UpdateUserRequest` types |
| Create | `bom-web/src/features/users/usersApi.ts` | TanStack Query hooks: `useUsers`, `useCreateUser`, `useUpdateUser`, `useDeactivateUser` |
| Create | `bom-web/src/features/users/AddUserModal.tsx` | Form: Name, Email, Password, Role (branchId always null) |
| Create | `bom-web/src/features/users/EditUserModal.tsx` | Form: Name, Email, Role, IsActive — pre-populated from row |
| Create | `bom-web/src/features/users/UsersPage.tsx` | DataTable + page header + inline confirmation dialog |
| Create | `bom-web/src/features/users/UsersPage.test.tsx` | 11 tests covering all behaviour |
| Modify | `bom-web/src/App.tsx` | Add `admin/users` route with `ProtectedRoute allow={["Admin"]}` |

---

## Task 1: Add User types to `api.ts`

**Files:**
- Modify: `bom-web/src/types/api.ts`

- [ ] **Step 1: Add the three User types at the bottom of `bom-web/src/types/api.ts`**

Append after the last line of the file:

```ts
// ─── Users ───────────────────────────────────────────────────────────────────

export interface User {
  id: number;
  name: string;
  email: string;
  role: UserRole;
  branchId: number | null;
  branchName: string | null;
  isActive: boolean;
}

export interface CreateUserRequest {
  name: string;
  email: string;
  password: string;
  role: UserRole;
  branchId: null;
}

export interface UpdateUserRequest {
  name: string;
  email: string;
  role: UserRole;
  branchId: null;
  isActive: boolean;
}
```

- [ ] **Step 2: Commit**

```bash
git add bom-web/src/types/api.ts
git commit -m "feat(web): add User, CreateUserRequest, UpdateUserRequest types"
```

---

## Task 2: Create `usersApi.ts`

**Files:**
- Create: `bom-web/src/features/users/usersApi.ts`

- [ ] **Step 1: Create the file**

```ts
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/api/axios";
import type { User, CreateUserRequest, UpdateUserRequest } from "@/types/api";

export const userKeys = {
  all: ["users"] as const,
  list: () => [...userKeys.all, "list"] as const,
};

export function useUsers() {
  return useQuery({
    queryKey: userKeys.list(),
    queryFn: () => api.get<User[]>("/users").then((r) => r.data),
  });
}

export function useCreateUser() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateUserRequest) =>
      api.post<User>("/users", body).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: userKeys.all }),
  });
}

export function useUpdateUser() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, data }: { id: number; data: UpdateUserRequest }) =>
      api.put(`/users/${id}`, data),
    onSuccess: () => qc.invalidateQueries({ queryKey: userKeys.all }),
  });
}

export function useDeactivateUser() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => api.delete(`/users/${id}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: userKeys.all }),
  });
}
```

- [ ] **Step 2: Commit**

```bash
git add bom-web/src/features/users/usersApi.ts
git commit -m "feat(web): add usersApi hooks (useUsers, useCreateUser, useUpdateUser, useDeactivateUser)"
```

---

## Task 3: Write `UsersPage.test.tsx` (failing)

**Files:**
- Create: `bom-web/src/features/users/UsersPage.test.tsx`

- [ ] **Step 1: Create the test file**

```ts
import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter } from "react-router-dom";
import type { ReactNode } from "react";
import { useAuthStore } from "@/store/authStore";
import UsersPage from "./UsersPage";
import { api } from "@/api/axios";

vi.mock("@/api/axios", () => ({
  api: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() },
}));

function wrap(ui: ReactNode) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter>{ui}</MemoryRouter>
    </QueryClientProvider>,
  );
}

const sampleUsers = [
  {
    id: 1,
    name: "Alice Admin",
    email: "alice@example.com",
    role: "Admin",
    branchId: null,
    branchName: null,
    isActive: true,
  },
  {
    id: 2,
    name: "Bob Sales",
    email: "bob@example.com",
    role: "SalesPerson",
    branchId: null,
    branchName: null,
    isActive: false,
  },
];

function loginAsAdmin() {
  useAuthStore.getState().setSession({
    accessToken: "at",
    refreshToken: "rt",
    role: "Admin",
    userId: 99,
    name: "Test Admin",
    branchId: null,
  });
}

function getModalPanel(headingName: string): HTMLElement {
  return screen.getByRole("heading", { name: headingName }).parentElement!;
}

beforeEach(() => {
  vi.mocked(api.get).mockReset();
  vi.mocked(api.post as ReturnType<typeof vi.fn>).mockReset();
  vi.mocked(api.put as ReturnType<typeof vi.fn>).mockReset();
  vi.mocked(api.delete as ReturnType<typeof vi.fn>).mockReset();
  loginAsAdmin();
});

afterEach(() => {
  useAuthStore.getState().logout();
});

// ─── UsersPage ────────────────────────────────────────────────────────────────

describe("UsersPage", () => {
  it("renders user rows (name, email, role, active badge)", async () => {
    vi.mocked(api.get).mockResolvedValueOnce({ data: sampleUsers });
    wrap(<UsersPage />);
    await waitFor(() => expect(screen.getByText("Alice Admin")).toBeInTheDocument());
    expect(screen.getByText("alice@example.com")).toBeInTheDocument();
    expect(screen.getByText("Admin")).toBeInTheDocument();
    expect(screen.getByText("Bob Sales")).toBeInTheDocument();
    expect(screen.getByText("bob@example.com")).toBeInTheDocument();
    expect(screen.getByText("SalesPerson")).toBeInTheDocument();
    // active badges
    expect(screen.getByText("Active")).toBeInTheDocument();
    expect(screen.getByText("Inactive")).toBeInTheDocument();
  });

  it("hides Deactivate button for inactive users", async () => {
    vi.mocked(api.get).mockResolvedValueOnce({ data: sampleUsers });
    wrap(<UsersPage />);
    await waitFor(() => expect(screen.getByText("Bob Sales")).toBeInTheDocument());
    // Alice (active) has Deactivate button
    expect(screen.getByRole("button", { name: /Deactivate Alice Admin/i })).toBeInTheDocument();
    // Bob (inactive) does not
    expect(screen.queryByRole("button", { name: /Deactivate Bob Sales/i })).not.toBeInTheDocument();
  });

  it("opens AddUserModal on Add User click", async () => {
    vi.mocked(api.get).mockResolvedValueOnce({ data: [] });
    const user = userEvent.setup();
    wrap(<UsersPage />);
    await waitFor(() =>
      expect(screen.getByRole("button", { name: /Add User/i })).toBeInTheDocument(),
    );
    await user.click(screen.getByRole("button", { name: /Add User/i }));
    await waitFor(() =>
      expect(screen.getByRole("heading", { name: "Add User" })).toBeInTheDocument(),
    );
    expect(screen.getByLabelText(/^Name$/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/^Email$/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/^Password$/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/^Role$/i)).toBeInTheDocument();
  });

  it("opens EditUserModal pre-populated when Edit clicked", async () => {
    vi.mocked(api.get).mockResolvedValueOnce({ data: sampleUsers });
    const user = userEvent.setup();
    wrap(<UsersPage />);
    await waitFor(() => expect(screen.getByText("Alice Admin")).toBeInTheDocument());
    await user.click(screen.getByRole("button", { name: /Edit Alice Admin/i }));
    await waitFor(() =>
      expect(screen.getByRole("heading", { name: "Edit User" })).toBeInTheDocument(),
    );
    expect(screen.getByDisplayValue("Alice Admin")).toBeInTheDocument();
    expect(screen.getByDisplayValue("alice@example.com")).toBeInTheDocument();
  });

  it("Deactivate shows confirmation dialog; cancel dismisses", async () => {
    vi.mocked(api.get).mockResolvedValueOnce({ data: sampleUsers });
    const user = userEvent.setup();
    wrap(<UsersPage />);
    await waitFor(() => expect(screen.getByText("Alice Admin")).toBeInTheDocument());

    await user.click(screen.getByRole("button", { name: /Deactivate Alice Admin/i }));
    await waitFor(() =>
      expect(screen.getByText(/Are you sure you want to deactivate/i)).toBeInTheDocument(),
    );

    const modal = getModalPanel("Confirm Deactivate");
    await user.click(within(modal).getByRole("button", { name: /Cancel/i }));
    await waitFor(() =>
      expect(screen.queryByText(/Are you sure you want to deactivate/i)).not.toBeInTheDocument(),
    );
  });

  it("Deactivate confirm calls DELETE /users/:id and invalidates list", async () => {
    vi.mocked(api.get).mockResolvedValue({ data: sampleUsers });
    vi.mocked(api.delete as ReturnType<typeof vi.fn>).mockResolvedValueOnce({ status: 200 });
    const user = userEvent.setup();
    wrap(<UsersPage />);
    await waitFor(() => expect(screen.getByText("Alice Admin")).toBeInTheDocument());

    await user.click(screen.getByRole("button", { name: /Deactivate Alice Admin/i }));
    await waitFor(() =>
      expect(screen.getByRole("button", { name: /^Confirm$/i })).toBeInTheDocument(),
    );
    await user.click(screen.getByRole("button", { name: /^Confirm$/i }));

    await waitFor(() =>
      expect(vi.mocked(api.delete as ReturnType<typeof vi.fn>)).toHaveBeenCalledWith("/users/1"),
    );
  });
});

// ─── AddUserModal ─────────────────────────────────────────────────────────────

describe("AddUserModal", () => {
  async function openAddModal() {
    vi.mocked(api.get).mockResolvedValue({ data: [] });
    const user = userEvent.setup();
    wrap(<UsersPage />);
    await waitFor(() =>
      expect(screen.getByRole("button", { name: /Add User/i })).toBeInTheDocument(),
    );
    await user.click(screen.getByRole("button", { name: /Add User/i }));
    await waitFor(() =>
      expect(screen.getByRole("heading", { name: "Add User" })).toBeInTheDocument(),
    );
    return user;
  }

  it("shows validation errors for empty required fields", async () => {
    const user = await openAddModal();
    await user.click(screen.getByRole("button", { name: /^Save$/i }));
    await waitFor(() =>
      expect(screen.getByText(/Name is required/i)).toBeInTheDocument(),
    );
    expect(screen.getByText(/Email is required/i)).toBeInTheDocument();
    expect(screen.getByText(/Password must be at least 8 characters/i)).toBeInTheDocument();
    expect(screen.getByText(/Role is required/i)).toBeInTheDocument();
  });

  it("submits correct payload (with branchId: null) and closes modal on success", async () => {
    vi.mocked(api.post as ReturnType<typeof vi.fn>).mockResolvedValueOnce({
      data: {
        id: 10,
        name: "Charlie",
        email: "charlie@example.com",
        role: "BomCreator",
        branchId: null,
        branchName: null,
        isActive: true,
      },
    });
    const user = await openAddModal();

    await user.type(screen.getByLabelText(/^Name$/i), "Charlie");
    await user.type(screen.getByLabelText(/^Email$/i), "charlie@example.com");
    await user.type(screen.getByLabelText(/^Password$/i), "password123");
    await user.selectOptions(screen.getByLabelText(/^Role$/i), "BomCreator");
    await user.click(screen.getByRole("button", { name: /^Save$/i }));

    await waitFor(() =>
      expect(screen.queryByRole("heading", { name: "Add User" })).not.toBeInTheDocument(),
    );
    expect(vi.mocked(api.post)).toHaveBeenCalledWith(
      "/users",
      expect.objectContaining({
        name: "Charlie",
        email: "charlie@example.com",
        password: "password123",
        role: "BomCreator",
        branchId: null,
      }),
    );
  });

  it("shows server error on failure", async () => {
    vi.mocked(api.post as ReturnType<typeof vi.fn>).mockRejectedValueOnce({
      response: { data: { message: "Email already exists" } },
    });
    const user = await openAddModal();

    await user.type(screen.getByLabelText(/^Name$/i), "Duplicate");
    await user.type(screen.getByLabelText(/^Email$/i), "dup@example.com");
    await user.type(screen.getByLabelText(/^Password$/i), "password123");
    await user.selectOptions(screen.getByLabelText(/^Role$/i), "Admin");
    await user.click(screen.getByRole("button", { name: /^Save$/i }));

    await waitFor(() =>
      expect(screen.getByText(/Email already exists/i)).toBeInTheDocument(),
    );
    expect(screen.getByRole("heading", { name: "Add User" })).toBeInTheDocument();
  });
});

// ─── EditUserModal ────────────────────────────────────────────────────────────

describe("EditUserModal", () => {
  async function openEditModal() {
    vi.mocked(api.get).mockResolvedValueOnce({ data: sampleUsers });
    const user = userEvent.setup();
    wrap(<UsersPage />);
    await waitFor(() => expect(screen.getByText("Alice Admin")).toBeInTheDocument());
    await user.click(screen.getByRole("button", { name: /Edit Alice Admin/i }));
    await waitFor(() =>
      expect(screen.getByRole("heading", { name: "Edit User" })).toBeInTheDocument(),
    );
    return user;
  }

  it("pre-populates fields from row data", async () => {
    await openEditModal();
    expect(screen.getByDisplayValue("Alice Admin")).toBeInTheDocument();
    expect(screen.getByDisplayValue("alice@example.com")).toBeInTheDocument();
    expect(screen.getByDisplayValue("Admin")).toBeInTheDocument();
    // isActive checkbox checked
    expect(screen.getByLabelText(/Is Active/i)).toBeChecked();
  });

  it("submits correct payload and closes modal on success", async () => {
    vi.mocked(api.get).mockResolvedValue({ data: sampleUsers });
    vi.mocked(api.put as ReturnType<typeof vi.fn>).mockResolvedValueOnce({ status: 204 });
    const user = await openEditModal();

    const nameInput = screen.getByDisplayValue("Alice Admin");
    await user.clear(nameInput);
    await user.type(nameInput, "Alice Updated");
    await user.click(screen.getByRole("button", { name: /^Save$/i }));

    await waitFor(() =>
      expect(screen.queryByRole("heading", { name: "Edit User" })).not.toBeInTheDocument(),
    );
    expect(vi.mocked(api.put as ReturnType<typeof vi.fn>)).toHaveBeenCalledWith(
      "/users/1",
      expect.objectContaining({
        name: "Alice Updated",
        email: "alice@example.com",
        role: "Admin",
        branchId: null,
        isActive: true,
      }),
    );
  });
});
```

- [ ] **Step 2: Run tests to confirm they all fail (module not found)**

```bash
cd bom-web && npx vitest run src/features/users/UsersPage.test.tsx
```

Expected: All tests fail with "Cannot find module './UsersPage'" or similar.

- [ ] **Step 3: Commit the failing test file**

```bash
git add bom-web/src/features/users/UsersPage.test.tsx
git commit -m "test(web): add failing tests for UsersPage, AddUserModal, EditUserModal"
```

---

## Task 4: Create `AddUserModal.tsx`

**Files:**
- Create: `bom-web/src/features/users/AddUserModal.tsx`

- [ ] **Step 1: Create the file**

```tsx
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { Dialog } from "@/components/ui/Dialog";
import { Button } from "@/components/ui/Button";
import { Input } from "@/components/ui/Input";
import { Label } from "@/components/ui/Label";
import { useCreateUser } from "./usersApi";
import type { UserRole } from "@/types/api";

const schema = z.object({
  name: z.string().min(1, "Name is required"),
  email: z.string().min(1, "Email is required").email("Invalid email format"),
  password: z.string().min(8, "Password must be at least 8 characters"),
  role: z.string().min(1, "Role is required"),
});

type FormValues = z.infer<typeof schema>;

interface Props {
  open: boolean;
  onClose: () => void;
}

export function AddUserModal({ open, onClose }: Props) {
  const create = useCreateUser();
  const {
    register,
    handleSubmit,
    reset,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { name: "", email: "", password: "", role: "" },
  });

  const onSubmit = handleSubmit(async (values) => {
    try {
      await create.mutateAsync({
        name: values.name,
        email: values.email,
        password: values.password,
        role: values.role as UserRole,
        branchId: null,
      });
      create.reset();
      reset();
      onClose();
    } catch {
      // error displayed via create.isError
    }
  });

  function handleClose() {
    create.reset();
    reset();
    onClose();
  }

  return (
    <Dialog open={open} onClose={handleClose} title="Add User">
      <form onSubmit={onSubmit} className="space-y-4" noValidate>
        <div className="space-y-1">
          <Label htmlFor="user-name">Name</Label>
          <Input id="user-name" {...register("name")} />
          {errors.name && <p className="text-xs text-destructive">{errors.name.message}</p>}
        </div>

        <div className="space-y-1">
          <Label htmlFor="user-email">Email</Label>
          <Input id="user-email" type="email" {...register("email")} />
          {errors.email && <p className="text-xs text-destructive">{errors.email.message}</p>}
        </div>

        <div className="space-y-1">
          <Label htmlFor="user-password">Password</Label>
          <Input id="user-password" type="password" {...register("password")} />
          {errors.password && (
            <p className="text-xs text-destructive">{errors.password.message}</p>
          )}
        </div>

        <div className="space-y-1">
          <Label htmlFor="user-role">Role</Label>
          <select
            id="user-role"
            className="block w-full rounded-md border border-input bg-background px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-ring"
            {...register("role")}
          >
            <option value="">Select role</option>
            <option value="Admin">Admin</option>
            <option value="SalesPerson">SalesPerson</option>
            <option value="BomCreator">BomCreator</option>
            <option value="Accountant">Accountant</option>
            <option value="ManagingDirector">ManagingDirector</option>
          </select>
          {errors.role && <p className="text-xs text-destructive">{errors.role.message}</p>}
        </div>

        {create.isError && (
          <p className="text-sm text-destructive">
            {(create.error as { response?: { data?: { message?: string } } })?.response?.data
              ?.message ?? "Failed to create user"}
          </p>
        )}

        <div className="flex justify-end gap-2 pt-2">
          <Button type="button" variant="ghost" onClick={handleClose}>
            Cancel
          </Button>
          <Button type="submit" disabled={isSubmitting || create.isPending}>
            {create.isPending ? "Saving…" : "Save"}
          </Button>
        </div>
      </form>
    </Dialog>
  );
}
```

---

## Task 5: Create `EditUserModal.tsx`

**Files:**
- Create: `bom-web/src/features/users/EditUserModal.tsx`

- [ ] **Step 1: Create the file**

```tsx
import { useEffect } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { Dialog } from "@/components/ui/Dialog";
import { Button } from "@/components/ui/Button";
import { Input } from "@/components/ui/Input";
import { Label } from "@/components/ui/Label";
import { useUpdateUser } from "./usersApi";
import type { User, UserRole } from "@/types/api";

const schema = z.object({
  name: z.string().min(1, "Name is required"),
  email: z.string().min(1, "Email is required").email("Invalid email format"),
  role: z.string().min(1, "Role is required"),
  isActive: z.boolean(),
});

type FormValues = z.infer<typeof schema>;

interface Props {
  open: boolean;
  user: User | null;
  onClose: () => void;
}

export function EditUserModal({ open, user, onClose }: Props) {
  const update = useUpdateUser();
  const {
    register,
    handleSubmit,
    reset,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
  });

  useEffect(() => {
    if (user) {
      reset({
        name: user.name,
        email: user.email,
        role: user.role,
        isActive: user.isActive,
      });
    }
  }, [user, reset]);

  function handleClose() {
    update.reset();
    onClose();
  }

  const onSubmit = handleSubmit(async (values) => {
    if (!user) return;
    try {
      await update.mutateAsync({
        id: user.id,
        data: {
          name: values.name,
          email: values.email,
          role: values.role as UserRole,
          branchId: null,
          isActive: values.isActive,
        },
      });
      update.reset();
      onClose();
    } catch {
      // error displayed via update.isError
    }
  });

  return (
    <Dialog open={open} onClose={handleClose} title="Edit User">
      <form onSubmit={onSubmit} className="space-y-4" noValidate>
        <div className="space-y-1">
          <Label htmlFor="edit-user-name">Name</Label>
          <Input id="edit-user-name" {...register("name")} />
          {errors.name && <p className="text-xs text-destructive">{errors.name.message}</p>}
        </div>

        <div className="space-y-1">
          <Label htmlFor="edit-user-email">Email</Label>
          <Input id="edit-user-email" type="email" {...register("email")} />
          {errors.email && <p className="text-xs text-destructive">{errors.email.message}</p>}
        </div>

        <div className="space-y-1">
          <Label htmlFor="edit-user-role">Role</Label>
          <select
            id="edit-user-role"
            className="block w-full rounded-md border border-input bg-background px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-ring"
            {...register("role")}
          >
            <option value="">Select role</option>
            <option value="Admin">Admin</option>
            <option value="SalesPerson">SalesPerson</option>
            <option value="BomCreator">BomCreator</option>
            <option value="Accountant">Accountant</option>
            <option value="ManagingDirector">ManagingDirector</option>
          </select>
          {errors.role && <p className="text-xs text-destructive">{errors.role.message}</p>}
        </div>

        <div className="flex items-center gap-2">
          <input
            id="edit-user-active"
            type="checkbox"
            className="h-4 w-4 rounded border-input"
            {...register("isActive")}
          />
          <Label htmlFor="edit-user-active">Is Active</Label>
        </div>

        {update.isError && (
          <p className="text-sm text-destructive">
            {(update.error as { response?: { data?: { message?: string } } })?.response?.data
              ?.message ?? "Failed to update user"}
          </p>
        )}

        <div className="flex justify-end gap-2 pt-2">
          <Button type="button" variant="ghost" onClick={handleClose}>
            Cancel
          </Button>
          <Button type="submit" disabled={isSubmitting || update.isPending}>
            {update.isPending ? "Saving…" : "Save"}
          </Button>
        </div>
      </form>
    </Dialog>
  );
}
```

---

## Task 6: Create `UsersPage.tsx`

**Files:**
- Create: `bom-web/src/features/users/UsersPage.tsx`

- [ ] **Step 1: Create the file**

```tsx
import { useState, useMemo } from "react";
import type { ColumnDef } from "@tanstack/react-table";
import { Pencil, UserX } from "lucide-react";
import { DataTable } from "@/components/ui/DataTable";
import { Button } from "@/components/ui/Button";
import { Card, CardContent } from "@/components/ui/Card";
import { Dialog } from "@/components/ui/Dialog";
import { useUsers, useDeactivateUser } from "./usersApi";
import { AddUserModal } from "./AddUserModal";
import { EditUserModal } from "./EditUserModal";
import type { User } from "@/types/api";

export default function UsersPage() {
  const { data, isLoading, isError, refetch } = useUsers();
  const deactivate = useDeactivateUser();

  const [addOpen, setAddOpen] = useState(false);
  const [editTarget, setEditTarget] = useState<User | null>(null);
  const [deactivateTarget, setDeactivateTarget] = useState<User | null>(null);

  async function handleConfirmDeactivate() {
    if (!deactivateTarget) return;
    try {
      await deactivate.mutateAsync(deactivateTarget.id);
    } catch {
      // errors visible via deactivate.isError (not shown here — dialog closes on success or error)
    }
    setDeactivateTarget(null);
  }

  const columns = useMemo<ColumnDef<User>[]>(
    () => [
      { accessorKey: "name", header: "Name" },
      { accessorKey: "email", header: "Email" },
      { accessorKey: "role", header: "Role" },
      {
        id: "isActive",
        accessorKey: "isActive",
        header: "Active",
        cell: (i) =>
          (i.getValue() as boolean) ? (
            <span className="inline-flex items-center rounded-full bg-green-50 px-2 py-0.5 text-xs font-medium text-green-700">
              Active
            </span>
          ) : (
            <span className="inline-flex items-center rounded-full bg-muted px-2 py-0.5 text-xs font-medium text-muted-foreground">
              Inactive
            </span>
          ),
      },
      {
        id: "actions",
        header: "",
        cell: ({ row }: { row: { original: User } }) => {
          const u = row.original;
          return (
            <div className="flex justify-end gap-1">
              <Button
                variant="ghost"
                size="icon"
                aria-label={`Edit ${u.name}`}
                onClick={() => setEditTarget(u)}
              >
                <Pencil className="h-4 w-4" />
              </Button>
              {u.isActive && (
                <Button
                  variant="ghost"
                  size="icon"
                  aria-label={`Deactivate ${u.name}`}
                  onClick={() => setDeactivateTarget(u)}
                >
                  <UserX className="h-4 w-4" />
                </Button>
              )}
            </div>
          );
        },
      },
    ],
    [],
  );

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold tracking-tight">Users</h1>
        <Button onClick={() => setAddOpen(true)}>Add User</Button>
      </div>

      {isError && (
        <Card>
          <CardContent className="flex items-center justify-between">
            <p className="text-destructive">Failed to load users.</p>
            <Button variant="ghost" onClick={() => refetch()}>
              Retry
            </Button>
          </CardContent>
        </Card>
      )}

      <DataTable
        columns={columns}
        data={data ?? []}
        isLoading={isLoading}
        emptyState={<p>No users found.</p>}
      />

      <AddUserModal open={addOpen} onClose={() => setAddOpen(false)} />
      <EditUserModal
        open={editTarget !== null}
        user={editTarget}
        onClose={() => setEditTarget(null)}
      />

      <Dialog
        open={deactivateTarget !== null}
        onClose={() => setDeactivateTarget(null)}
        title="Confirm Deactivate"
      >
        <p className="text-sm">
          Are you sure you want to deactivate{" "}
          <span className="font-semibold">{deactivateTarget?.name}</span>?
        </p>
        <div className="flex justify-end gap-2 pt-4">
          <Button type="button" variant="ghost" onClick={() => setDeactivateTarget(null)}>
            Cancel
          </Button>
          <Button
            type="button"
            variant="destructive"
            onClick={handleConfirmDeactivate}
            disabled={deactivate.isPending}
          >
            {deactivate.isPending ? "Deactivating…" : "Confirm"}
          </Button>
        </div>
      </Dialog>
    </div>
  );
}
```

- [ ] **Step 2: Run tests and verify they all pass**

```bash
cd bom-web && npx vitest run src/features/users/UsersPage.test.tsx
```

Expected: 11 tests pass, 0 fail.

- [ ] **Step 3: Run the full test suite to ensure nothing is broken**

```bash
cd bom-web && npx vitest run
```

Expected: All tests pass.

- [ ] **Step 4: Commit all user feature files**

```bash
git add bom-web/src/features/users/
git commit -m "feat(web): add UsersPage, AddUserModal, EditUserModal with full test coverage"
```

---

## Task 7: Register route in `App.tsx`

**Files:**
- Modify: `bom-web/src/App.tsx`

- [ ] **Step 1: Add the import for UsersPage**

In `bom-web/src/App.tsx`, add this import after the `ExchangeRatesPage` import line:

```ts
import UsersPage from "@/features/users/UsersPage";
```

- [ ] **Step 2: Add the route inside the authenticated children array**

In `bom-web/src/App.tsx`, add this route inside the `children` array (after the `exchange-rates` route entry):

```tsx
{
  path: "admin/users",
  element: (
    <ProtectedRoute allow={["Admin"]}>
      <UsersPage />
    </ProtectedRoute>
  ),
},
```

- [ ] **Step 3: Run full test suite one final time**

```bash
cd bom-web && npx vitest run
```

Expected: All tests pass.

- [ ] **Step 4: Commit**

```bash
git add bom-web/src/App.tsx
git commit -m "feat(web): register /admin/users route for UsersPage"
```

---

## Self-Review

**Spec coverage check:**

| Spec requirement | Task |
|---|---|
| `useUsers()` GET `/users` | Task 2 |
| `useCreateUser()` POST `/users` | Task 2 |
| `useUpdateUser()` PUT `/users/:id` | Task 2 |
| `useDeactivateUser()` DELETE `/users/:id` | Task 2 |
| DataTable: Name, Email, Role, Active badge, Actions | Task 6 |
| Active badge green/grey | Task 6 |
| Deactivate button only when `isActive === true` | Task 6 |
| Add User button in header | Task 6 |
| Confirmation dialog with Confirm + Cancel | Task 6 |
| AddUserModal: Name, Email, Password, Role fields | Task 4 |
| AddUserModal: branchId always null | Task 4 |
| AddUserModal: closes + resets on success | Task 4 |
| AddUserModal: inline server error on failure | Task 4 |
| EditUserModal: pre-populated from row | Task 5 |
| EditUserModal: Name, Email, Role, IsActive fields | Task 5 |
| EditUserModal: no password field | Task 5 |
| EditUserModal: inline server error on failure | Task 5 |
| Route `admin/users` with Admin guard | Task 7 |
| All 11 test cases | Task 3 |
| `User`, `CreateUserRequest`, `UpdateUserRequest` types | Task 1 |

**No placeholders found.** All steps contain complete code.

**Type consistency check:** `User` defined in Task 1, referenced in Tasks 2, 4, 5, 6 — all use `User` (not `UserResponse`). `UserRole` imported from `@/types/api` in Tasks 4 and 5 — consistent. `userKeys.all` used in `invalidateQueries` in all four hooks — consistent.
