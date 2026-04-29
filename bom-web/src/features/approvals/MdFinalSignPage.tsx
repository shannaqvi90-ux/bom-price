import { useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { toast } from "sonner";
import { useV3Requisition } from "@/features/requisitions/requisitionsApi";
import { useFinalSign } from "@/features/approvals/approvalsApi";
import { V3StatusBadge } from "@/components/v3/V3StatusBadge";

export function MdFinalSignPage() {
  const { id } = useParams();
  const navigate = useNavigate();
  const reqId = id ? parseInt(id) : 0;
  const { data: req, isLoading } = useV3Requisition(reqId);
  const sign = useFinalSign();
  const [token, setToken] = useState("");
  const [notes, setNotes] = useState("");

  if (isLoading || !req) return <div className="p-6">Loading…</div>;

  const onSubmit = async () => {
    try {
      const result = await sign.mutateAsync({
        requisitionId: reqId,
        payload: { confirmationToken: token, notes: notes || undefined },
      });
      toast.success("Signed and locked. PDF available.");
      navigate(`/requisitions/${reqId}`);
      void result; // pdfDownloadUrl returned but RequisitionDetailPage handles download
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
      <p className="mt-1 text-sm text-gray-500">Customer: {req.customer.name}</p>

      <div className="mt-6 rounded-lg border border-orange-200 bg-orange-50 p-4">
        <h2 className="text-base font-semibold text-orange-900">
          Final sign locks this quotation
        </h2>
        <p className="mt-1 text-sm text-orange-800">
          After signing, no changes can be made. The PDF will be generated immediately
          and can be downloaded by the salesperson to share with the customer manually.
        </p>
      </div>

      <label className="mt-6 block">
        <span className="text-sm font-medium text-gray-700">Notes (optional)</span>
        <textarea
          value={notes}
          onChange={(e) => setNotes(e.target.value)}
          rows={3}
          className="mt-1 w-full rounded-md border-gray-300 px-3 py-2 text-sm"
        />
      </label>

      <label className="mt-4 block">
        <span className="text-sm font-medium text-gray-700">Type SIGN to confirm</span>
        <input
          value={token}
          onChange={(e) => setToken(e.target.value)}
          aria-label="type SIGN to confirm"
          placeholder="SIGN"
          className="mt-1 w-full rounded-md border-gray-300 px-3 py-2 text-sm font-mono uppercase"
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
          disabled={token !== "SIGN" || sign.isPending}
          className="rounded-md bg-orange-600 px-4 py-2 text-sm font-medium text-white hover:bg-orange-700 disabled:opacity-50"
        >
          Sign and Lock
        </button>
      </div>
    </div>
  );
}
