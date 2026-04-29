import { useMemo } from "react";
import { useNavigate } from "react-router-dom";
import type { ColumnDef } from "@tanstack/react-table";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/Card";
import { DataTable } from "@/components/ui/DataTable";
import { StatusBadge } from "@/components/ui/StatusBadge";
import { useRequisitions } from "@/features/requisitions/requisitionsApi";
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
  const signedThisMonth = useMemo(
    () =>
      (data ?? []).filter((r) => r.status === "Signed" && r.createdAt >= startOfMonth),
    [data, startOfMonth],
  );

  return (
    <div className="space-y-4">
      <h1 className="text-2xl font-semibold tracking-tight">MD Dashboard</h1>

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
