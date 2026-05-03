import { useState } from "react";
import { toast } from "sonner";
import { useRejectRequisition } from "./approvalsApi";
import { Textarea } from "@/components/ui/Textarea";

interface Props {
  requisitionId: number;
  refNo: string;
  open: boolean;
  onClose: () => void;
  onRejected?: () => void;
}

export function RejectReqModal({ requisitionId, refNo, open, onClose, onRejected }: Props) {
  const reject = useRejectRequisition();
  const [reason, setReason] = useState("");
  const [error, setError] = useState<string | null>(null);

  if (!open) return null;

  const handleReject = async () => {
    if (reason.trim().length < 5) {
      setError("Reason must be at least 5 characters.");
      return;
    }
    setError(null);
    try {
      await reject.mutateAsync({
        requisitionId,
        payload: { notes: reason.trim() },
      });
      toast.success(`${refNo} rejected — sales person notified`);
      setReason("");
      onClose();
      onRejected?.();
    } catch (err: unknown) {
      const message =
        (err as { response?: { data?: { error?: string } } } | null)?.response?.data?.error
        ?? "Reject failed";
      setError(message);
    }
  };

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby="reject-modal-title"
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4"
      onClick={onClose}
    >
      <div
        className="w-full max-w-md rounded-lg bg-card p-6 shadow-xl"
        onClick={(e) => e.stopPropagation()}
      >
        <h2 id="reject-modal-title" className="text-lg font-semibold text-foreground">
          Reject {refNo}
        </h2>
        <p className="mt-1 text-sm text-muted-foreground">
          Provide a reason. The req moves to Rejected and the sales person is notified.
        </p>

        <label className="mt-4 block">
          <span className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">
            Reason
          </span>
          <Textarea
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            placeholder="e.g. Material cost looks wrong, please verify…"
            rows={4}
            className="mt-1 resize-none"
            autoFocus
          />
        </label>

        {error ? (
          <div className="mt-3 rounded-md border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-800">
            {error}
          </div>
        ) : null}

        <div className="mt-5 flex justify-end gap-3">
          <button
            type="button"
            onClick={onClose}
            className="rounded-md border border-border bg-card px-4 py-2 text-sm font-medium text-foreground hover:bg-muted"
          >
            Cancel
          </button>
          <button
            type="button"
            onClick={handleReject}
            disabled={reject.isPending}
            className="rounded-md bg-red-600 px-4 py-2 text-sm font-medium text-white hover:bg-red-700 disabled:opacity-50"
          >
            {reject.isPending ? "Rejecting…" : "Reject"}
          </button>
        </div>
      </div>
    </div>
  );
}
