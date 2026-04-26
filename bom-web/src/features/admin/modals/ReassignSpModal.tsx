import { useState } from "react";
import { useReassignSp } from "@/api/admin";
import { useUsers } from "@/features/users/usersApi";
import { Dialog } from "@/components/ui/Dialog";
import { Button } from "@/components/ui/Button";
import type { RequisitionStatus } from "@/types/api";

interface Props {
  requisition: { id: number; refNo: string; status: RequisitionStatus };
  onClose: () => void;
}

export function ReassignSpModal({ requisition, onClose }: Props) {
  const [newSpId, setNewSpId] = useState<number | null>(null);
  const [reason, setReason] = useState("");
  const mutation = useReassignSp();
  const { data: users } = useUsers();

  const activeSps = (users ?? []).filter(
    (u) => u.role === "SalesPerson" && u.isActive,
  );
  const valid = newSpId !== null && reason.trim().length >= 5;

  async function handleConfirm() {
    if (!valid || newSpId === null) return;
    try {
      await mutation.mutateAsync({
        id: requisition.id,
        newSalesPersonId: newSpId,
        reason: reason.trim(),
      });
      onClose();
    } catch {
      // surfaced via mutation.error below
    }
  }

  return (
    <Dialog open title={`Reassign SP — ${requisition.refNo}`} onClose={onClose}>
      <label className="block" htmlFor="reassign-sp">
        <span className="text-sm font-medium">New salesperson</span>
        <select
          id="reassign-sp"
          value={newSpId ?? ""}
          onChange={(e) =>
            setNewSpId(e.target.value ? Number(e.target.value) : null)
          }
          className="mt-1 w-full rounded border border-input bg-background p-2 text-sm"
        >
          <option value="">— select —</option>
          {activeSps.map((sp) => (
            <option key={sp.id} value={sp.id}>
              {sp.name}
            </option>
          ))}
        </select>
      </label>

      <label className="mt-4 block" htmlFor="reassign-reason">
        <span className="text-sm font-medium">Reason (required, min 5 chars)</span>
        <textarea
          id="reassign-reason"
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          className="mt-1 w-full rounded border border-input bg-background p-2 text-sm"
          rows={3}
          placeholder="Enter reason for reassignment..."
        />
      </label>

      {mutation.error ? (
        <p className="mt-2 text-sm text-red-600">
          Error:{" "}
          {String(
            (mutation.error as { response?: { data?: { error?: string } } })
              .response?.data?.error ?? (mutation.error as Error).message,
          )}
        </p>
      ) : null}

      <div className="mt-4 flex justify-end gap-2">
        <Button variant="outline" onClick={onClose}>
          Cancel
        </Button>
        <Button disabled={!valid || mutation.isPending} onClick={handleConfirm}>
          {mutation.isPending ? "Reassigning..." : "Reassign"}
        </Button>
      </div>
    </Dialog>
  );
}
