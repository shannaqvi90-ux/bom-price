import { useNavigate, useParams } from "react-router-dom";
import { toast } from "sonner";
import { useV3Requisition } from "@/features/requisitions/requisitionsApi";
import { useSetMargin } from "@/features/approvals/approvalsApi";
import { V3StatusBadge } from "@/components/v3/V3StatusBadge";
import { useMdPricingState } from "./useMdPricingState";
import { MdFgPricingCard } from "./MdFgPricingCard";

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
        <h1 className="text-2xl font-semibold text-gray-900">{req.refNo}</h1>
        <V3StatusBadge status={req.status} />
      </div>
      <p className="mt-1 text-sm text-gray-500">
        Customer: {req.customer.name} · Currency: {req.currencyCode}
      </p>

      <h2 className="mt-6 text-lg font-semibold text-gray-900">
        Set Margin per FG
      </h2>
      <p className="mt-1 text-sm text-gray-500">
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

      {state.livePreview ? (
        <div className="mt-4 rounded-lg bg-blue-700 px-5 py-4 text-white">
          <div className="text-xs font-semibold uppercase tracking-wider opacity-90">
            Grand Total (preview)
          </div>
          <div className="mt-1 text-3xl font-bold">
            {req.currencyCode}{" "}
            {state.livePreview.totalAed.toLocaleString(undefined, { maximumFractionDigits: 2 })}
          </div>
          {req.currencyCode !== "AED" ? (
            <div className="mt-1 text-xs opacity-80">
              Backend re-snaps the FX rate at save time. Final AED total computed on the server.
            </div>
          ) : null}
        </div>
      ) : null}

      <label className="mt-6 block">
        <span className="text-sm font-medium text-gray-700">Notes (optional)</span>
        <textarea
          value={state.notes}
          onChange={(e) => state.setNotes(e.target.value)}
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
          disabled={!state.isValid || setMargin.isPending}
          className="rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50"
        >
          {setMargin.isPending ? "Submitting…" : "Approve & send"}
        </button>
      </div>
    </div>
  );
}
