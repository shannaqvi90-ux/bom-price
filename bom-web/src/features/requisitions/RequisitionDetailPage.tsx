import { useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { ArrowLeft } from "lucide-react";
import { toast } from "sonner";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/Card";
import { Button } from "@/components/ui/Button";
import {
  useV3Requisition,
  useSubmitRequisition,
  useCancelRequisition,
} from "./requisitionsApi";
import { useAuthStore } from "@/store/authStore";
import { V3StatusBadge } from "@/components/v3/V3StatusBadge";
import { BomEditorTable } from "@/components/v3/BomEditorTable";
import { AdminActionsCard } from "@/features/admin/AdminActionsCard";
import { SignedQuotationViewer } from "./SignedQuotationViewer";
import { FinalPriceSummary } from "@/features/approvals/FinalPriceSummary";
import { api } from "@/api/axios";
import type { V3FinishedGood, V3RequisitionStatus } from "@/types/api";

function LabeledValue({ label, value }: { label: string; value: string | number | null | undefined }) {
  return (
    <div className="flex justify-between gap-4 py-1 text-sm">
      <span className="text-muted-foreground">{label}</span>
      <span className="text-right">{value ?? "—"}</span>
    </div>
  );
}

export default function RequisitionDetailPage() {
  const { id } = useParams<{ id: string }>();
  const numericId = Number(id);
  const navigate = useNavigate();
  const user = useAuthStore((s) => s.user);
  const role = user?.role;

  const { data: req, isLoading, error, refetch } = useV3Requisition(numericId);

  const submit = useSubmitRequisition();
  const cancel = useCancelRequisition();

  const [showCancelInput, setShowCancelInput] = useState(false);
  const [cancelReason, setCancelReason] = useState("");

  const httpStatus = (error as { response?: { status?: number } } | null)?.response?.status;

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
          Failed to load requisition.
        </CardContent>
      </Card>
    );
  }

  if (isLoading || !req) {
    return <p className="text-sm text-muted-foreground">Loading…</p>;
  }

  const status = req.status as V3RequisitionStatus;
  const isOwnSalesPerson = role === "SalesPerson" && req.salesPerson.id === user?.userId;
  const isAdmin = role === "Admin";

  async function onSubmit() {
    try {
      await submit.mutateAsync(numericId);
      toast.success("Submitted to costing");
      refetch();
    } catch (err: unknown) {
      const message =
        (err as { response?: { data?: { error?: string } } } | null)?.response?.data?.error
        ?? "Failed to submit";
      toast.error(message);
    }
  }

  async function onCancel() {
    if (cancelReason.trim().length < 5) {
      toast.error("Reason must be ≥ 5 chars");
      return;
    }
    try {
      await cancel.mutateAsync({ id: numericId, reason: cancelReason });
      toast.success("Cancelled");
      refetch();
      setShowCancelInput(false);
    } catch (err: unknown) {
      const message =
        (err as { response?: { data?: { error?: string } } } | null)?.response?.data?.error
        ?? "Failed to cancel";
      toast.error(message);
    }
  }

  async function onDownloadPdf() {
    try {
      const response = await api.get(`/approvals/${numericId}/pdf`, {
        responseType: "blob",
      });
      const url = URL.createObjectURL(
        new Blob([response.data], { type: "application/pdf" }),
      );
      const a = document.createElement("a");
      a.href = url;
      a.download = `${req!.refNo}-Quotation.pdf`;
      a.click();
      URL.revokeObjectURL(url);
    } catch {
      toast.error("Failed to download PDF.");
    }
  }

  return (
    <div className="space-y-6">
      <Link to="/requisitions" className="inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground">
        <ArrowLeft className="h-4 w-4" /> Back to Requisitions
      </Link>

      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <div className="flex items-center gap-3">
            <h1 className="font-mono text-2xl font-semibold">{req.refNo}</h1>
            <V3StatusBadge status={req.status} />
          </div>
          <p className="mt-1 text-sm text-muted-foreground">
            {req.finishedGoods.length}{" "}
            {req.finishedGoods.length === 1 ? "finished good" : "finished goods"} — {req.customer.name}
          </p>
        </div>

        {/* V3 status-aware action buttons */}
        <div className="flex flex-wrap gap-2">
          {status === "Draft" && (isOwnSalesPerson || isAdmin) && (
            <>
              <Button onClick={onSubmit} disabled={submit.isPending}>
                Submit
              </Button>
              <Button
                variant="outline"
                onClick={() => setShowCancelInput(true)}
              >
                Cancel
              </Button>
            </>
          )}
          {status === "Costing" && (role === "Accountant" || isAdmin) && (
            <Button onClick={() => navigate(`/requisitions/${numericId}/costing`)}>
              Edit BOM &amp; Costing
            </Button>
          )}
          {status === "MdPricing" && (role === "ManagingDirector" || isAdmin) && (
            <Button onClick={() => navigate(`/approvals/${numericId}/margin`)}>
              Set Margin
            </Button>
          )}
          {status === "CustomerConfirm" && (isOwnSalesPerson || isAdmin) && (
            <Button onClick={() => navigate(`/requisitions/${numericId}/customer-confirm`)}>
              Confirm with Customer
            </Button>
          )}
          {status === "MdFinalSign" && (role === "ManagingDirector" || isAdmin) && (
            <Button onClick={() => navigate(`/approvals/${numericId}/final`)}>
              Sign Final
            </Button>
          )}
          {status === "Signed" && (
            <Button onClick={onDownloadPdf}>Download PDF</Button>
          )}
        </div>
      </div>

      <StatusBanner status={status} req={req} />

      {showCancelInput && (
        <Card>
          <CardContent className="py-4">
            <label className="block">
              <span className="text-sm font-medium text-destructive">Cancel reason (≥5 chars)</span>
              <input
                value={cancelReason}
                onChange={(e) => setCancelReason(e.target.value)}
                className="mt-1 w-full rounded-md border border-input px-3 py-2 text-sm"
              />
            </label>
            <div className="mt-3 flex gap-2">
              <Button onClick={onCancel} disabled={cancel.isPending}>
                Confirm Cancel
              </Button>
              <Button variant="outline" onClick={() => setShowCancelInput(false)}>
                Back
              </Button>
            </div>
          </CardContent>
        </Card>
      )}

      <div className="grid grid-cols-1 gap-6 lg:grid-cols-[1fr,1fr]">
        <Card>
          <CardHeader>
            <CardTitle>Customer</CardTitle>
          </CardHeader>
          <CardContent>
            <LabeledValue label="Name" value={req.customer.name} />
            <LabeledValue label="Code" value={req.customer.code} />
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>Quotation</CardTitle>
          </CardHeader>
          <CardContent>
            <LabeledValue label="Currency" value={req.currencyCode} />
            <LabeledValue label="Sales person" value={req.salesPerson.name} />
            {req.notes && <LabeledValue label="Notes" value={req.notes} />}
          </CardContent>
        </Card>
      </div>

      <Card>
        <CardHeader>
          <CardTitle>Finished Goods</CardTitle>
        </CardHeader>
        <CardContent>
          {req.finishedGoods.length === 0 ? (
            <p className="text-sm text-muted-foreground">No finished goods.</p>
          ) : (
            <div className="space-y-4">
              {req.finishedGoods.map((fg) => {
                const lines = (fg.bomLines ?? []).map((bl) => ({
                  itemId: bl.item.id,
                  qtyPerKg: bl.qtyPerKg,
                  micron: bl.micron,
                  processId: 1,
                }));
                const editedByAccountant = (fg.bomLines ?? []).some(
                  (bl) => bl.lastModifiedByUserId != null,
                );
                // Show cost-aware BOM only for accountant/MD/admin once costing
                // has run. Salespeople intentionally see the lite (no-cost) view.
                const showCostBom =
                  (role === "Accountant" ||
                    role === "ManagingDirector" ||
                    role === "Admin") &&
                  fg.costs != null &&
                  fg.bomLines != null &&
                  fg.bomLines.length > 0;
                return (
                  <div key={fg.id} className="rounded-lg border border-border p-4">
                    <div className="flex items-baseline justify-between">
                      <h3 className="font-medium text-foreground">
                        {fg.item.code} · {fg.item.description}
                      </h3>
                      <span className="text-sm text-muted-foreground">
                        {fg.expectedQty.toLocaleString()} KG
                        {fg.hasPrinting ? " · Printed" : ""}
                      </span>
                    </div>
                    {showCostBom ? (
                      <div className="mt-3">
                        <CostAwareBomTable fg={fg} currencyCode={req.currencyCode} />
                        {editedByAccountant && (
                          <p className="mt-2 text-xs text-amber-700">
                            ⚠ Edited by accountant after sales submitted.
                          </p>
                        )}
                      </div>
                    ) : fg.bomLines && fg.bomLines.length > 0 ? (
                      <div className="mt-3">
                        <BomEditorTable lines={lines} readOnly />
                        {editedByAccountant && (
                          <p className="mt-2 text-xs text-amber-700">
                            ⚠ Edited by accountant after sales submitted.
                          </p>
                        )}
                      </div>
                    ) : (
                      <p className="mt-2 text-sm text-muted-foreground">
                        No BOM yet.
                      </p>
                    )}
                  </div>
                );
              })}
            </div>
          )}
        </CardContent>
      </Card>

      {req.finalPrice ? (
        <FinalPriceSummary
          finalPrice={req.finalPrice}
          previewMode={status !== "Signed"}
        />
      ) : null}

      {status === "Signed" ? (
        <SignedQuotationViewer requisitionId={numericId} refNo={req.refNo} />
      ) : null}

      <AdminActionsCard requisition={{ id: req.id, refNo: req.refNo, status: req.status }} />
    </div>
  );
}

interface StatusBannerProps {
  status: V3RequisitionStatus;
  req: {
    cancelReason?: string | null;
  };
}

function StatusBanner({ status, req }: StatusBannerProps) {
  if (status === "Costing" || status === "Draft") {
    return (
      <div className="rounded-lg border border-blue-200 bg-blue-50 px-4 py-3 text-sm text-blue-900">
        Waiting on accountant costing.
      </div>
    );
  }
  if (status === "CustomerConfirm") {
    return (
      <div className="rounded-lg border border-blue-200 bg-blue-50 px-4 py-3 text-sm text-blue-900">
        Waiting on sales person to confirm with customer.
      </div>
    );
  }
  if (status === "Rejected") {
    return (
      <div className="rounded-lg border border-red-200 bg-red-50 px-4 py-3">
        <div className="text-sm font-semibold text-red-900">Rejected</div>
        <div className="mt-1 text-sm text-red-900">
          Reason: {req.cancelReason ?? "(no reason recorded)"}
        </div>
      </div>
    );
  }
  if (status === "Cancelled") {
    return (
      <div className="rounded-lg border border-border bg-muted px-4 py-3">
        <div className="text-sm font-semibold text-foreground">Cancelled</div>
        <div className="mt-1 text-sm text-foreground">
          Reason: {req.cancelReason ?? "(no reason recorded)"}
        </div>
      </div>
    );
  }
  return null;
}

// Detailed BOM + cost breakdown shown to Accountant/MD/Admin once costing has
// run. Mirrors the structure used in MdFgPricingCard's BOM details so the same
// numbers the MD priced against are surfaced on the read-only detail page.
function CostAwareBomTable({
  fg,
  currencyCode,
}: {
  fg: V3FinishedGood;
  currencyCode: string;
}) {
  const costs = fg.costs!;
  const lines = fg.bomLines ?? [];
  return (
    <div className="rounded-md border border-border bg-muted px-3 py-2">
      <table className="w-full text-xs">
        <thead className="text-muted-foreground">
          <tr>
            <th className="px-1 py-1 text-left font-medium">Raw Material</th>
            <th className="px-1 py-1 text-right font-medium">Qty/KG</th>
            <th className="px-1 py-1 text-right font-medium">Wastage %</th>
            <th className="px-1 py-1 text-right font-medium">Cost/KG</th>
            <th className="px-1 py-1 text-right font-medium">Total/KG</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-border">
          {lines.map((line) => {
            const lineCost = costs.lines.find((l) => l.bomLineId === line.id);
            const wastageMult =
              lineCost?.wastagePercent != null
                ? 1 + lineCost.wastagePercent / 100
                : 1;
            const lineTotal =
              lineCost?.purchaseValuePerKg != null
                ? lineCost.purchaseValuePerKg * line.qtyPerKg * wastageMult
                : null;
            return (
              <tr key={line.id}>
                <td className="px-1 py-1 text-foreground">{line.item.description}</td>
                <td className="px-1 py-1 text-right text-foreground">
                  {line.qtyPerKg.toFixed(4)}
                </td>
                <td className="px-1 py-1 text-right text-foreground">
                  {lineCost?.wastagePercent != null
                    ? `${lineCost.wastagePercent.toFixed(2)}%`
                    : "—"}
                </td>
                <td className="px-1 py-1 text-right text-foreground">
                  {lineCost?.purchaseValuePerKg != null
                    ? `${lineCost.purchaseCurrency ?? ""} ${lineCost.purchaseValuePerKg.toFixed(2)}`
                    : "—"}
                </td>
                <td className="px-1 py-1 text-right font-medium text-foreground">
                  {lineTotal != null
                    ? `${lineCost?.purchaseCurrency ?? currencyCode} ${lineTotal.toFixed(2)}`
                    : "—"}
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
      <div className="mt-2 grid grid-cols-2 gap-x-4 gap-y-1 border-t border-border pt-2 text-xs text-muted-foreground">
        {costs.printingCostPerKg != null ? (
          <div className="flex justify-between">
            <span>Printing/KG</span>
            <span className="font-medium">
              {costs.printingCostCurrency ?? currencyCode}{" "}
              {costs.printingCostPerKg.toFixed(2)}
            </span>
          </div>
        ) : null}
        <div className="flex justify-between">
          <span>FOH/KG</span>
          <span className="font-medium">
            {currencyCode} {costs.fohPerKg.toFixed(2)}
          </span>
        </div>
        <div className="flex justify-between">
          <span>Transport/KG</span>
          <span className="font-medium">
            {currencyCode} {costs.transportPerKg.toFixed(2)}
          </span>
        </div>
        <div className="flex justify-between">
          <span>Commission/KG</span>
          <span className="font-medium">
            {currencyCode} {costs.commissionPerKg.toFixed(2)}
          </span>
        </div>
      </div>
      <div className="mt-2 flex justify-between border-t border-border pt-2 text-sm">
        <span className="font-semibold text-foreground">Total Cost/KG</span>
        <span className="font-bold text-foreground">
          {currencyCode} {costs.totalCostPerKg.toFixed(2)}
        </span>
      </div>
    </div>
  );
}
