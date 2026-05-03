import { useMemo } from "react";
import { Link, useNavigate } from "react-router-dom";
import type { ColumnDef } from "@tanstack/react-table";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/Card";
import { DataTable } from "@/components/ui/DataTable";
import { StatusBadge } from "@/components/ui/StatusBadge";
import { useRequisitions } from "@/features/requisitions/requisitionsApi";
import { useOwnSignatureBlobUrl } from "@/features/profile/profileApi";
import { useMdDashboardCounts } from "@/api/stats";
import { formatRelative } from "@/utils/date";
import type { RequisitionListItem } from "@/types/api";

const columns: ColumnDef<RequisitionListItem>[] = [
  { accessorKey: "refNo", header: "Ref No" },
  {
    accessorKey: "status",
    header: "Status",
    cell: (info) => <StatusBadge status={info.getValue() as RequisitionListItem["status"]} />,
  },
  { accessorKey: "customerName", header: "Customer" },
  {
    accessorKey: "createdAt",
    header: "Created",
    cell: (info) => formatRelative(info.getValue() as string),
  },
];

export default function MdDashboard() {
  const navigate = useNavigate();
  const { data, isLoading } = useRequisitions();
  const { data: signatureUrl, isLoading: sigLoading } = useOwnSignatureBlobUrl();
  const counts = useMdDashboardCounts();

  const startOfMonth = useMemo(() => {
    const d = new Date();
    return new Date(d.getFullYear(), d.getMonth(), 1).toISOString();
  }, []);

  const awaitingPricing = useMemo(
    () => (data ?? []).filter((r) => r.status === "MdPricing"),
    [data],
  );
  const awaitingFinalSign = useMemo(
    () => (data ?? []).filter((r) => r.status === "MdFinalSign"),
    [data],
  );
  const customerConfirm = useMemo(
    () => (data ?? []).filter((r) => r.status === "CustomerConfirm"),
    [data],
  );
  const signedThisMonth = useMemo(
    () =>
      (data ?? []).filter((r) => r.status === "Signed" && r.createdAt >= startOfMonth),
    [data, startOfMonth],
  );

  return (
    <div className="space-y-4">
      <h1 className="text-2xl font-semibold tracking-tight">MD Dashboard</h1>

      {/* Signature missing — surface ASAP so MD doesn't hit it at sign-time */}
      {!sigLoading && !signatureUrl ? (
        <div className="rounded-lg border border-yellow-300 bg-yellow-50 px-4 py-3">
          <div className="font-semibold text-yellow-900">⚠️ No signature uploaded</div>
          <div className="mt-1 text-sm text-yellow-900">
            Final-sign will be blocked until you upload your signature.{" "}
            <Link to="/profile/signature" className="font-semibold underline">
              Upload now →
            </Link>
          </div>
        </div>
      ) : null}

      {/* KPI tiles fed by /api/stats/v3-dashboard */}
      <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
        <Kpi
          label="TO PRICE"
          value={counts.data?.toPrice}
          loading={counts.isLoading}
          onClick={() => navigate("/md/list?tab=queue")}
          accent="blue"
        />
        <Kpi
          label="TO SIGN"
          value={counts.data?.toSign}
          loading={counts.isLoading}
          onClick={() => navigate("/md/list?tab=queue")}
          accent="orange"
        />
        <Kpi
          label="IN FLIGHT"
          value={counts.data?.inFlight}
          loading={counts.isLoading}
          onClick={() => navigate("/md/list?tab=in-flight")}
          accent="slate"
        />
        <Kpi
          label="SIGNED TODAY"
          value={counts.data?.signedToday}
          loading={counts.isLoading}
          onClick={() => navigate("/md/list?tab=signed")}
          accent="green"
        />
      </div>

      {/* Drill-down tables (kept for at-a-glance triage) */}
      <Card>
        <CardHeader>
          <CardTitle>Awaiting Pricing ({awaitingPricing.length})</CardTitle>
        </CardHeader>
        <CardContent>
          <DataTable
            columns={columns}
            data={awaitingPricing}
            isLoading={isLoading}
            emptyState={<p>No requisitions awaiting pricing.</p>}
            onRowClick={(row) => navigate(`/requisitions/${row.id}`)}
          />
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Customer Confirm ({customerConfirm.length})</CardTitle>
        </CardHeader>
        <CardContent>
          <DataTable
            columns={columns}
            data={customerConfirm}
            isLoading={isLoading}
            emptyState={<p>No requisitions awaiting customer confirmation.</p>}
            onRowClick={(row) => navigate(`/requisitions/${row.id}`)}
          />
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Awaiting Final Sign ({awaitingFinalSign.length})</CardTitle>
        </CardHeader>
        <CardContent>
          <DataTable
            columns={columns}
            data={awaitingFinalSign}
            isLoading={isLoading}
            emptyState={<p>No requisitions awaiting final sign.</p>}
            onRowClick={(row) => navigate(`/requisitions/${row.id}`)}
          />
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Signed this month ({signedThisMonth.length})</CardTitle>
        </CardHeader>
        <CardContent>
          <DataTable
            columns={columns}
            data={signedThisMonth}
            isLoading={isLoading}
            emptyState={<p>Nothing signed this month yet.</p>}
            onRowClick={(row) => navigate(`/requisitions/${row.id}`)}
          />
        </CardContent>
      </Card>
    </div>
  );
}

interface KpiProps {
  label: string;
  value: number | undefined;
  loading: boolean;
  onClick: () => void;
  accent: "blue" | "orange" | "slate" | "green";
}

const accentMap = {
  blue: "bg-blue-50 border-blue-200 text-blue-900",
  orange: "bg-orange-50 border-orange-200 text-orange-900",
  slate: "bg-muted border-border text-foreground",
  green: "bg-emerald-50 border-emerald-200 text-emerald-900",
} as const;

function Kpi({ label, value, loading, onClick, accent }: KpiProps) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={`rounded-lg border px-4 py-3 text-left transition hover:brightness-95 ${accentMap[accent]}`}
    >
      <div className="text-xs font-bold uppercase tracking-wider opacity-80">{label}</div>
      <div className="mt-1 text-3xl font-bold">
        {loading ? <span className="opacity-30">—</span> : (value ?? 0)}
      </div>
    </button>
  );
}
