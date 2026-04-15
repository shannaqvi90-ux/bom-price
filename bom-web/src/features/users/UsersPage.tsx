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
      // errors visible via deactivate.isError
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
        header: "Status",
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
