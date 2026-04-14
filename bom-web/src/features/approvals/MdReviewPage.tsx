import { Link, useNavigate, useParams } from "react-router-dom";
import { ArrowLeft } from "lucide-react";
import { useState } from "react";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/Card";
import { Button } from "@/components/ui/Button";
import { Dialog } from "@/components/ui/Dialog";
import { api } from "@/api/axios";
import { useBom } from "@/features/bom/bomApi";
import {
  useMdReview,
  useApproveRequisition,
  useRejectRequisition,
} from "./approvalsApi";

type PageState =
  | { kind: "reviewing" }
  | { kind: "approved"; salesPrice: number; marginPct: number };

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
  const [salesPriceInput, setSalesPriceInput] = useState("");
  const [notes, setNotes] = useState("");
  const [validationError, setValidationError] = useState<string | null>(null);
  const [showBom, setShowBom] = useState(false);

  const httpStatus = (error as { response?: { status?: number } } | null)
    ?.response?.status;

  if (httpStatus === 404) {
    return (
      <Card className="mx-auto max-w-lg">
        <CardContent className="py-8 text-center">
          <p className="text-sm">Requisition not found or not ready for review.</p>
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

  const salesPrice = Number(salesPriceInput);
  const hasValidPrice = Number.isFinite(salesPrice) && salesPrice > 0;
  const marginPct =
    hasValidPrice && salesPrice > 0
      ? ((salesPrice - data.totalCostPerKg) / salesPrice) * 100
      : 0;
  const marginIsPositive = marginPct > 0;

  async function handleApprove() {
    setValidationError(null);
    if (!hasValidPrice) {
      setValidationError("Enter a sales price greater than zero.");
      return;
    }
    try {
      await approve.mutateAsync({
        requisitionId,
        payload: { salesPricePerKgAed: salesPrice, notes: notes || undefined },
      });
      setPageState({ kind: "approved", salesPrice, marginPct });
    } catch (e) {
      const msg =
        (e as { response?: { data?: { message?: string } } })?.response?.data
          ?.message ?? "Failed to approve.";
      setValidationError(msg);
    }
  }

  async function handleReject() {
    setValidationError(null);
    if (notes.trim().length === 0) {
      setValidationError("Notes are required when rejecting.");
      return;
    }
    try {
      await reject.mutateAsync({
        requisitionId,
        payload: { notes: notes.trim() },
      });
      navigate(`/requisitions/${requisitionId}`);
    } catch (e) {
      const msg =
        (e as { response?: { data?: { message?: string } } })?.response?.data
          ?.message ?? "Failed to reject.";
      setValidationError(msg);
    }
  }

  async function handleDownloadPdf() {
    const response = await api.get(`/approvals/${requisitionId}/pdf`, {
      responseType: "blob",
    });
    const url = URL.createObjectURL(new Blob([response.data], { type: "application/pdf" }));
    const a = document.createElement("a");
    a.href = url;
    a.download = `${data.refNo}-Quotation.pdf`;
    a.click();
    URL.revokeObjectURL(url);
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
            {data.itemDescription} — {data.customerName}
          </p>
        </div>
        <Button variant="outline" onClick={() => setShowBom(true)}>
          View BOM
        </Button>
      </div>

      <div className="grid grid-cols-1 gap-6 lg:grid-cols-2">
        <Card>
          <CardHeader>
            <CardTitle>Cost Breakdown</CardTitle>
          </CardHeader>
          <CardContent>
            <LabeledRow
              label="Raw Material"
              value={`${data.rawMaterialCostPerKg.toFixed(4)} AED/kg`}
            />
            <LabeledRow
              label="Landed Cost"
              value={`${data.landedCostPerKg.toFixed(4)} AED/kg`}
            />
            <LabeledRow label="FOH" value={`${data.fohPerKg.toFixed(4)} AED/kg`} />
            <div className="mt-2 flex justify-between gap-4 border-t pt-2 text-sm font-semibold">
              <span>Total Cost/kg</span>
              <span className="font-mono">{data.totalCostPerKg.toFixed(4)} AED</span>
            </div>
            <div className="mt-4 space-y-1 text-xs text-muted-foreground">
              <div>Expected qty: {data.expectedQty} kg</div>
              <div>Currency: {data.currencyCode}</div>
              {data.exchangeRate !== null && (
                <div>Exchange rate: {data.exchangeRate}</div>
              )}
            </div>
          </CardContent>
        </Card>

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
                  Quotation approved ✓
                </div>
                <LabeledRow
                  label="Sales Price"
                  value={`${pageState.salesPrice.toFixed(4)} AED/kg`}
                />
                <LabeledRow
                  label="Profit Margin"
                  value={`${pageState.marginPct.toFixed(2)}%`}
                />
                <Button onClick={handleDownloadPdf} className="w-full">
                  Download PDF
                </Button>
              </div>
            ) : (
              <div className="space-y-4">
                <div>
                  <label
                    className="mb-1 block text-xs text-muted-foreground"
                    htmlFor="sales-price"
                  >
                    Sales Price (AED/kg)
                  </label>
                  <input
                    id="sales-price"
                    type="number"
                    step="0.0001"
                    min="0"
                    value={salesPriceInput}
                    onChange={(e) => setSalesPriceInput(e.target.value)}
                    className="w-full rounded-md border px-3 py-2 text-sm"
                    placeholder="0.0000"
                  />
                </div>

                {hasValidPrice && (
                  <div
                    data-testid="margin-pill"
                    className={`rounded-md p-3 text-center text-sm font-semibold ${
                      marginIsPositive
                        ? "bg-green-50 text-green-800"
                        : "bg-red-50 text-red-800"
                    }`}
                  >
                    Profit Margin: {marginPct.toFixed(2)}%
                  </div>
                )}

                <div>
                  <label
                    className="mb-1 block text-xs text-muted-foreground"
                    htmlFor="notes"
                  >
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

                {validationError && (
                  <p className="text-sm text-destructive">{validationError}</p>
                )}

                <div className="flex gap-2">
                  <Button
                    onClick={handleApprove}
                    disabled={approve.isPending || !hasValidPrice}
                    className="flex-1 bg-green-700 hover:bg-green-800"
                  >
                    {approve.isPending ? "Approving…" : "Approve"}
                  </Button>
                  <Button
                    onClick={handleReject}
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
      </div>

      <Dialog
        open={showBom}
        onClose={() => setShowBom(false)}
        title="Bill of Materials"
        className="max-w-2xl"
      >
        {bom ? (
          <div className="space-y-4 text-sm">
            {Array.from(new Set(bom.lines.map((l) => l.processName))).map((proc) => (
              <div key={proc}>
                <p className="mb-1 font-semibold text-muted-foreground">{proc}</p>
                <table className="w-full text-xs">
                  <thead>
                    <tr className="border-b text-left text-muted-foreground">
                      <th className="pb-1 font-medium">Material</th>
                      <th className="pb-1 text-right font-medium">Qty/kg</th>
                      <th className="pb-1 text-right font-medium">Wastage%</th>
                    </tr>
                  </thead>
                  <tbody>
                    {bom.lines
                      .filter((l) => l.processName === proc)
                      .map((l) => (
                        <tr key={l.id} className="border-b last:border-0">
                          <td className="py-1">{l.rawMaterialDescription}</td>
                          <td className="py-1 text-right font-mono">{l.qtyPerKg.toFixed(4)}</td>
                          <td className="py-1 text-right font-mono">{l.wastagePct.toFixed(2)}%</td>
                        </tr>
                      ))}
                  </tbody>
                </table>
              </div>
            ))}
            <div className="border-t pt-2 text-right font-semibold">
              Total Cost/kg: <span className="font-mono">{bom.totalCostPerKg.toFixed(4)} AED</span>
            </div>
          </div>
        ) : (
          <p className="text-sm text-muted-foreground">Loading BOM…</p>
        )}
      </Dialog>
    </div>
  );
}
