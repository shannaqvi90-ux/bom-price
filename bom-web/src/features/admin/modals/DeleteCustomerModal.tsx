import { useState } from "react";
import { useHardDeleteCustomer, type HardDeleteCustomerBlocked } from "@/api/admin";
import { Button } from "@/components/ui/Button";
import { Dialog } from "@/components/ui/Dialog";

interface Props {
  customer: { id: number; code: string; name: string };
  onClose: () => void;
}

interface ApiErrorShape {
  response?: { status?: number; data?: HardDeleteCustomerBlocked | { error?: string } };
  message?: string;
}

function isBlockedResponse(data: unknown): data is HardDeleteCustomerBlocked {
  return (
    typeof data === "object" &&
    data !== null &&
    Array.isArray((data as HardDeleteCustomerBlocked).blockingRequisitions)
  );
}

export function DeleteCustomerModal({ customer, onClose }: Props) {
  const [reason, setReason] = useState("");
  const [confirmed, setConfirmed] = useState(false);
  const mutation = useHardDeleteCustomer();
  const valid = reason.trim().length >= 5 && confirmed;

  async function handleConfirm() {
    if (!valid) return;
    try {
      await mutation.mutateAsync({ id: customer.id, reason: reason.trim() });
      onClose();
    } catch {
      // surfaced via mutation.error below
    }
  }

  const err = mutation.error as ApiErrorShape | null;
  const blocked =
    err?.response?.status === 409 && isBlockedResponse(err.response.data)
      ? err.response.data
      : null;
  const otherError =
    err && !blocked
      ? (err.response?.data as { error?: string } | undefined)?.error ?? err.message ?? "Unknown error"
      : null;

  return (
    <Dialog open title={`Delete customer ${customer.code}?`} onClose={onClose}>
      <p className="text-sm text-red-700 mb-3 dark:text-red-300">
        This will permanently anonymize <strong>{customer.name}</strong> and hide it from all
        listings. Historical requisitions will still reference the (now-anonymized) record. PII
        (name / email / phone / address) will be erased. This cannot be undone.
      </p>

      {blocked && (
        <div className="mb-4 rounded border border-red-300 bg-red-50 p-3 text-sm dark:border-red-800/60 dark:bg-red-900/30">
          <p className="font-medium text-red-800 dark:text-red-300">{blocked.error}</p>
          <p className="mt-1 text-red-700 dark:text-red-300">
            Blocking requisition IDs: {blocked.blockingRequisitions.join(", ")}
          </p>
          <p className="mt-1 text-xs text-red-600 dark:text-red-300">
            Resolve (approve / reject / hard-delete) those requisitions before deleting this
            customer.
          </p>
        </div>
      )}

      <label className="block" htmlFor="delete-customer-reason">
        <span className="text-sm font-medium">Reason (required, min 5 chars)</span>
        <textarea
          id="delete-customer-reason"
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          className="mt-1 w-full rounded border border-input bg-background p-2 text-sm"
          rows={3}
          placeholder="Enter reason for deletion..."
        />
      </label>

      <label className="mt-3 flex items-start gap-2 text-sm">
        <input
          type="checkbox"
          checked={confirmed}
          onChange={(e) => setConfirmed(e.target.checked)}
          className="mt-0.5"
        />
        <span>
          I understand this will erase customer PII and the action cannot be undone.
        </span>
      </label>

      {otherError && (
        <p className="mt-2 text-sm text-red-600">Error: {otherError}</p>
      )}

      <div className="mt-4 flex justify-end gap-2">
        <Button variant="outline" onClick={onClose}>
          Cancel
        </Button>
        <Button
          variant="destructive"
          disabled={!valid || mutation.isPending}
          onClick={handleConfirm}
        >
          {mutation.isPending ? "Deleting..." : "Delete Customer"}
        </Button>
      </div>
    </Dialog>
  );
}
