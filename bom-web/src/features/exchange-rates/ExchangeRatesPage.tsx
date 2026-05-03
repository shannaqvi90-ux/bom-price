import { useState, useMemo } from "react";
import type { ColumnDef } from "@tanstack/react-table";
import { Pencil } from "lucide-react";
import { DataTable } from "@/components/ui/DataTable";
import { Button } from "@/components/ui/Button";
import { Card, CardContent } from "@/components/ui/Card";
import { useAuthStore } from "@/store/authStore";
import { useExchangeRates } from "./exchangeRatesApi";
import { AddRateModal } from "./AddRateModal";
import { EditRateModal } from "./EditRateModal";
import type { ExchangeRate } from "@/types/api";

export default function ExchangeRatesPage() {
  const role = useAuthStore((s) => s.user?.role);
  const { data, isLoading, isError, refetch } = useExchangeRates();
  const [addOpen, setAddOpen] = useState(false);
  const [editRate, setEditRate] = useState<ExchangeRate | null>(null);

  const canManage = role === "Accountant";

  const columns = useMemo<ColumnDef<ExchangeRate>[]>(
    () => [
      {
        accessorKey: "currencyCode",
        header: "Code",
        cell: (i) => (
          <span className="font-mono font-semibold">{i.getValue() as string}</span>
        ),
      },
      { accessorKey: "currencyName", header: "Currency" },
      {
        accessorKey: "rateToAed",
        header: "Rate to AED",
        cell: (i) => (i.getValue() as number).toFixed(4),
      },
      {
        accessorKey: "effectiveDate",
        header: "Effective Date",
        cell: (i) => (i.getValue() as string).split("T")[0],
      },
      {
        accessorKey: "isActive",
        header: "Status",
        cell: (i) =>
          (i.getValue() as boolean) ? (
            <span className="inline-flex items-center rounded-full bg-green-50 px-2 py-0.5 text-xs font-medium text-green-700 dark:bg-emerald-900/30 dark:text-emerald-300">
              Active
            </span>
          ) : (
            <span className="inline-flex items-center rounded-full bg-muted px-2 py-0.5 text-xs font-medium text-muted-foreground">
              Inactive
            </span>
          ),
      },
      { accessorKey: "setByName", header: "Set By" },
      ...(canManage
        ? [
            {
              id: "actions",
              header: "",
              cell: ({ row }: { row: { original: ExchangeRate } }) => {
                const rate = row.original;
                return (
                  <div className="flex justify-end">
                    <Button
                      variant="ghost"
                      size="icon"
                      aria-label={`Edit ${rate.currencyCode}`}
                      onClick={() => setEditRate(rate)}
                    >
                      <Pencil className="h-4 w-4" />
                    </Button>
                  </div>
                );
              },
            } as ColumnDef<ExchangeRate>,
          ]
        : []),
    ],
    [canManage],
  );

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold tracking-tight">Exchange Rates</h1>
        {canManage && (
          <Button onClick={() => setAddOpen(true)}>Add Rate</Button>
        )}
      </div>

      {isError && (
        <Card>
          <CardContent className="flex items-center justify-between">
            <p className="text-destructive">Failed to load exchange rates.</p>
            <Button variant="ghost" onClick={() => refetch()}>
              Retry
            </Button>
          </CardContent>
        </Card>
      )}

      <DataTable
        columns={columns}
        data={data ?? []}
        isLoading={isLoading}
        emptyState={<p>No exchange rates yet.</p>}
      />

      <AddRateModal open={addOpen} onClose={() => setAddOpen(false)} />
      <EditRateModal
        open={editRate !== null}
        rate={editRate}
        onClose={() => setEditRate(null)}
      />
    </div>
  );
}
