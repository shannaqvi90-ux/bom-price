import { useState, useMemo } from "react";
import type { ColumnDef } from "@tanstack/react-table";
import { Pencil, Ban, CheckCircle } from "lucide-react";
import { DataTable } from "@/components/ui/DataTable";
import { Button } from "@/components/ui/Button";
import { Card, CardContent } from "@/components/ui/Card";
import { useAuthStore } from "@/store/authStore";
import { useBranches } from "@/api/branches";
import { useItems, useUpdateItemStatus } from "./itemsApi";
import { AddItemModal } from "./AddItemModal";
import { EditItemModal } from "./EditItemModal";
import { ImportItemsModal } from "./ImportItemsModal";
import { ImportLedgerModal } from "./ImportLedgerModal";
import type { Item } from "@/types/api";

export default function ItemListPage() {
  const role = useAuthStore((s) => s.user?.role);
  const { data, isLoading, isError, refetch } = useItems();
  const { data: branches = [] } = useBranches();
  const updateStatus = useUpdateItemStatus();

  const [addOpen, setAddOpen] = useState(false);
  const [editItem, setEditItem] = useState<Item | null>(null);
  const [importOpen, setImportOpen] = useState(false);
  const [ledgerOpen, setLedgerOpen] = useState(false);
  const [showInactive, setShowInactive] = useState(false);

  const canAdd = role === "SalesPerson" || role === "Admin";
  const canImport = role === "Admin";
  const canManage = role === "Admin" || role === "Accountant";

  const filteredItems = useMemo(
    () => (showInactive ? (data ?? []) : (data ?? []).filter((i) => i.isActive)),
    [data, showInactive],
  );

  // V3: Item codes are server-generated via CodeGeneratorService.NextItemCodeAsync.
  // The Code column here is render-only. The legacy AddItemModal still accepts Code input
  // for V2.3 admin manual-create flow; the V3 NewRequisitionPage uses
  // CreateFinishedGoodModal / CreateRawMaterialModal (preview-only Code) for the
  // new-item-while-building-BOM path.
  const columns = useMemo<ColumnDef<Item>[]>(
    () => [
      {
        accessorKey: "code",
        header: "Code",
        cell: (i) => (
          <span className={`font-mono text-xs${!i.row.original.isActive ? " text-muted-foreground" : ""}`}>
            {i.getValue() as string}
          </span>
        ),
      },
      {
        accessorKey: "description",
        header: "Description",
        cell: (i) => (
          <span className={!i.row.original.isActive ? "text-muted-foreground" : ""}>
            {i.getValue() as string}
          </span>
        ),
      },
      { accessorKey: "type", header: "Type" },
      {
        accessorKey: "lastPurchasePrice",
        header: "Last Purchase Price",
        cell: (i) => {
          const v = i.getValue() as number | null;
          return v == null ? "—" : v.toFixed(4);
        },
      },
      {
        accessorKey: "isActive",
        header: "Status",
        cell: (i) =>
          (i.getValue() as boolean) ? (
            <span className="inline-flex items-center rounded-full bg-green-50 px-2 py-0.5 text-xs font-medium text-green-700">
              Active
            </span>
          ) : (
            <span className="inline-flex items-center rounded-full bg-muted px-2 py-0.5 text-xs font-medium text-muted-foreground">
              Inactive
            </span>
          ),
      },
      ...(canManage
        ? [
            {
              id: "actions",
              header: "",
              cell: ({ row }: { row: { original: Item } }) => {
                const item = row.original;
                return (
                  <div className="flex justify-end gap-1">
                    <Button
                      variant="ghost"
                      size="icon"
                      aria-label={`Edit ${item.description}`}
                      onClick={() => setEditItem(item)}
                    >
                      <Pencil className="h-4 w-4" />
                    </Button>
                    <Button
                      variant="ghost"
                      size="icon"
                      aria-label={item.isActive ? `Deactivate ${item.description}` : `Reactivate ${item.description}`}
                      onClick={() => {
                        if (
                          item.isActive &&
                          !window.confirm(
                            `Deactivate "${item.description}"? It will no longer appear in BOM dropdowns.`,
                          )
                        )
                          return;
                        updateStatus.mutate({ id: item.id, isActive: !item.isActive });
                      }}
                    >
                      {item.isActive ? (
                        <Ban className="h-4 w-4 text-destructive" />
                      ) : (
                        <CheckCircle className="h-4 w-4 text-green-600" />
                      )}
                    </Button>
                  </div>
                );
              },
            } as ColumnDef<Item>,
          ]
        : []),
    ],
    [canManage, updateStatus],
  );

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold tracking-tight">Items</h1>
        <div className="flex gap-2">
          {canImport && (
            <Button variant="ghost" onClick={() => setLedgerOpen(true)}>
              Import from Ledger
            </Button>
          )}
          {canImport && (
            <Button variant="ghost" onClick={() => setImportOpen(true)}>
              Import
            </Button>
          )}
          {canAdd && <Button onClick={() => setAddOpen(true)}>Add Item</Button>}
        </div>
      </div>

      <div className="flex items-center gap-2">
        <input
          id="show-inactive"
          type="checkbox"
          checked={showInactive}
          onChange={(e) => setShowInactive(e.target.checked)}
          className="h-4 w-4 rounded border-input"
        />
        <label htmlFor="show-inactive" className="text-sm text-muted-foreground">
          Show inactive
        </label>
      </div>

      {isError && (
        <Card>
          <CardContent className="flex items-center justify-between">
            <p className="text-destructive">Failed to load items.</p>
            <Button variant="ghost" onClick={() => refetch()}>
              Retry
            </Button>
          </CardContent>
        </Card>
      )}

      <DataTable
        columns={columns}
        data={filteredItems}
        isLoading={isLoading}
        emptyState={<p>No items yet.</p>}
      />

      <AddItemModal open={addOpen} onClose={() => setAddOpen(false)} />
      <EditItemModal
        open={editItem !== null}
        item={editItem}
        onClose={() => setEditItem(null)}
      />
      {canImport && (
        <ImportItemsModal
          open={importOpen}
          onClose={() => setImportOpen(false)}
          branches={branches}
        />
      )}
      {canImport && (
        <ImportLedgerModal
          open={ledgerOpen}
          onClose={() => setLedgerOpen(false)}
          branches={branches}
        />
      )}
    </div>
  );
}
