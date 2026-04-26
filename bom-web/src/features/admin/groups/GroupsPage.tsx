import { useState, useMemo } from "react";
import type { ColumnDef } from "@tanstack/react-table";
import { Pencil, Trash2 } from "lucide-react";
import { DataTable } from "@/components/ui/DataTable";
import { Button } from "@/components/ui/Button";
import { Card, CardContent } from "@/components/ui/Card";
import { Dialog } from "@/components/ui/Dialog";
import { Input } from "@/components/ui/Input";
import { Label } from "@/components/ui/Label";
import {
  useGroups,
  useCreateGroup,
  useUpdateGroup,
  useDeleteGroup,
  type SalesGroup,
} from "@/api/groups";

// ─── Add Group Modal ──────────────────────────────────────────────────────────

interface AddGroupModalProps {
  open: boolean;
  onClose: () => void;
}

function AddGroupModal({ open, onClose }: AddGroupModalProps) {
  const create = useCreateGroup();
  const [name, setName] = useState("");
  const [nameError, setNameError] = useState("");

  function handleClose() {
    create.reset();
    setName("");
    setNameError("");
    onClose();
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!name.trim()) {
      setNameError("Group name is required.");
      return;
    }
    setNameError("");
    try {
      await create.mutateAsync({ name: name.trim() });
      handleClose();
    } catch {
      // error displayed via create.isError
    }
  }

  return (
    <Dialog open={open} onClose={handleClose} title="Add Group">
      <form onSubmit={handleSubmit} className="space-y-4" noValidate>
        <div className="space-y-1">
          <Label htmlFor="add-group-name">Group Name</Label>
          <Input
            id="add-group-name"
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="e.g. North Sales Team"
          />
          {nameError && <p className="text-xs text-destructive">{nameError}</p>}
        </div>

        {create.isError && (
          <p className="text-sm text-destructive">
            {(create.error as { response?: { data?: { message?: string } } })?.response?.data
              ?.message ?? "Failed to create group"}
          </p>
        )}

        <div className="flex justify-end gap-2 pt-2">
          <Button type="button" variant="ghost" onClick={handleClose}>
            Cancel
          </Button>
          <Button type="submit" disabled={create.isPending}>
            {create.isPending ? "Saving…" : "Save"}
          </Button>
        </div>
      </form>
    </Dialog>
  );
}

// ─── Edit Group Modal ─────────────────────────────────────────────────────────

interface EditGroupModalProps {
  open: boolean;
  group: SalesGroup | null;
  onClose: () => void;
}

function EditGroupModal({ open, group, onClose }: EditGroupModalProps) {
  const update = useUpdateGroup();
  const [name, setName] = useState(group?.name ?? "");
  const [isActive, setIsActive] = useState(group?.isActive ?? true);
  const [nameError, setNameError] = useState("");

  // Sync local state when group changes
  useState(() => {
    setName(group?.name ?? "");
    setIsActive(group?.isActive ?? true);
    setNameError("");
  });

  // Keep form in sync with incoming group prop
  const [prevGroup, setPrevGroup] = useState(group);
  if (group !== prevGroup) {
    setPrevGroup(group);
    setName(group?.name ?? "");
    setIsActive(group?.isActive ?? true);
    setNameError("");
  }

  function handleClose() {
    update.reset();
    onClose();
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!name.trim()) {
      setNameError("Group name is required.");
      return;
    }
    if (!group) return;
    setNameError("");
    try {
      await update.mutateAsync({ id: group.id, name: name.trim(), isActive });
      handleClose();
    } catch {
      // error displayed via update.isError
    }
  }

  return (
    <Dialog open={open} onClose={handleClose} title="Edit Group">
      <form onSubmit={handleSubmit} className="space-y-4" noValidate>
        <div className="space-y-1">
          <Label htmlFor="edit-group-name">Group Name</Label>
          <Input
            id="edit-group-name"
            value={name}
            onChange={(e) => setName(e.target.value)}
          />
          {nameError && <p className="text-xs text-destructive">{nameError}</p>}
        </div>

        <div className="flex items-center gap-2">
          <input
            id="edit-group-active"
            type="checkbox"
            className="h-4 w-4 rounded border-input"
            checked={isActive}
            onChange={(e) => setIsActive(e.target.checked)}
          />
          <Label htmlFor="edit-group-active">Is Active</Label>
        </div>

        {update.isError && (
          <p className="text-sm text-destructive">
            {(update.error as { response?: { data?: { message?: string } } })?.response?.data
              ?.message ?? "Failed to update group"}
          </p>
        )}

        <div className="flex justify-end gap-2 pt-2">
          <Button type="button" variant="ghost" onClick={handleClose}>
            Cancel
          </Button>
          <Button type="submit" disabled={update.isPending}>
            {update.isPending ? "Saving…" : "Save"}
          </Button>
        </div>
      </form>
    </Dialog>
  );
}

// ─── GroupsPage ───────────────────────────────────────────────────────────────

export default function GroupsPage() {
  const { data, isLoading, isError, refetch } = useGroups();
  const deleteGroup = useDeleteGroup();

  const [addOpen, setAddOpen] = useState(false);
  const [editTarget, setEditTarget] = useState<SalesGroup | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<SalesGroup | null>(null);

  async function handleConfirmDelete() {
    if (!deleteTarget) return;
    try {
      await deleteGroup.mutateAsync(deleteTarget.id);
      deleteGroup.reset();
      setDeleteTarget(null);
    } catch {
      // error displayed via deleteGroup.isError
    }
  }

  const columns = useMemo<ColumnDef<SalesGroup>[]>(
    () => [
      { accessorKey: "name", header: "Name" },
      {
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
        cell: ({ row }: { row: { original: SalesGroup } }) => {
          const g = row.original;
          return (
            <div className="flex justify-end gap-1">
              <Button
                variant="ghost"
                size="icon"
                aria-label={`Edit ${g.name}`}
                onClick={() => setEditTarget(g)}
              >
                <Pencil className="h-4 w-4" />
              </Button>
              <Button
                variant="ghost"
                size="icon"
                aria-label={`Delete ${g.name}`}
                onClick={() => setDeleteTarget(g)}
              >
                <Trash2 className="h-4 w-4" />
              </Button>
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
        <h1 className="text-2xl font-semibold tracking-tight">Groups</h1>
        <Button onClick={() => setAddOpen(true)}>Add Group</Button>
      </div>

      {isError && (
        <Card>
          <CardContent className="flex items-center justify-between">
            <p className="text-destructive">Failed to load groups.</p>
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
        emptyState={<p>No groups found.</p>}
      />

      <AddGroupModal open={addOpen} onClose={() => setAddOpen(false)} />
      <EditGroupModal
        open={editTarget !== null}
        group={editTarget}
        onClose={() => setEditTarget(null)}
      />

      <Dialog
        open={deleteTarget !== null}
        onClose={() => { deleteGroup.reset(); setDeleteTarget(null); }}
        title="Confirm Delete"
      >
        <p className="text-sm">
          Are you sure you want to delete{" "}
          <span className="font-semibold">{deleteTarget?.name}</span>? This will
          mark the group as inactive if it is not in use.
        </p>

        {deleteGroup.isError && (
          <p className="text-sm text-destructive">
            {(deleteGroup.error as { response?: { data?: { message?: string } } })?.response?.data
              ?.message ?? "Failed to delete group"}
          </p>
        )}

        <div className="flex justify-end gap-2 pt-4">
          <Button
            type="button"
            variant="ghost"
            onClick={() => { deleteGroup.reset(); setDeleteTarget(null); }}
          >
            Cancel
          </Button>
          <Button
            type="button"
            variant="destructive"
            onClick={handleConfirmDelete}
            disabled={deleteGroup.isPending}
          >
            {deleteGroup.isPending ? "Deleting…" : "Confirm"}
          </Button>
        </div>
      </Dialog>
    </div>
  );
}
