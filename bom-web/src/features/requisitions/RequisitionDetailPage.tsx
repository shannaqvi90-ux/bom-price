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
  useCustomerChangeHistory,
  useBranchChangeHistory,
} from "./requisitionsApi";
import { CustomerHistoryModal } from "./CustomerHistoryModal";
import { BranchSwapModal } from "./BranchSwapModal";
import { BranchChangeHistoryModal } from "./BranchChangeHistoryModal";
import { useAuthStore } from "@/store/authStore";
import { V3StatusBadge } from "@/components/v3/V3StatusBadge";
import { BomEditorTable } from "@/components/v3/BomEditorTable";
import { AdminActionsCard } from "@/features/admin/AdminActionsCard";
import { api } from "@/api/axios";
import type { V3RequisitionStatus } from "@/types/api";

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

  // V2.3 modals + history (preserved for Task 20 cleanup).
  const [historyOpen, setHistoryOpen] = useState(false);
  const historyQ = useCustomerChangeHistory(numericId, true);
  const historyCount = historyQ.data?.length ?? 0;
  const [branchSwapOpen, setBranchSwapOpen] = useState(false);
  const [branchHistoryOpen, setBranchHistoryOpen] = useState(false);
  const branchHistQ = useBranchChangeHistory(numericId, true);
  const branchChangeCount = branchHistQ.data?.length ?? 0;

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

  // V2.3 "Change branch" trigger guard preserved (Task 20 cleanup target).
  const canChangeBranch =
    (role === "Accountant" || role === "Admin") &&
    (status === "Costing");

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
            <CardTitle className="flex items-center gap-2">
              Customer
              {historyCount > 0 && (
                <button
                  type="button"
                  onClick={() => setHistoryOpen(true)}
                  className="ml-2 inline-flex items-center rounded-full bg-amber-100 px-2 py-0.5 text-xs text-amber-800 hover:bg-amber-200"
                >
                  Customer changed ({historyCount})
                </button>
              )}
            </CardTitle>
          </CardHeader>
          <CardContent>
            <LabeledValue label="Name" value={req.customer.name} />
            <LabeledValue label="Code" value={req.customer.code} />
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2">
              Quotation
              {canChangeBranch && (
                <button
                  type="button"
                  onClick={() => setBranchSwapOpen(true)}
                  className="text-sm text-blue-700 underline ml-3"
                >
                  Change branch
                </button>
              )}
              {branchChangeCount > 0 && (
                <button
                  type="button"
                  onClick={() => setBranchHistoryOpen(true)}
                  className="ml-2 inline-flex items-center rounded-full bg-amber-100 px-2 py-0.5 text-xs text-amber-800 hover:bg-amber-200"
                >
                  Branch changed ({branchChangeCount})
                </button>
              )}
            </CardTitle>
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
                return (
                  <div key={fg.id} className="rounded-lg border border-gray-200 p-4">
                    <div className="flex items-baseline justify-between">
                      <h3 className="font-medium text-gray-900">
                        {fg.item.code} · {fg.item.description}
                      </h3>
                      <span className="text-sm text-gray-600">
                        {fg.expectedQty.toLocaleString()} KG
                        {fg.hasPrinting ? " · Printed" : ""}
                      </span>
                    </div>
                    {fg.bomLines && fg.bomLines.length > 0 ? (
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

      <AdminActionsCard requisition={{ id: req.id, refNo: req.refNo, status: req.status }} />

      {/* V2.3 modals retained — Task 20 will delete with their imports. */}
      <CustomerHistoryModal
        open={historyOpen}
        onClose={() => setHistoryOpen(false)}
        requisitionId={numericId}
      />
      <BranchSwapModal
        requisitionId={req.id}
        currentBranchId={0}
        open={branchSwapOpen}
        onClose={() => setBranchSwapOpen(false)}
      />
      <BranchChangeHistoryModal
        requisitionId={req.id}
        open={branchHistoryOpen}
        onClose={() => setBranchHistoryOpen(false)}
      />
    </div>
  );
}
