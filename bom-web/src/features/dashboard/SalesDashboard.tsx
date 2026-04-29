import { useMemo, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import type { ColumnDef } from "@tanstack/react-table";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/Card";
import { Button } from "@/components/ui/Button";
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

type Tab = "Draft" | "Costing" | "AwaitingMd" | "CustomerConfirm" | "Signed" | "Closed";

const TABS: { key: Tab; label: string; statuses: RequisitionStatus[] }[] = [
  { key: "Draft", label: "Drafts", statuses: ["Draft"] },
  { key: "Costing", label: "In Costing", statuses: ["Costing"] },
  { key: "AwaitingMd", label: "Awaiting MD", statuses: ["MdPricing", "MdFinalSign"] },
  { key: "CustomerConfirm", label: "Awaiting Customer Confirm", statuses: ["CustomerConfirm"] },
  { key: "Signed", label: "Signed/Done", statuses: ["Signed"] },
  { key: "Closed", label: "Cancelled/Rejected", statuses: ["Cancelled", "Rejected"] },
];

export default function SalesDashboard() {
  const navigate = useNavigate();
  const { data, isLoading } = useRequisitions();
  const [activeTab, setActiveTab] = useState<Tab>("Draft");

  const counts = useMemo(() => {
    const all = data ?? [];
    const map: Record<Tab, number> = {
      Draft: 0,
      Costing: 0,
      AwaitingMd: 0,
      CustomerConfirm: 0,
      Signed: 0,
      Closed: 0,
    };
    for (const tab of TABS) {
      map[tab.key] = all.filter((r) => tab.statuses.includes(r.status)).length;
    }
    return map;
  }, [data]);

  const rows = useMemo(() => {
    const allowed = TABS.find((t) => t.key === activeTab)?.statuses ?? [];
    return (data ?? []).filter((r) => allowed.includes(r.status));
  }, [data, activeTab]);

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold tracking-tight">My Requisitions</h1>
        <Link to="/requisitions/new">
          <Button>New Requisition</Button>
        </Link>
      </div>

      <div className="flex flex-wrap gap-2 border-b">
        {TABS.map((tab) => (
          <button
            key={tab.key}
            type="button"
            onClick={() => setActiveTab(tab.key)}
            className={`px-3 py-2 text-sm font-medium border-b-2 transition-colors ${
              activeTab === tab.key
                ? "border-primary text-primary"
                : "border-transparent text-muted-foreground hover:text-foreground"
            }`}
          >
            {tab.label}
            <span className="ml-1 rounded-full bg-muted px-1.5 py-0.5 text-xs">
              {counts[tab.key]}
            </span>
          </button>
        ))}
      </div>

      <Card>
        <CardHeader>
          <CardTitle>{TABS.find((t) => t.key === activeTab)?.label}</CardTitle>
        </CardHeader>
        <CardContent>
          <DataTable
            columns={columns}
            data={rows}
            isLoading={isLoading}
            emptyState={<p>No requisitions in this tab.</p>}
            onRowClick={(row) => navigate(`/requisitions/${row.id}`)}
          />
        </CardContent>
      </Card>
    </div>
  );
}
