import { useMemo, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import type { ColumnDef } from "@tanstack/react-table";
import { useRequisitions } from "./requisitionsApi";
import { RequisitionFilters, type Filters } from "./components/RequisitionFilters";
import { DataTable } from "@/components/ui/DataTable";
import { StatusBadge } from "@/components/ui/StatusBadge";
import { Button } from "@/components/ui/Button";
import { Card, CardContent } from "@/components/ui/Card";
import { useAuthStore } from "@/store/authStore";
import { formatRelative } from "@/utils/date";
import type { RequisitionListItem } from "@/types/api";

const columns: ColumnDef<RequisitionListItem>[] = [
  {
    accessorKey: "refNo",
    header: "Ref No",
    cell: (info) => (
      <span className="font-mono text-xs">{info.getValue() as string}</span>
    ),
  },
  {
    accessorKey: "status",
    header: "Status",
    cell: (info) => <StatusBadge status={info.getValue() as RequisitionListItem["status"]} />,
  },
  {
    accessorKey: "itemDescription",
    header: "Item",
    cell: (info) => {
      const v = info.getValue() as string;
      return <span title={v}>{v.length > 40 ? `${v.slice(0, 40)}…` : v}</span>;
    },
    enableSorting: false,
  },
  { accessorKey: "customerName", header: "Customer" },
  {
    id: "qty",
    header: "Qty",
    accessorFn: (row) => `${row.expectedQty} ${row.currencyCode}`,
    enableSorting: false,
  },
  { accessorKey: "branchName", header: "Branch" },
  {
    accessorKey: "createdAt",
    header: "Created",
    cell: (info) => formatRelative(info.getValue() as string),
  },
];

const EMPTY_FILTERS: Filters = { status: "", from: "", to: "" };

export default function RequisitionListPage() {
  const role = useAuthStore((s) => s.user?.role);
  const branchId = useAuthStore((s) => s.user?.branchId);
  const navigate = useNavigate();
  const [filters, setFilters] = useState<Filters>(EMPTY_FILTERS);

  const { data, isLoading, isError, refetch } = useRequisitions();

  const visibleColumns = useMemo(() => {
    if (branchId) {
      return columns.filter((c) => (c as { accessorKey?: string }).accessorKey !== "branchName");
    }
    return columns;
  }, [branchId]);

  const filtered = useMemo(() => {
    if (!data) return [];
    return data.filter((r) => {
      if (filters.status && r.status !== filters.status) return false;
      if (filters.from && new Date(r.createdAt) < new Date(filters.from + "T00:00:00Z")) return false;
      if (filters.to && new Date(r.createdAt) > new Date(filters.to + "T23:59:59.999Z")) return false;
      return true;
    });
  }, [data, filters]);

  const hasActiveFilters =
    filters.status !== "" || filters.from !== "" || filters.to !== "";

  const emptyState =
    hasActiveFilters ? (
      <div className="space-y-2">
        <p>No requisitions match your filters.</p>
        <Button variant="ghost" onClick={() => setFilters(EMPTY_FILTERS)}>
          Clear filters
        </Button>
      </div>
    ) : role === "SalesPerson" || role === "Admin" ? (
      <div className="space-y-2">
        <p>You haven't created any requisitions yet.</p>
        <Link to="/requisitions/new">
          <Button>Create your first requisition</Button>
        </Link>
      </div>
    ) : (
      <p>No requisitions waiting.</p>
    );

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold tracking-tight">Requisitions</h1>
        {(role === "SalesPerson" || role === "Admin") && (
          <Button onClick={() => navigate("/requisitions/new")}>New Requisition</Button>
        )}
      </div>

      <RequisitionFilters value={filters} onChange={setFilters} />

      {isError && (
        <Card>
          <CardContent className="flex items-center justify-between">
            <p className="text-destructive">Failed to load requisitions.</p>
            <Button variant="ghost" onClick={() => refetch()}>
              Retry
            </Button>
          </CardContent>
        </Card>
      )}

      <DataTable
        columns={visibleColumns}
        data={filtered}
        isLoading={isLoading}
        emptyState={emptyState}
        onRowClick={(row) => navigate(`/requisitions/${row.id}`)}
      />
    </div>
  );
}
