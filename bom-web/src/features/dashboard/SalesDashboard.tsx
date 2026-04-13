import { Link, useNavigate } from "react-router-dom";
import type { ColumnDef } from "@tanstack/react-table";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/Card";
import { Button } from "@/components/ui/Button";
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

export default function SalesDashboard() {
  const navigate = useNavigate();
  const { data, isLoading } = useRequisitions();
  const rows = (data ?? []).slice(0, 10);

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold tracking-tight">My Recent Requisitions</h1>
        <Link to="/requisitions/new">
          <Button>New Requisition</Button>
        </Link>
      </div>
      <Card>
        <CardHeader>
          <CardTitle>Latest</CardTitle>
        </CardHeader>
        <CardContent>
          <DataTable
            columns={columns}
            data={rows}
            isLoading={isLoading}
            emptyState={<p>No requisitions yet.</p>}
            onRowClick={(row) => navigate(`/requisitions/${row.id}`)}
          />
        </CardContent>
      </Card>
    </div>
  );
}
