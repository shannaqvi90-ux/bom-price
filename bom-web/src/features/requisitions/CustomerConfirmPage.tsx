import { useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { toast } from "sonner";
import { useV3Requisition } from "@/features/requisitions/requisitionsApi";
import { useAcceptCustomer, useRejectCustomer } from "@/features/approvals/approvalsApi";
import { V3StatusBadge } from "@/components/v3/V3StatusBadge";
import { Input } from "@/components/ui/Input";
import { Textarea } from "@/components/ui/Textarea";

export function CustomerConfirmPage() {
  const { id } = useParams();
  const navigate = useNavigate();
  const reqId = id ? parseInt(id) : 0;
  const { data: req, isLoading } = useV3Requisition(reqId);
  const accept = useAcceptCustomer();
  const reject = useRejectCustomer();

  const [feedback, setFeedback] = useState("");
  const [rejectReason, setRejectReason] = useState("");
  const [showRejectInput, setShowRejectInput] = useState(false);

  if (isLoading || !req) return <div className="p-6">Loading…</div>;

  const onAccept = async () => {
    try {
      await accept.mutateAsync({
        requisitionId: reqId,
        customerFeedback: feedback || undefined,
      });
      toast.success("Confirmed — sent for MD final sign");
      navigate(`/requisitions/${reqId}`);
    } catch (err: unknown) {
      const message =
        (err as { response?: { data?: { error?: string } } } | null)?.response?.data?.error
        ?? "Failed";
      toast.error(message);
    }
  };

  const onReject = async () => {
    if (rejectReason.trim().length < 5) {
      toast.error("Reason must be ≥5 chars");
      return;
    }
    try {
      await reject.mutateAsync({ requisitionId: reqId, reason: rejectReason });
      toast.success("Sent back to MD for re-pricing");
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

      <h2 className="mt-6 text-lg font-semibold text-foreground">MD-Priced Quotation</h2>
      <table className="mt-2 w-full text-sm">
        <thead className="bg-muted">
          <tr>
            <th className="px-3 py-2 text-left font-medium text-foreground">Finished Good</th>
            <th className="px-3 py-2 text-right font-medium text-foreground">Qty (KG)</th>
            <th className="px-3 py-2 text-right font-medium text-foreground">
              Price/KG ({req.currencyCode})
            </th>
            <th className="px-3 py-2 text-right font-medium text-foreground">Total (AED)</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-border">
          {req.finishedGoods.map((fg) => {
            const priced = req.finalPrice?.perFg.find(
              (p) => p.requisitionItemId === fg.id,
            );
            return (
              <tr key={fg.id}>
                <td className="px-3 py-2">{fg.item.description}</td>
                <td className="px-3 py-2 text-right">{fg.expectedQty.toLocaleString()}</td>
                <td className="px-3 py-2 text-right">
                  {priced ? priced.salePerKg.toFixed(2) : "—"}
                </td>
                <td className="px-3 py-2 text-right">
                  {priced
                    ? priced.totalAed.toLocaleString(undefined, {
                        maximumFractionDigits: 2,
                      })
                    : "—"}
                </td>
              </tr>
            );
          })}
        </tbody>
        {req.finalPrice ? (
          <tfoot>
            <tr className="border-t-2 border-border">
              <td colSpan={3} className="px-3 py-3 text-right text-sm font-bold text-foreground">
                GRAND TOTAL
              </td>
              <td className="px-3 py-3 text-right text-base font-bold text-blue-700">
                AED{" "}
                {req.finalPrice.totalAed.toLocaleString(undefined, {
                  maximumFractionDigits: 2,
                })}
              </td>
            </tr>
          </tfoot>
        ) : null}
      </table>

      <h2 className="mt-8 text-lg font-semibold text-foreground">Customer feedback</h2>
      <Textarea
        value={feedback}
        onChange={(e) => setFeedback(e.target.value)}
        rows={3}
        placeholder="Optional — what did customer say?"
        className="mt-2"
      />

      <div className="mt-6 flex flex-wrap gap-3">
        <button
          onClick={onAccept}
          disabled={accept.isPending}
          className="rounded-md bg-green-600 px-4 py-2 text-sm font-medium text-white hover:bg-green-700 disabled:opacity-50"
        >
          ✓ Customer Accepted
        </button>
        <button
          onClick={() => setShowRejectInput(true)}
          className="rounded-md bg-amber-500 px-4 py-2 text-sm font-medium text-white hover:bg-amber-600"
        >
          ✕ Request MD to Re-price
        </button>
      </div>

      {showRejectInput && (
        <div className="mt-4 rounded-lg border border-amber-200 bg-amber-50 p-4">
          <label className="block">
            <span className="text-sm font-medium text-amber-900">
              Reason for re-price (≥5 chars)
            </span>
            <Input
              value={rejectReason}
              onChange={(e) => setRejectReason(e.target.value)}
              className="mt-1"
            />
          </label>
          <button
            onClick={onReject}
            disabled={reject.isPending}
            className="mt-3 rounded-md bg-amber-600 px-4 py-2 text-sm font-medium text-white hover:bg-amber-700 disabled:opacity-50"
          >
            Send Back to MD
          </button>
        </div>
      )}
    </div>
  );
}
