import { Dialog } from "@/components/ui/Dialog";
import { useBranchChangeHistory } from "./requisitionsApi";

interface Props {
  requisitionId: number;
  open: boolean;
  onClose: () => void;
}

export function BranchChangeHistoryModal({ requisitionId, open, onClose }: Props) {
  const q = useBranchChangeHistory(requisitionId, open);

  return (
    <Dialog open={open} onClose={onClose} title="Branch change history">
      {q.isLoading ? (
        <p className="text-sm text-muted-foreground">Loading…</p>
      ) : q.isError ? (
        <p className="text-sm text-destructive">Failed to load history.</p>
      ) : !Array.isArray(q.data) || q.data.length === 0 ? (
        <p className="text-sm text-muted-foreground">No changes yet.</p>
      ) : (
        <ol className="space-y-3">
          {q.data.map((e) => (
            <li key={e.id} className="border-l-2 border-blue-400 pl-3">
              <p className="text-sm">
                <span className="font-medium">{e.oldBranchName}</span>
                <span className="text-muted-foreground mx-1">→</span>
                <span className="font-medium">{e.newBranchName}</span>
              </p>
              <p className="text-xs text-muted-foreground">
                by {e.changedByUserName} on {new Date(e.changedAt).toLocaleString()}
              </p>
              {e.reason && <p className="text-xs mt-1 italic">"{e.reason}"</p>}
            </li>
          ))}
        </ol>
      )}
    </Dialog>
  );
}
