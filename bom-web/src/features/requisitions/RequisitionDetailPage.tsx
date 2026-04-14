import { Link, useNavigate, useParams } from "react-router-dom";
import { ArrowLeft } from "lucide-react";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/Card";
import { Button } from "@/components/ui/Button";
import { StatusBadge } from "@/components/ui/StatusBadge";
import { RequisitionTimeline } from "./components/RequisitionTimeline";
import { useRequisition } from "./requisitionsApi";
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
  if (role === "BomCreator" && status === "BomPending") {
    return { label: "Start BOM", path: "bom" };
  }
  if (role === "BomCreator" && status === "BomInProgress") {
    return { label: "Continue BOM", path: "bom" };
  }
  if (role === "Accountant" && status === "CostingPending") {
    return { label: "Start Costing", path: "costing" };
  }
  if (role === "Accountant" && status === "CostingInProgress") {
    return { label: "Continue Costing", path: "costing" };
  }
  if (role === "ManagingDirector" && status === "MdReview") {
    return { label: "Review & Approve", path: "approval" };
  }
  return null;
}

export default function RequisitionDetailPage() {
  const { id } = useParams<{ id: string }>();
  const numericId = Number(id);
  const { data, isLoading, error } = useRequisition(numericId);
  const role = useAuthStore((s) => s.user?.role);
  const navigate = useNavigate();

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
            {r.itemDescription} — {r.customerName}
          </p>
        </div>
        {action && (
          <Button
            onClick={() => navigate(`/requisitions/${id}/${action.path}`)}
          >
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
            <CardHeader><CardTitle>Customer</CardTitle></CardHeader>
            <CardContent>
              <LabeledValue label="Name" value={r.customerName} />
              <LabeledValue label="Email" value={r.customerEmail} />
              <LabeledValue label="Phone" value={r.customerPhone} />
              <LabeledValue label="Address" value={r.customerAddress} />
            </CardContent>
          </Card>

          <Card>
            <CardHeader><CardTitle>Quotation</CardTitle></CardHeader>
            <CardContent>
              <LabeledValue label="Expected Qty" value={`${r.expectedQty} ${r.currencyCode}`} />
              {r.exchangeRateSnapshot !== null && (
                <LabeledValue label="Exchange rate" value={r.exchangeRateSnapshot} />
              )}
              <LabeledValue label="Branch" value={r.branchName} />
              <LabeledValue label="Sales person" value={r.salesPersonName} />
            </CardContent>
          </Card>

          <Card>
            <CardHeader><CardTitle>BOM</CardTitle></CardHeader>
            <CardContent>
              {r.bom ? (
                <>
                  <LabeledValue label="Total cost / kg" value={r.bom.totalCostPerKg} />
                  <LabeledValue label="Has cost" value={r.bom.hasCost ? "Yes" : "No"} />
                </>
              ) : (
                <p className="text-sm text-muted-foreground">BOM not yet created.</p>
              )}
            </CardContent>
          </Card>

          <Card>
            <CardHeader><CardTitle>Approval</CardTitle></CardHeader>
            <CardContent>
              {r.approval ? (
                <>
                  <LabeledValue label="Sales price (AED)" value={r.approval.salesPriceAed} />
                  {r.approval.salesPriceForeign !== null && (
                    <LabeledValue label="Sales price (foreign)" value={r.approval.salesPriceForeign} />
                  )}
                  <LabeledValue label="Profit margin" value={`${r.approval.profitMarginPct}%`} />
                  <LabeledValue label="Approved" value={r.approval.isApproved ? "Yes" : "No"} />
                </>
              ) : (
                <p className="text-sm text-muted-foreground">Not yet submitted for approval.</p>
              )}
            </CardContent>
          </Card>
        </div>
      </div>
    </div>
  );
}
