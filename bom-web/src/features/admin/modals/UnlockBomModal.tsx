import { useState } from "react";
import { useUnlockBom } from "@/api/admin";
import { Dialog } from "@/components/ui/Dialog";
import { Button } from "@/components/ui/Button";
import type { RequisitionStatus } from "@/types/api";

interface Props {
  requisition: { id: number; refNo: string; status: RequisitionStatus };
  onClose: () => void;
}

export function UnlockBomModal({ requisition, onClose }: Props) {
  const [reason, setReason] = useState("");
  const mutation = useUnlockBom();
  const valid = reason.trim().length >= 5;

  async function handleConfirm() {
    if (!valid) return;
    try {
      await mutation.mutateAsync({ id: requisition.id, reason: reason.trim() });
      onClose();
    } catch {
      // surfaced via mutation.error
    }
  }

  return (
    <Dialog open title={`Unlock BOM — ${requisition.refNo}`} onClose={onClose}>
      <p className="text-sm mb-4">
        Status will revert to <strong>BomInProgress</strong> so the BomCreator can re-edit.
        Existing BOM data is preserved.
      </p>

      <label className="block" htmlFor="unlock-bom-reason">
        <span className="text-sm font-medium">Reason (required, min 5 chars)</span>
        <textarea
          id="unlock-bom-reason"
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          className="mt-1 w-full rounded border border-input bg-background p-2 text-sm"
          rows={3}
          placeholder="Enter reason for unlock..."
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
        <Button disabled={!valid || mutation.isPending} onClick={handleConfirm}>
          {mutation.isPending ? "Unlocking..." : "Unlock BOM"}
        </Button>
      </div>
    </Dialog>
  );
}
