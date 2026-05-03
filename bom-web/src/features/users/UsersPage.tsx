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
import { ResetPasswordModal } from "./ResetPasswordModal";
import { useUserBranches } from "@/api/userBranches";
import { useBranches } from "@/api/branches";
import { SalesGroupCell } from "@/components/SalesGroupCell";
import { useAuthStore } from "@/store/authStore";
import type { User } from "@/types/api";

// ─── AccountantBranchCell ─────────────────────────────────────────────────────
// Renders the comma-separated branch names for an Accountant row.
// Uses per-row query (N+1 acceptable for small user lists).

interface AccountantBranchCellProps {
  userId: number;
}

function AccountantBranchCell({ userId }: AccountantBranchCellProps) {
  const { data: branchIds = [], isLoading } = useUserBranches(userId);
  const { data: allBranches = [] } = useBranches();

  if (isLoading) return <span className="text-muted-foreground text-xs">…</span>;
  if (branchIds.length === 0) return <span className="text-muted-foreground">—</span>;

  const names = branchIds
    .map((id) => allBranches.find((b) => b.id === id)?.name ?? String(id))
    .join(", ");

  return <span className="text-sm">{names}</span>;
}

export default function UsersPage() {
  const { data, isLoading, isError, refetch } = useUsers();
  const deactivate = useDeactivateUser();
  const currentUserRole = useAuthStore((s) => s.user?.role);

  const [addOpen, setAddOpen] = useState(false);
  const [editTarget, setEditTarget] = useState<User | null>(null);
  const [deactivateTarget, setDeactivateTarget] = useState<User | null>(null);
  const [resetTarget, setResetTarget] = useState<User | null>(null);

  async function handleConfirmDeactivate() {
    if (!deactivateTarget) return;
    try {
      await deactivate.mutateAsync(deactivateTarget.id);
      deactivate.reset();
      setDeactivateTarget(null);
    } catch {
      // error displayed via deactivate.isError
    }
  }

  const { data: allBranches = [] } = useBranches();

  const columns = useMemo<ColumnDef<User>[]>(
    () => [
      { accessorKey: "name", header: "Name" },
      { accessorKey: "email", header: "Email" },
      { accessorKey: "role", header: "Role" },
      {
        id: "branch",
        header: "Branch",
        cell: ({ row }: { row: { original: User } }) => {
          const u = row.original;
          if (u.role === "Accountant") {
            return <AccountantBranchCell userId={u.id} />;
          }
          if (u.branchId === null) return <span className="text-muted-foreground">—</span>;
          const name = allBranches.find((b) => b.id === u.branchId)?.name ?? u.branchName;
          return <span className="text-sm">{name ?? String(u.branchId)}</span>;
        },
      },
      {
        id: "group",
        header: "Group",
        cell: ({ row }: { row: { original: User } }) => {
          const u = row.original;
          return <SalesGroupCell userId={u.id} role={u.role} />;
        },
      },
      {
        accessorKey: "isActive",
        header: "Status",
        cell: (i) =>
          (i.getValue() as boolean) ? (
            <span className="inline-flex items-center rounded-full bg-green-50 px-2 py-0.5 text-xs font-medium text-green-700 dark:bg-emerald-900/30 dark:text-emerald-300">
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
              {currentUserRole === "Admin" && (
                <Button
                  variant="outline"
                  size="sm"
                  aria-label={`Reset password ${u.name}`}
                  onClick={() => setResetTarget(u)}
                >
                  Reset password
                </Button>
              )}
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
    [allBranches, currentUserRole],
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
      {resetTarget && (
        <ResetPasswordModal user={resetTarget} onClose={() => setResetTarget(null)} />
      )}

      <Dialog
        open={deactivateTarget !== null}
        onClose={() => { deactivate.reset(); setDeactivateTarget(null); }}
        title="Confirm Deactivate"
      >
        <p className="text-sm">
          Are you sure you want to deactivate{" "}
          <span className="font-semibold">{deactivateTarget?.name}</span>?
        </p>

        {deactivate.isError && (
          <p className="text-sm text-destructive">
            {(deactivate.error as { response?: { data?: { message?: string } } })?.response?.data
              ?.message ?? "Failed to deactivate user"}
          </p>
        )}

        <div className="flex justify-end gap-2 pt-4">
          <Button type="button" variant="ghost" onClick={() => { deactivate.reset(); setDeactivateTarget(null); }}>
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
