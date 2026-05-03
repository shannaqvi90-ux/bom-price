import { useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { toast } from "sonner";
import { useV3Requisition } from "@/features/requisitions/requisitionsApi";
import { useSetMargin } from "@/features/approvals/approvalsApi";
import { V3StatusBadge } from "@/components/v3/V3StatusBadge";
import { useMdPricingState } from "./useMdPricingState";
import { MdFgPricingCard } from "./MdFgPricingCard";
import { RejectReqModal } from "./RejectReqModal";
import { Textarea } from "@/components/ui/Textarea";

export function MdMarginPage() {
  const { id } = useParams();
  const navigate = useNavigate();
  const reqId = id ? parseInt(id) : 0;
  const { data: req, isLoading } = useV3Requisition(reqId);
  const setMargin = useSetMargin();

  if (isLoading || !req) return <div className="p-6">Loading…</div>;

  return <MdMarginPageBody req={req} reqId={reqId} setMargin={setMargin} navigate={navigate} />;
}

interface BodyProps {
  req: NonNullable<ReturnType<typeof useV3Requisition>["data"]>;
  reqId: number;
  setMargin: ReturnType<typeof useSetMargin>;
  navigate: ReturnType<typeof useNavigate>;
}

function MdMarginPageBody({ req, reqId, setMargin, navigate }: BodyProps) {
  const state = useMdPricingState(req);
  const [rejectOpen, setRejectOpen] = useState(false);

  const onSubmit = async () => {
    if (!state.isValid) {
      toast.error("Enter a margin (≥ 0) for every FG");
      return;
    }
    const items = req.finishedGoods.map((fg) => ({
      requisitionItemId: fg.id,
      marginPerKg: state.parsed[fg.id]!,
    }));
    try {
      await setMargin.mutateAsync({
        requisitionId: reqId,
        payload: { notes: state.notes.trim() || undefined, items },
      });
      toast.success("Margin saved — now sales will confirm with customer");
      navigate(`/requisitions/${reqId}`);
    } catch (err: unknown) {
      const message =
        (err as { response?: { data?: { error?: string } } } | null)?.response?.data?.error
        ?? "Failed";
      toast.error(message);
    }
  };

  return (
    <div className="mx-auto max-w-4xl p-6">
      <div className="flex items-center gap-3">
        <h1 className="text-2xl font-semibold text-foreground">{req.refNo}</h1>
        <V3StatusBadge status={req.status} />
      </div>
      <p className="mt-1 text-sm text-muted-foreground">
        Customer: {req.customer.name} · Currency: {req.currencyCode}
      </p>

      {req.previousMargin ? (
        <div className="mt-6 rounded-lg border border-amber-200 bg-amber-50 p-4">
          <div className="flex items-baseline justify-between">
            <h3 className="text-sm font-semibold text-amber-900">
              Previous attempt — rejected by customer
            </h3>
            <span className="text-xs text-amber-700">
              {new Date(req.previousMargin.supersededAt).toLocaleDateString()}
            </span>
          </div>
          <table className="mt-2 w-full text-xs">
            <thead className="text-amber-800">
              <tr>
                <th className="px-1 py-1 text-left font-medium">Finished Good</th>
                <th className="px-1 py-1 text-right font-medium">
                  Prev Margin/KG ({req.currencyCode})
                </th>
                <th className="px-1 py-1 text-right font-medium">
                  Prev Sale/KG ({req.currencyCode})
                </th>
              </tr>
            </thead>
            <tbody>
              {req.finishedGoods.map((fg) => {
                const prev = req.previousMargin!.items.find(
                  (p) => p.requisitionItemId === fg.id,
                );
                const cost = fg.costs?.totalCostPerKg ?? 0;
                const prevSale = prev != null ? cost + prev.marginPerKg : null;
                return (
                  <tr key={fg.id}>
                    <td className="px-1 py-1 text-amber-900">{fg.item.description}</td>
                    <td className="px-1 py-1 text-right text-amber-900">
                      {prev != null ? prev.marginPerKg.toFixed(2) : "—"}
                    </td>
                    <td className="px-1 py-1 text-right font-medium text-amber-900">
                      {prevSale != null ? prevSale.toFixed(2) : "—"}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
          <p className="mt-2 text-xs text-amber-800">
            Customer feedback recorded in Notes (above). Re-price below.
          </p>
        </div>
      ) : null}

      <h2 className="mt-6 text-lg font-semibold text-foreground">
        Set Margin per FG
      </h2>
      <p className="mt-1 text-sm text-muted-foreground">
        Cost includes raw materials, FOH, transport, and commission. Expand each card to see BOM lines.
      </p>

      <div className="mt-3 space-y-3">
        {req.finishedGoods.map((fg, idx) => (
          <MdFgPricingCard
            key={fg.id}
            fg={fg}
            index={idx}
            marginInput={state.margins[fg.id] ?? ""}
            onMarginChange={(v) => state.setMargin(fg.id, v)}
            livePerFg={
              state.livePreview?.perFg.find((p) => p.requisitionItemId === fg.id) ?? null
            }
            currencyCode={req.currencyCode}
          />
        ))}
      </div>

      <label className="mt-6 block">
        <span className="text-sm font-medium text-foreground">Notes (optional)</span>
        <Textarea
          value={state.notes}
          onChange={(e) => state.setNotes(e.target.value)}
          rows={3}
          className="mt-1"
        />
      </label>

      <div className="mt-6 flex items-center justify-between gap-3">
        <button
          onClick={() => setRejectOpen(true)}
          className="rounded-md border border-red-300 bg-card px-4 py-2 text-sm font-medium text-red-700 hover:bg-red-50"
        >
          Reject
        </button>
        <div className="flex gap-3">
          <button
            onClick={() => navigate(-1)}
            className="rounded-md border border-border bg-card px-4 py-2 text-sm font-medium text-foreground hover:bg-muted"
          >
            Cancel
          </button>
          <button
            onClick={onSubmit}
            disabled={!state.isValid || setMargin.isPending}
            className="rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50"
          >
            {setMargin.isPending ? "Submitting…" : "Approve & send"}
          </button>
        </div>
      </div>

      <RejectReqModal
        requisitionId={reqId}
        refNo={req.refNo}
        open={rejectOpen}
        onClose={() => setRejectOpen(false)}
        onRejected={() => navigate(`/requisitions/${reqId}`)}
      />
    </div>
  );
}
