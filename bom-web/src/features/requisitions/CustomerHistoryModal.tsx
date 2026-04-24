import { Dialog } from "@/components/ui/Dialog";
import { useCustomerChangeHistory } from "./requisitionsApi";

interface Props {
  open: boolean;
  onClose: () => void;
  requisitionId: number;
}

export function CustomerHistoryModal({ open, onClose, requisitionId }: Props) {
  const q = useCustomerChangeHistory(requisitionId, open);

  return (
    <Dialog open={open} onClose={onClose} title="Customer change history">
      {q.isLoading ? (
        <p className="text-sm text-muted-foreground">Loading…</p>
      ) : q.isError ? (
        <p className="text-sm text-destructive">Failed to load history.</p>
      ) : !Array.isArray(q.data) || q.data.length === 0 ? (
        <p className="text-sm text-muted-foreground">No changes yet.</p>
      ) : (
        <ol className="space-y-3">
          {q.data.map((h) => (
            <li key={h.id} className="border-l-2 border-amber-400 pl-3">
              <p className="text-sm">
                <span className="font-medium">{h.oldCustomerName}</span>
                <span className="text-muted-foreground mx-1">→</span>
                <span className="font-medium">{h.newCustomerName}</span>
              </p>
              <p className="text-xs text-muted-foreground">
                by {h.changedByUserName} on {new Date(h.changedAt).toLocaleString()}
              </p>
              {h.reason && <p className="text-xs mt-1 italic">"{h.reason}"</p>}
            </li>
          ))}
        </ol>
      )}
    </Dialog>
  );
}
