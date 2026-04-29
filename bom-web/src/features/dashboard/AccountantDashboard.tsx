import { useMemo } from "react";
import { useNavigate } from "react-router-dom";
import type { ColumnDef } from "@tanstack/react-table";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/Card";
import { DataTable } from "@/components/ui/DataTable";
import { StatusBadge } from "@/components/ui/StatusBadge";
import { useRequisitions } from "@/features/requisitions/requisitionsApi";
import { formatRelative } from "@/utils/date";
import type { RequisitionListItem, RequisitionStatus } from "@/types/api";

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

const POST_COSTING_STATUSES: RequisitionStatus[] = [
  "MdPricing",
  "CustomerConfirm",
  "MdFinalSign",
  "Signed",
];
const AWAITING_MD_STATUSES: RequisitionStatus[] = ["MdPricing", "MdFinalSign"];

export default function AccountantDashboard() {
  const navigate = useNavigate();
  const { data, isLoading } = useRequisitions();

  const startOfMonth = useMemo(() => {
    const d = new Date();
    return new Date(d.getFullYear(), d.getMonth(), 1).toISOString();
  }, []);

  const pendingCosting = useMemo(
    () => (data ?? []).filter((r) => r.status === "Costing"),
    [data],
  );
  const submittedThisMonth = useMemo(
    () =>
      (data ?? []).filter(
        (r) => POST_COSTING_STATUSES.includes(r.status) && r.createdAt >= startOfMonth,
      ),
    [data, startOfMonth],
  );
  const awaitingMd = useMemo(
    () => (data ?? []).filter((r) => AWAITING_MD_STATUSES.includes(r.status)),
    [data],
  );

  return (
    <div className="space-y-4">
      <h1 className="text-2xl font-semibold tracking-tight">Costing Dashboard</h1>

      <Card>
        <CardHeader>
          <CardTitle>Pending Costing ({pendingCosting.length})</CardTitle>
        </CardHeader>
        <CardContent>
          <DataTable
            columns={columns}
            data={pendingCosting}
            isLoading={isLoading}
            emptyState={<p>No requisitions waiting for costing.</p>}
            onRowClick={(row) => navigate(`/requisitions/${row.id}`)}
          />
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Submitted this month ({submittedThisMonth.length})</CardTitle>
        </CardHeader>
        <CardContent>
          <DataTable
            columns={columns}
            data={submittedThisMonth}
            isLoading={isLoading}
            emptyState={<p>Nothing submitted this month.</p>}
            onRowClick={(row) => navigate(`/requisitions/${row.id}`)}
          />
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Awaiting MD ({awaitingMd.length})</CardTitle>
        </CardHeader>
        <CardContent>
          <DataTable
            columns={columns}
            data={awaitingMd}
            isLoading={isLoading}
            emptyState={<p>Nothing currently with MD.</p>}
            onRowClick={(row) => navigate(`/requisitions/${row.id}`)}
          />
        </CardContent>
      </Card>
    </div>
  );
}
