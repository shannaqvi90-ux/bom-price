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
  useBranches,
  useCreateBranch,
  useUpdateBranch,
  useDeleteBranch,
  type Branch,
} from "@/api/branches";

// ─── Add Branch Modal ─────────────────────────────────────────────────────────

interface AddBranchModalProps {
  open: boolean;
  onClose: () => void;
}

function AddBranchModal({ open, onClose }: AddBranchModalProps) {
  const create = useCreateBranch();
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
      setNameError("Branch name is required.");
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
    <Dialog open={open} onClose={handleClose} title="Add Branch">
      <form onSubmit={handleSubmit} className="space-y-4" noValidate>
        <div className="space-y-1">
          <Label htmlFor="add-branch-name">Branch Name</Label>
          <Input
            id="add-branch-name"
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="e.g. Dubai Main"
          />
          {nameError && <p className="text-xs text-destructive">{nameError}</p>}
        </div>

        {create.isError && (
          <p className="text-sm text-destructive">
            {(create.error as { response?: { data?: { message?: string } } })?.response?.data
              ?.message ?? "Failed to create branch"}
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

// ─── Edit Branch Modal ────────────────────────────────────────────────────────

interface EditBranchModalProps {
  open: boolean;
  branch: Branch | null;
  onClose: () => void;
}

function EditBranchModal({ open, branch, onClose }: EditBranchModalProps) {
  const update = useUpdateBranch();
  const [name, setName] = useState(branch?.name ?? "");
  const [isActive, setIsActive] = useState(branch?.isActive ?? true);
  const [nameError, setNameError] = useState("");

  // Sync local state when branch changes
  useState(() => {
    setName(branch?.name ?? "");
    setIsActive(branch?.isActive ?? true);
    setNameError("");
  });

  // Keep form in sync with incoming branch prop
  const [prevBranch, setPrevBranch] = useState(branch);
  if (branch !== prevBranch) {
    setPrevBranch(branch);
    setName(branch?.name ?? "");
    setIsActive(branch?.isActive ?? true);
    setNameError("");
  }

  function handleClose() {
    update.reset();
    onClose();
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!name.trim()) {
      setNameError("Branch name is required.");
      return;
    }
    if (!branch) return;
    setNameError("");
    try {
      await update.mutateAsync({ id: branch.id, name: name.trim(), isActive });
      handleClose();
    } catch {
      // error displayed via update.isError
    }
  }

  return (
    <Dialog open={open} onClose={handleClose} title="Edit Branch">
      <form onSubmit={handleSubmit} className="space-y-4" noValidate>
        <div className="space-y-1">
          <Label htmlFor="edit-branch-name">Branch Name</Label>
          <Input
            id="edit-branch-name"
            value={name}
            onChange={(e) => setName(e.target.value)}
          />
          {nameError && <p className="text-xs text-destructive">{nameError}</p>}
        </div>

        <div className="flex items-center gap-2">
          <input
            id="edit-branch-active"
            type="checkbox"
            className="h-4 w-4 rounded border-input"
            checked={isActive}
            onChange={(e) => setIsActive(e.target.checked)}
          />
          <Label htmlFor="edit-branch-active">Is Active</Label>
        </div>

        {update.isError && (
          <p className="text-sm text-destructive">
            {(update.error as { response?: { data?: { message?: string } } })?.response?.data
              ?.message ?? "Failed to update branch"}
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

// ─── BranchesPage ─────────────────────────────────────────────────────────────

export default function BranchesPage() {
  const { data, isLoading, isError, refetch } = useBranches();
  const deleteBranch = useDeleteBranch();

  const [addOpen, setAddOpen] = useState(false);
  const [editTarget, setEditTarget] = useState<Branch | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<Branch | null>(null);

  async function handleConfirmDelete() {
    if (!deleteTarget) return;
    try {
      await deleteBranch.mutateAsync(deleteTarget.id);
      deleteBranch.reset();
      setDeleteTarget(null);
    } catch {
      // error displayed via deleteBranch.isError
    }
  }

  const columns = useMemo<ColumnDef<Branch>[]>(
    () => [
      { accessorKey: "name", header: "Name" },
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
        cell: ({ row }: { row: { original: Branch } }) => {
          const b = row.original;
          return (
            <div className="flex justify-end gap-1">
              <Button
                variant="ghost"
                size="icon"
                aria-label={`Edit ${b.name}`}
                onClick={() => setEditTarget(b)}
              >
                <Pencil className="h-4 w-4" />
              </Button>
              <Button
                variant="ghost"
                size="icon"
                aria-label={`Delete ${b.name}`}
                onClick={() => setDeleteTarget(b)}
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
        <h1 className="text-2xl font-semibold tracking-tight">Branches</h1>
        <Button onClick={() => setAddOpen(true)}>Add Branch</Button>
      </div>

      {isError && (
        <Card>
          <CardContent className="flex items-center justify-between">
            <p className="text-destructive">Failed to load branches.</p>
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
        emptyState={<p>No branches found.</p>}
      />

      <AddBranchModal open={addOpen} onClose={() => setAddOpen(false)} />
      <EditBranchModal
        open={editTarget !== null}
        branch={editTarget}
        onClose={() => setEditTarget(null)}
      />

      <Dialog
        open={deleteTarget !== null}
        onClose={() => { deleteBranch.reset(); setDeleteTarget(null); }}
        title="Confirm Delete"
      >
        <p className="text-sm">
          Are you sure you want to delete{" "}
          <span className="font-semibold">{deleteTarget?.name}</span>? This will
          mark the branch as inactive if it is not in use.
        </p>

        {deleteBranch.isError && (
          <p className="text-sm text-destructive">
            {(deleteBranch.error as { response?: { data?: { message?: string } } })?.response?.data
              ?.message ?? "Failed to delete branch"}
          </p>
        )}

        <div className="flex justify-end gap-2 pt-4">
          <Button
            type="button"
            variant="ghost"
            onClick={() => { deleteBranch.reset(); setDeleteTarget(null); }}
          >
            Cancel
          </Button>
          <Button
            type="button"
            variant="destructive"
            onClick={handleConfirmDelete}
            disabled={deleteBranch.isPending}
          >
            {deleteBranch.isPending ? "Deleting…" : "Confirm"}
          </Button>
        </div>
      </Dialog>
    </div>
  );
}
