import { useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { toast } from "sonner";
import { useV3Requisition } from "@/features/requisitions/requisitionsApi";
import { useSetMargin } from "@/features/approvals/approvalsApi";
import { V3StatusBadge } from "@/components/v3/V3StatusBadge";

export function MdMarginPage() {
  const { id } = useParams();
  const navigate = useNavigate();
  const reqId = id ? parseInt(id) : 0;
  const { data: req, isLoading } = useV3Requisition(reqId);
  const setMargin = useSetMargin();
  const [margins, setMargins] = useState<Record<number, number>>({});
  const [notes, setNotes] = useState("");

  if (isLoading || !req) return <div className="p-6">Loading…</div>;

  const onSubmit = async () => {
    const items = req.finishedGoods.map((fg) => ({
      requisitionItemId: fg.id,
      marginPerKg: margins[fg.id] ?? 0,
    }));
    if (items.some((i) => i.marginPerKg < 0)) {
      toast.error("Margin must be ≥ 0");
      return;
    }

    try {
      await setMargin.mutateAsync({
        requisitionId: reqId,
        payload: { notes: notes || undefined, items },
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
        <h1 className="text-2xl font-semibold text-gray-900">{req.refNo}</h1>
        <V3StatusBadge status={req.status} />
      </div>
      <p className="mt-1 text-sm text-gray-500">
        Customer: {req.customer.name} · Currency: {req.currencyCode}
      </p>

      <h2 className="mt-6 text-lg font-semibold text-gray-900">
        Set Margin per FG ({req.currencyCode}/KG)
      </h2>
      <table className="mt-2 w-full text-sm">
        <thead className="bg-gray-50">
          <tr>
            <th className="px-3 py-2 text-left font-medium text-gray-700">Finished Good</th>
            <th className="px-3 py-2 text-right font-medium text-gray-700">Qty (KG)</th>
            <th className="px-3 py-2 text-right font-medium text-gray-700">Margin/KG</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-gray-100">
          {req.finishedGoods.map((fg) => (
            <tr key={fg.id}>
              <td className="px-3 py-2">{fg.item.description}</td>
              <td className="px-3 py-2 text-right">{fg.expectedQty.toLocaleString()}</td>
              <td className="px-3 py-2 text-right">
                <input
                  type="number"
                  step="0.01"
                  value={margins[fg.id] ?? ""}
                  onChange={(e) =>
                    setMargins((m) => ({
                      ...m,
                      [fg.id]: parseFloat(e.target.value) || 0,
                    }))
                  }
                  className="w-28 rounded border-gray-300 px-2 py-1 text-right text-sm"
                />
              </td>
            </tr>
          ))}
        </tbody>
      </table>

      <label className="mt-6 block">
        <span className="text-sm font-medium text-gray-700">Notes (optional)</span>
        <textarea
          value={notes}
          onChange={(e) => setNotes(e.target.value)}
          rows={3}
          className="mt-1 w-full rounded-md border-gray-300 px-3 py-2 text-sm"
        />
      </label>

      <div className="mt-6 flex justify-end gap-3">
        <button
          onClick={() => navigate(-1)}
          className="rounded-md border border-gray-300 bg-white px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50"
        >
          Cancel
        </button>
        <button
          onClick={onSubmit}
          disabled={setMargin.isPending}
          className="rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50"
        >
          Submit
        </button>
      </div>
    </div>
  );
}
