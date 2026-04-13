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
  { accessorKey: "itemDescription", header: "Item" },
  { accessorKey: "customerName", header: "Customer" },
  {
    accessorKey: "createdAt",
    header: "Created",
    cell: (info) => formatRelative(info.getValue() as string),
  },
];

export default function BomDashboard() {
  const navigate = useNavigate();
  const { data, isLoading } = useRequisitions();
  const rows = useMemo(
    () => (data ?? []).filter((r) => r.status === "BomPending" || r.status === "BomInProgress"),
    [data],
  );

  return (
    <div className="space-y-4">
      <h1 className="text-2xl font-semibold tracking-tight">BOM Queue</h1>
      <Card>
        <CardHeader><CardTitle>Awaiting BOM</CardTitle></CardHeader>
        <CardContent>
          <DataTable
            columns={columns}
            data={rows}
            isLoading={isLoading}
            emptyState={<p>No requisitions waiting for BOM.</p>}
            onRowClick={(row) => navigate(`/requisitions/${row.id}`)}
          />
        </CardContent>
      </Card>
    </div>
  );
}
