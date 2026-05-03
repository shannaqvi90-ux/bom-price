import { useMemo } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";
import type { ColumnDef } from "@tanstack/react-table";
import { Card, CardContent } from "@/components/ui/Card";
import { DataTable } from "@/components/ui/DataTable";
import { StatusBadge } from "@/components/ui/StatusBadge";
import { useRequisitions } from "@/features/requisitions/requisitionsApi";
import { formatRelative } from "@/utils/date";
import type { RequisitionListItem, V3RequisitionStatus } from "@/types/api";

type AccountantTab = "queue" | "in-flight" | "signed" | "rejected";

const VALID_TABS: AccountantTab[] = ["queue", "in-flight", "signed", "rejected"];

const TAB_LABELS: Record<AccountantTab, string> = {
  queue: "Queue",
  "in-flight": "In Flight",
  signed: "Signed",
  rejected: "Rejected/Cancelled",
};

const TAB_STATUSES: Record<AccountantTab, V3RequisitionStatus[]> = {
  queue: ["Costing"],
  "in-flight": ["MdPricing", "CustomerConfirm", "MdFinalSign"],
  signed: ["Signed"],
  rejected: ["Rejected", "Cancelled"],
};

const columns: ColumnDef<RequisitionListItem>[] = [
  { accessorKey: "refNo", header: "Ref No" },
  {
    accessorKey: "status",
    header: "Status",
    cell: (info) => (
      <StatusBadge status={info.getValue() as RequisitionListItem["status"]} />
    ),
  },
  { accessorKey: "customerName", header: "Customer" },
  { accessorKey: "salesPersonName", header: "Sales" },
  {
    accessorKey: "createdAt",
    header: "Created",
    cell: (info) => formatRelative(info.getValue() as string),
  },
];

export function AccountantListPage() {
  const navigate = useNavigate();
  const [params, setParams] = useSearchParams();
  const tabParam = params.get("tab") as AccountantTab | null;
  const tab: AccountantTab =
    tabParam && VALID_TABS.includes(tabParam) ? tabParam : "queue";

  const { data, isLoading } = useRequisitions();

  const filtered = useMemo(() => {
    const wanted = new Set(TAB_STATUSES[tab]);
    return (data ?? []).filter((r) =>
      wanted.has(r.status as V3RequisitionStatus),
    );
  }, [data, tab]);

  const counts = useMemo(() => {
    const out: Record<AccountantTab, number> = {
      queue: 0,
      "in-flight": 0,
      signed: 0,
      rejected: 0,
    };
    for (const r of data ?? []) {
      for (const t of VALID_TABS) {
        if (TAB_STATUSES[t].includes(r.status as V3RequisitionStatus)) {
          out[t] += 1;
        }
      }
    }
    return out;
  }, [data]);

  const setTab = (next: AccountantTab) => {
    const p = new URLSearchParams(params);
    p.set("tab", next);
    setParams(p, { replace: true });
  };

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold tracking-tight">My Queue</h1>
      </div>

      <div className="flex gap-1 border-b border-border">
        {VALID_TABS.map((t) => {
          const active = t === tab;
          return (
            <button
              key={t}
              type="button"
              onClick={() => setTab(t)}
              className={`relative px-4 py-2 text-sm font-medium transition ${
                active
                  ? "text-blue-700 dark:text-blue-300"
                  : "text-muted-foreground hover:text-foreground"
              }`}
            >
              {TAB_LABELS[t]}
              <span
                className={`ml-2 rounded-full px-2 py-0.5 text-xs font-semibold ${
                  active
                    ? "bg-blue-100 text-blue-800 dark:bg-blue-900/40 dark:text-blue-300"
                    : "bg-muted text-foreground"
                }`}
              >
                {counts[t]}
              </span>
              {active ? (
                <span className="absolute -bottom-px left-0 right-0 h-0.5 bg-blue-600" />
              ) : null}
            </button>
          );
        })}
      </div>

      <Card>
        <CardContent className="p-0">
          <DataTable
            columns={columns}
            data={filtered}
            isLoading={isLoading}
            emptyState={
              <div className="px-6 py-10 text-center text-sm text-muted-foreground">
                {emptyHintFor(tab)}
              </div>
            }
            onRowClick={(row) => navigate(`/requisitions/${row.id}`)}
          />
        </CardContent>
      </Card>
    </div>
  );
}

function emptyHintFor(tab: AccountantTab): string {
  switch (tab) {
    case "queue":
      return "Nothing waiting on costing right now.";
    case "in-flight":
      return "No requisitions in flight.";
    case "signed":
      return "Nothing signed yet.";
    case "rejected":
      return "Nothing rejected or cancelled.";
  }
}
