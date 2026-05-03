import { useState } from "react";
import { useRollbackStatus } from "@/api/admin";
import { rollbackTarget } from "@/features/admin/adminOverrideAuthorization";
import { Dialog } from "@/components/ui/Dialog";
import { Button } from "@/components/ui/Button";
import type { RequisitionStatus } from "@/types/api";

interface Props {
  requisition: { id: number; refNo: string; status: RequisitionStatus };
  onClose: () => void;
}

export function RollbackStatusModal({ requisition, onClose }: Props) {
  const target = rollbackTarget(requisition.status);
  const [reason, setReason] = useState("");
  const mutation = useRollbackStatus();
  const valid = !!target && reason.trim().length >= 5;

  if (!target) {
    return (
      <Dialog open title={`Rollback ${requisition.refNo}`} onClose={onClose}>
        <p className="text-sm text-foreground">
          No rollback target available for status {requisition.status}.
        </p>
        <div className="mt-4 flex justify-end">
          <Button variant="outline" onClick={onClose}>
            Close
          </Button>
        </div>
      </Dialog>
    );
  }

  async function handleConfirm() {
    if (!valid) return;
    try {
      await mutation.mutateAsync({ id: requisition.id, targetStatus: target!, reason: reason.trim() });
      onClose();
    } catch {
      // surfaced via mutation.error below
    }
  }

  return (
    <Dialog open title={`Rollback ${requisition.refNo}`} onClose={onClose}>
      <p className="text-sm mb-4">
        {requisition.status} → <strong>{target}</strong>
      </p>

      <label className="block" htmlFor="rollback-reason">
        <span className="text-sm font-medium">Reason (required, min 5 chars)</span>
        <textarea
          id="rollback-reason"
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          className="mt-1 w-full rounded border border-input bg-background p-2 text-sm"
          rows={3}
          placeholder="Enter reason for rollback..."
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
          {mutation.isPending ? "Rolling back..." : "Rollback"}
        </Button>
      </div>
    </Dialog>
  );
}
