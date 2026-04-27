import { useState } from "react";
import { Dialog } from "@/components/ui/Dialog";
import { Button } from "@/components/ui/Button";
import { useBranches } from "@/api/branches";
import { useChangeBranch } from "./requisitionsApi";

interface Props {
  requisitionId: number;
  currentBranchId: number;
  open: boolean;
  onClose: () => void;
}

export function BranchSwapModal({ requisitionId, currentBranchId, open, onClose }: Props) {
  const { data: branches } = useBranches();
  const changeMut = useChangeBranch(requisitionId);
  const [pickedId, setPickedId] = useState<number | null>(null);
  const [reason, setReason] = useState("");
  const [error, setError] = useState<string | null>(null);

  const candidateBranches = (branches ?? []).filter((b) => (b.isActive ?? true) && b.id !== currentBranchId);
  const canSave = pickedId !== null && pickedId !== currentBranchId && !changeMut.isPending;

  async function handleSave() {
    if (pickedId === null) return;
    setError(null);
    try {
      await changeMut.mutateAsync({ branchId: pickedId, reason: reason.trim() || undefined });
      setPickedId(null);
      setReason("");
      onClose();
    } catch (e: unknown) {
      const msg =
        (e as { response?: { data?: { message?: string } } })?.response?.data?.message ??
        (e instanceof Error ? e.message : "Branch change failed");
      setError(msg);
    }
  }

  return (
    <Dialog open={open} onClose={onClose} title="Change branch">
      <div className="space-y-4">
        <div className="space-y-1">
          <label htmlFor="new-branch" className="text-sm font-medium">
            New branch
          </label>
          <select
            id="new-branch"
            className="w-full rounded-md border px-3 py-2 text-sm"
            value={pickedId ?? ""}
            onChange={(e) => setPickedId(Number(e.target.value) || null)}
          >
            <option value="" disabled>
              Select new branch
            </option>
            {candidateBranches.map((b) => (
              <option key={b.id} value={b.id}>
                {b.name}
              </option>
            ))}
          </select>
        </div>

        <div className="space-y-1">
          <label htmlFor="branch-reason" className="text-sm font-medium">
            Reason (optional)
          </label>
          <textarea
            id="branch-reason"
            rows={3}
            maxLength={500}
            className="w-full rounded-md border px-3 py-2 text-sm"
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            placeholder="Why is this changing? (visible in audit history)"
          />
        </div>

        {error && (
          <div className="rounded border border-red-200 bg-red-50 p-2 text-sm text-red-700">
            {error}
          </div>
        )}

        <div className="flex justify-end gap-2">
          <Button variant="ghost" onClick={onClose}>
            Cancel
          </Button>
          <Button onClick={handleSave} disabled={!canSave}>
            {changeMut.isPending ? "Saving…" : "Save"}
          </Button>
        </div>
      </div>
    </Dialog>
  );
}
