import { Link, useNavigate, useParams } from "react-router-dom";
import { ArrowLeft } from "lucide-react";
import { useState } from "react";
import { notify } from "@/lib/notify";
import { extractFieldErrors } from "@/lib/apiError";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/Card";
import { Button } from "@/components/ui/Button";
import { Dialog } from "@/components/ui/Dialog";
import { ConfirmDialog } from "@/components/ui/ConfirmDialog";
import { api } from "@/api/axios";
import { useBom } from "@/features/bom/bomApi";
import { useCustomerChangeHistory } from "@/features/requisitions/requisitionsApi";
import { CustomerHistoryModal } from "@/features/requisitions/CustomerHistoryModal";
import {
  useMdReview,
  useApproveRequisition,
  useRejectRequisition,
} from "./approvalsApi";

type PageState =
  | { kind: "reviewing" }
  | { kind: "approved" };

function LabeledRow({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex justify-between gap-4 py-1 text-sm">
      <span className="text-muted-foreground">{label}</span>
      <span className="text-right font-mono">{value}</span>
    </div>
  );
}

export default function MdReviewPage() {
  const { id } = useParams<{ id: string }>();
  const requisitionId = Number(id);
  const navigate = useNavigate();
  const { data, isLoading, error } = useMdReview(requisitionId);
  const approve = useApproveRequisition();
  const reject = useRejectRequisition();

  const { data: bom } = useBom(requisitionId);

  const [pageState, setPageState] = useState<PageState>({ kind: "reviewing" });
  const [salesPrices, setSalesPrices] = useState<Record<number, string>>({});
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const [notes, setNotes] = useState("");
  const [showBom, setShowBom] = useState(false);
  const [confirmApprove, setConfirmApprove] = useState(false);
  const [confirmReject, setConfirmReject] = useState(false);
  const [historyOpen, setHistoryOpen] = useState(false);
  const historyQ = useCustomerChangeHistory(requisitionId, true);
  const historyCount = historyQ.data?.length ?? 0;

  const httpStatus = (error as { response?: { status?: number } } | null)
    ?.response?.status;

  if (httpStatus === 404) {
    return (
      <Card className="mx-auto max-w-lg">
        <CardContent className="py-8 text-center">
          <p className="text-sm">Requisition not found.</p>
          <Link to="/requisitions" className="mt-4 inline-block text-sm underline">
            Back to Requisitions
          </Link>
        </CardContent>
      </Card>
    );
  }

  if (httpStatus === 403) {
    return (
      <Card className="mx-auto max-w-lg">
        <CardContent className="py-8 text-center">
          <p className="text-sm">You don't have access to this requisition.</p>
          <Link to="/requisitions" className="mt-4 inline-block text-sm underline">
            Back to Requisitions
          </Link>
        </CardContent>
      </Card>
    );
  }

  if (error) {
    return (
      <Card className="mx-auto max-w-lg">
        <CardContent className="py-8 text-center text-destructive">
          Failed to load review data.
        </CardContent>
      </Card>
    );
  }

  if (isLoading || !data) {
    return <p className="text-sm text-muted-foreground">Loading…</p>;
  }

  const allPricesValid = data.items
    .filter((item) => item.cost !== null)
    .every((item) => {
      const price = Number(salesPrices[item.requisitionItemId] ?? "");
      return Number.isFinite(price) && price > 0;
    });

  const uncostdItems = data.items.filter((item) => item.cost === null);

  function requestApprove() {
    setFieldErrors({});
    const items = data!.items
      .filter((item) => item.cost !== null)
      .map((item) => {
        const price = Number(salesPrices[item.requisitionItemId] ?? "");
        return { requisitionItemId: item.requisitionItemId, salesPricePerKgAed: price };
      });
    if (items.some((i) => !Number.isFinite(i.salesPricePerKgAed) || i.salesPricePerKgAed <= 0)) {
      notify.error("Enter a valid sales price for all items.");
      return;
    }
    setConfirmApprove(true);
  }

  async function handleApproveConfirmed() {
    const items = data!.items
      .filter((item) => item.cost !== null)
      .map((item) => {
        const price = Number(salesPrices[item.requisitionItemId] ?? "");
        return { requisitionItemId: item.requisitionItemId, salesPricePerKgAed: price };
      });
    try {
      await approve.mutateAsync({
        requisitionId,
        payload: { items, notes: notes || undefined },
      });
      setConfirmApprove(false);
      notify.success("Quotation approved");
      setPageState({ kind: "approved" });
    } catch (e) {
      setConfirmApprove(false);
      setFieldErrors(extractFieldErrors(e));
      notify.fromApiError(e, "Failed to approve.");
    }
  }

  function requestReject() {
    if (notes.trim().length === 0) {
      notify.error("Notes are required when rejecting.");
      return;
    }
    setConfirmReject(true);
  }

  async function handleRejectConfirmed() {
    try {
      await reject.mutateAsync({
        requisitionId,
        payload: { notes: notes.trim() },
      });
      setConfirmReject(false);
      notify.success("Quotation rejected");
      navigate(`/requisitions/${requisitionId}`);
    } catch (e) {
      setConfirmReject(false);
      notify.fromApiError(e, "Failed to reject.");
    }
  }

  async function handleDownloadPdf() {
    try {
      const response = await api.get(`/approvals/${requisitionId}/pdf`, {
        responseType: "blob",
      });
      const url = URL.createObjectURL(new Blob([response.data], { type: "application/pdf" }));
      const a = document.createElement("a");
      a.href = url;
      a.download = `${data!.refNo}-Quotation.pdf`;
      a.click();
      URL.revokeObjectURL(url);
    } catch (e) {
      notify.fromApiError(e, "Failed to download PDF.");
    }
  }

  return (
    <div className="space-y-6">
      <Link
        to={`/requisitions/${requisitionId}`}
        className="inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground"
      >
        <ArrowLeft className="h-4 w-4" /> Back to Requisition
      </Link>

      <div className="flex items-start justify-between gap-4">
        <div>
          <h1 className="font-mono text-2xl font-semibold">{data.refNo}</h1>
          <p className="mt-1 text-sm text-muted-foreground">
            {data.customerName} · {data.currencyCode}
            {data.exchangeRate !== null && ` · Rate: ${data.exchangeRate}`}
          </p>
          {historyCount > 0 && (
            <button
              type="button"
              onClick={() => setHistoryOpen(true)}
              className="mt-1 inline-flex items-center rounded-full bg-amber-100 px-2 py-0.5 text-xs text-amber-800 hover:bg-amber-200"
            >
              Customer changed ({historyCount})
            </button>
          )}
        </div>
        <Button variant="outline" onClick={() => setShowBom(true)}>
          View BOM
        </Button>
      </div>

      {/* Partial-costing warning banner */}
      {!data.readyForReview && uncostdItems.length > 0 && (
        <div
          role="alert"
          className="rounded-md border border-yellow-300 bg-yellow-50 px-4 py-3 text-sm text-yellow-900"
        >
          <span className="font-semibold">
            {uncostdItems.length} item{uncostdItems.length > 1 ? "s" : ""} awaiting costing before approval can be done:
          </span>{" "}
          {uncostdItems.map((i) => i.itemDescription).join(", ")}
        </div>
      )}

      {/* Per-item cost breakdown */}
      <div className="space-y-4">
        {data.items.map((item, idx) => {
          const priceStr = salesPrices[item.requisitionItemId] ?? "";
          const price = Number(priceStr);
          const hasValidPrice = Number.isFinite(price) && price > 0;
          const marginPct =
            hasValidPrice && item.cost
              ? ((price - item.cost.totalCostPerKg) / price) * 100
              : 0;
          const priceErr = fieldErrors[`items.${idx}.salesPricePerKgAed`];

          return (
            <Card key={item.requisitionItemId}>
              <CardHeader>
                <CardTitle className="text-base">
                  {item.itemDescription}
                  <span className="ml-2 text-xs font-normal text-muted-foreground">
                    {item.expectedQty.toLocaleString()} kg
                  </span>
                </CardTitle>
              </CardHeader>
              <CardContent>
                {item.cost === null ? (
                  <p className="text-sm text-muted-foreground">Awaiting costing</p>
                ) : (
                  <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
                    <div>
                      <LabeledRow label="Raw Material" value={`${item.cost.rawMaterialCostPerKg.toFixed(4)} /kg`} />
                      <LabeledRow label="Landed Cost" value={`${item.cost.landedCostPerKg.toFixed(4)} /kg`} />
                      <LabeledRow label="FOH" value={`${item.cost.fohPerKg.toFixed(4)} /kg`} />
                      <div className="mt-2 flex justify-between gap-4 border-t pt-2 text-sm font-semibold">
                        <span>Total Cost/kg</span>
                        <span className="font-mono">{item.cost.totalCostPerKg.toFixed(4)}</span>
                      </div>
                      <div className="mt-2 text-xs text-muted-foreground">
                        Material {item.cost.materialCostPct.toFixed(1)}% · Landed {item.cost.landedCostPct.toFixed(1)}% · FOH {item.cost.fohPct.toFixed(1)}%
                      </div>
                    </div>

                    {pageState.kind === "reviewing" && (
                      <div className="space-y-3">
                        <div>
                          <label className="mb-1 block text-xs text-muted-foreground">
                            Sales Price (AED/kg)
                          </label>
                          <input
                            type="number"
                            step="0.0001"
                            min="0"
                            value={priceStr}
                            onChange={(e) => {
                              setFieldErrors({});
                              setSalesPrices((prev) => ({
                                ...prev,
                                [item.requisitionItemId]: e.target.value,
                              }));
                            }}
                            className={`w-full rounded-md border px-3 py-2 text-sm ${priceErr ? "border-destructive" : ""}`}
                            placeholder="0.0000"
                            aria-label="Sales Price (AED/kg)"
                          />
                          {priceErr && <p className="text-xs text-destructive">{priceErr}</p>}
                        </div>
                        {hasValidPrice && (
                          <div
                            role={marginPct < 0 ? "alert" : undefined}
                            aria-live={marginPct < 0 ? "polite" : undefined}
                            className={`rounded-md p-2 text-center text-sm font-semibold ${
                              marginPct > 0
                                ? "bg-green-50 text-green-800"
                                : "bg-red-50 text-red-800"
                            }`}
                          >
                            <span>Margin: {marginPct.toFixed(2)}%</span>
                            {marginPct < 0 && (
                              <span className="ml-2 inline-flex items-center rounded-full bg-red-100 px-2 py-0.5 text-xs">
                                ⚠ Negative margin
                              </span>
                            )}
                          </div>
                        )}
                      </div>
                    )}
                  </div>
                )}
              </CardContent>
            </Card>
          );
        })}
      </div>

      {/* Decision panel */}
      <Card>
        <CardHeader>
          <CardTitle>
            {pageState.kind === "approved" ? "Approved" : "Your Decision"}
          </CardTitle>
        </CardHeader>
        <CardContent>
          {pageState.kind === "approved" ? (
            <div className="space-y-4">
              <div className="rounded-md bg-green-50 p-3 text-sm text-green-800">
                Quotation approved
              </div>
              <Button onClick={handleDownloadPdf} className="w-full">
                Download PDF
              </Button>
            </div>
          ) : (
            <div className="space-y-4">
              <div>
                <label className="mb-1 block text-xs text-muted-foreground" htmlFor="notes">
                  Notes (required when rejecting)
                </label>
                <textarea
                  id="notes"
                  value={notes}
                  onChange={(e) => setNotes(e.target.value)}
                  className="w-full rounded-md border px-3 py-2 text-sm"
                  rows={3}
                />
              </div>


              <div className="flex gap-2">
                <Button
                  onClick={requestApprove}
                  disabled={approve.isPending || !allPricesValid || !data.readyForReview}
                  className="flex-1 bg-green-700 hover:bg-green-800"
                >
                  {approve.isPending ? "Approving…" : "Approve All"}
                </Button>
                <Button
                  onClick={requestReject}
                  disabled={reject.isPending}
                  variant="destructive"
                  className="flex-1"
                >
                  {reject.isPending ? "Rejecting…" : "Reject"}
                </Button>
              </div>
            </div>
          )}
        </CardContent>
      </Card>

      {/* BOM Dialog */}
      <Dialog
        open={showBom}
        onClose={() => setShowBom(false)}
        title="Bill of Materials"
        className="max-w-3xl"
      >
        {bom ? (
          <div className="space-y-6 text-sm">
            {bom.items.map((item) => (
              <div key={item.requisitionItemId}>
                <p className="mb-2 font-semibold">{item.itemDescription} ({item.expectedQty} kg)</p>
                {Array.from(new Set(item.lines.map((l) => l.processName))).map((proc) => (
                  <div key={proc} className="mb-3">
                    <p className="mb-1 text-xs font-semibold text-muted-foreground">{proc}</p>
                    <table className="w-full text-xs">
                      <thead>
                        <tr className="border-b text-left text-muted-foreground">
                          <th className="pb-1 font-medium">Material</th>
                          <th className="pb-1 text-right font-medium">Qty/kg</th>
                          <th className="pb-1 text-right font-medium">Wastage%</th>
                          <th className="pb-1 text-right font-medium">Cost/kg</th>
                          <th className="pb-1 text-right font-medium">Contribution</th>
                        </tr>
                      </thead>
                      <tbody>
                        {item.lines
                          .filter((l) => l.processName === proc)
                          .map((l) => (
                            <tr key={l.id} className="border-b last:border-0">
                              <td className="py-1">{l.rawMaterialDescription}</td>
                              <td className="py-1 text-right font-mono">{l.qtyPerKg.toFixed(4)}</td>
                              <td className="py-1 text-right font-mono">{l.wastagePct.toFixed(2)}%</td>
                              <td className="py-1 text-right font-mono">
                                {l.costPerKg != null
                                  ? `${l.costPerKg.toFixed(4)} ${l.currencyCode}`
                                  : "—"}
                              </td>
                              <td className="py-1 text-right font-mono">
                                {l.contributionAed != null
                                  ? `${l.contributionAed.toFixed(4)} AED`
                                  : "—"}
                              </td>
                            </tr>
                          ))}
                      </tbody>
                    </table>
                  </div>
                ))}
                <div className="flex justify-between gap-4 border-t pt-1 text-xs font-semibold">
                  <span>Total Cost/kg</span>
                  <span className="font-mono">{item.totalCostPerKg.toFixed(4)} AED/kg</span>
                </div>
              </div>
            ))}
          </div>
        ) : (
          <p className="text-sm text-muted-foreground">Loading BOM…</p>
        )}
      </Dialog>

      <ConfirmDialog
        open={confirmApprove}
        title="Approve quotation?"
        message={`This will approve ${data.refNo} and notify the salesperson. The quotation PDF will be generated and emailed. Continue?`}
        confirmLabel="Approve"
        isPending={approve.isPending}
        onConfirm={handleApproveConfirmed}
        onCancel={() => setConfirmApprove(false)}
      />

      <ConfirmDialog
        open={confirmReject}
        title="Reject quotation?"
        message={`This will reject ${data.refNo} and notify the salesperson and accountants. Continue?`}
        confirmLabel="Reject"
        destructive
        isPending={reject.isPending}
        onConfirm={handleRejectConfirmed}
        onCancel={() => setConfirmReject(false)}
      />

      <CustomerHistoryModal
        open={historyOpen}
        onClose={() => setHistoryOpen(false)}
        requisitionId={requisitionId}
      />
    </div>
  );
}
