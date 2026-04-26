import { useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { ArrowLeft } from "lucide-react";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/Card";
import { Button } from "@/components/ui/Button";
import { StatusBadge } from "@/components/ui/StatusBadge";
import { RequisitionTimeline } from "./components/RequisitionTimeline";
import { useRequisition, useCustomerChangeHistory, useBranchChangeHistory } from "./requisitionsApi";
import { CustomerHistoryModal } from "./CustomerHistoryModal";
import { BranchSwapModal } from "./BranchSwapModal";
import { BranchChangeHistoryModal } from "./BranchChangeHistoryModal";
import { useAuthStore } from "@/store/authStore";
import { formatRelative } from "@/utils/date";
import type { RequisitionDetail, RequisitionStatus, UserRole } from "@/types/api";

function LabeledValue({ label, value }: { label: string; value: string | number | null | undefined }) {
  return (
    <div className="flex justify-between gap-4 py-1 text-sm">
      <span className="text-muted-foreground">{label}</span>
      <span className="text-right">{value ?? "—"}</span>
    </div>
  );
}

function actionButtonFor(
  role: UserRole | undefined,
  status: RequisitionStatus,
): { label: string; path: string } | null {
  if (role === "BomCreator" && status === "BomPending")
    return { label: "Start BOM", path: "bom" };
  if (role === "BomCreator" && status === "BomInProgress")
    return { label: "Continue BOM", path: "bom" };
  if (role === "Accountant" && (status === "CostingPending" || status === "CostingInProgress"))
    return { label: status === "CostingPending" ? "Start Costing" : "Continue Costing", path: "costing" };
  if (role === "ManagingDirector" && status === "MdReview")
    return { label: "Review & Approve", path: "approval" };
  if (role === "SalesPerson" && status === "Rejected")
    return { label: "Edit & Resubmit", path: "edit" };
  return null;
}

export default function RequisitionDetailPage() {
  const { id } = useParams<{ id: string }>();
  const numericId = Number(id);
  const { data, isLoading, error } = useRequisition(numericId);
  const role = useAuthStore((s) => s.user?.role);
  const navigate = useNavigate();
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

  if (isLoading || !data) {
    return <p className="text-sm text-muted-foreground">Loading…</p>;
  }

  const r: RequisitionDetail = data;
  const action = actionButtonFor(role, r.status);
  const canChangeBranch =
    (role === "Accountant" || role === "Admin") &&
    (r.status === "BomPending" || r.status === "BomInProgress" || r.status === "CostingPending");

  return (
    <div className="space-y-6">
      <Link to="/requisitions" className="inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground">
        <ArrowLeft className="h-4 w-4" /> Back to Requisitions
      </Link>

      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <div className="flex items-center gap-3">
            <h1 className="font-mono text-2xl font-semibold">{r.refNo}</h1>
            <StatusBadge status={r.status} />
            <span className="text-xs text-muted-foreground">{formatRelative(r.createdAt)}</span>
          </div>
          <p className="mt-1 text-sm text-muted-foreground">
            {r.items.length} {r.items.length === 1 ? "item" : "items"} — {r.customerName}
          </p>
        </div>
        {action && (
          <Button onClick={() => navigate(`/requisitions/${id}/${action.path}`)}>
            {action.label}
          </Button>
        )}
      </div>

      <div className="grid grid-cols-1 gap-6 lg:grid-cols-[1fr,1fr]">
        <Card>
          <CardHeader>
            <CardTitle>Progress</CardTitle>
          </CardHeader>
          <CardContent>
            <RequisitionTimeline
              status={r.status as Exclude<RequisitionStatus, "Draft">}
              createdAt={r.createdAt}
              updatedAt={r.updatedAt}
            />
          </CardContent>
        </Card>

        <div className="space-y-4">
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
              <LabeledValue label="Name" value={r.customerName} />
              <LabeledValue label="Email" value={r.customerEmail} />
              <LabeledValue label="Phone" value={r.customerPhone} />
              <LabeledValue label="Address" value={r.customerAddress} />
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
              <LabeledValue label="Currency" value={r.currencyCode} />
              {r.exchangeRateSnapshot !== null && (
                <LabeledValue label="Exchange rate" value={r.exchangeRateSnapshot} />
              )}
              <LabeledValue label="Branch" value={r.branchName} />
              <LabeledValue label="Sales person" value={r.salesPersonName} />
            </CardContent>
          </Card>

          <Card>
            <CardHeader><CardTitle>Approval</CardTitle></CardHeader>
            <CardContent>
              {r.approval ? (
                <>
                  <LabeledValue
                    label={r.approval.isApproved ? "Approved" : "Rejected"}
                    value={formatRelative(r.approval.approvedAt)}
                  />
                  {r.approval.notes && (
                    <div className={`mt-2 text-sm ${r.approval.isApproved ? "" : "text-destructive"}`}>
                      <p className="font-medium">
                        {r.approval.isApproved ? "Notes" : "Rejection reason"}
                      </p>
                      <p className="mt-1 whitespace-pre-wrap">{r.approval.notes}</p>
                    </div>
                  )}
                </>
              ) : (
                <p className="text-sm text-muted-foreground">Not yet submitted for approval.</p>
              )}
            </CardContent>
          </Card>
        </div>
      </div>

      <Card>
        <CardHeader><CardTitle>Items</CardTitle></CardHeader>
        <CardContent>
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b text-left text-muted-foreground">
                <th className="pb-2 font-medium">#</th>
                <th className="pb-2 font-medium">Item</th>
                <th className="pb-2 text-right font-medium">Expected Qty</th>
              </tr>
            </thead>
            <tbody>
              {r.items.map((ri, i) => (
                <tr key={ri.id} className="border-b last:border-0">
                  <td className="py-2">{i + 1}</td>
                  <td className="py-2">{ri.itemDescription}</td>
                  <td className="py-2 text-right font-mono">{ri.expectedQty.toLocaleString()} kg</td>
                </tr>
              ))}
            </tbody>
          </table>
        </CardContent>
      </Card>

      <CustomerHistoryModal
        open={historyOpen}
        onClose={() => setHistoryOpen(false)}
        requisitionId={numericId}
      />
      <BranchSwapModal
        requisitionId={r.id}
        currentBranchId={r.branchId}
        open={branchSwapOpen}
        onClose={() => setBranchSwapOpen(false)}
      />
      <BranchChangeHistoryModal
        requisitionId={r.id}
        open={branchHistoryOpen}
        onClose={() => setBranchHistoryOpen(false)}
      />
    </div>
  );
}
