import { useState } from "react";
import { useDeleteRequisition } from "@/api/admin";
import type { RequisitionStatus } from "@/types/api";
import { Button } from "@/components/ui/Button";
import { Dialog } from "@/components/ui/Dialog";

interface Props {
  requisition: { id: number; refNo: string; status: RequisitionStatus };
  onClose: () => void;
}

export function DeleteRequisitionModal({ requisition, onClose }: Props) {
  const [reason, setReason] = useState("");
  const mutation = useDeleteRequisition();
  const valid = reason.trim().length >= 5;

  async function handleConfirm() {
    if (!valid) return;
    try {
      await mutation.mutateAsync({ id: requisition.id, reason: reason.trim() });
      onClose();
    } catch {
      // error surfaced via mutation.error below
    }
  }

  return (
    <Dialog open title={`Delete requisition ${requisition.refNo}?`} onClose={onClose}>
      <p className="text-sm text-red-700 mb-4">
        This will permanently delete the requisition and all related BOM, costing, and approval
        data. This cannot be undone.
      </p>

      <label className="block" htmlFor="delete-reason">
        <span className="text-sm font-medium">Reason (required, min 5 chars)</span>
        <textarea
          id="delete-reason"
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          className="mt-1 w-full rounded border border-input bg-background p-2 text-sm"
          rows={3}
          placeholder="Enter reason for deletion..."
        />
      </label>

      {mutation.error ? (
        <p className="mt-2 text-sm text-red-600">
          Error:{" "}
          {String(
            (mutation.error as { response?: { data?: { error?: string } } }).response?.data
              ?.error ?? (mutation.error as Error).message,
          )}
        </p>
      ) : null}

      <div className="mt-4 flex justify-end gap-2">
        <Button variant="outline" onClick={onClose}>
          Cancel
        </Button>
        <Button
          variant="destructive"
          disabled={!valid || mutation.isPending}
          onClick={handleConfirm}
        >
          {mutation.isPending ? "Deleting..." : "Delete"}
        </Button>
      </div>
    </Dialog>
  );
}
