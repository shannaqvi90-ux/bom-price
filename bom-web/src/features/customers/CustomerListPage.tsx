import { useMemo, useState } from "react";
import type { ColumnDef } from "@tanstack/react-table";
import { DataTable } from "@/components/ui/DataTable";
import { Button } from "@/components/ui/Button";
import { Card, CardContent } from "@/components/ui/Card";
import { useAuthStore } from "@/store/authStore";
import { OwnedByBadge } from "@/components/OwnedByBadge";
import { useCustomers } from "./customersApi";
import { AddCustomerModal } from "./AddCustomerModal";
import { ImportCustomersModal } from "./ImportCustomersModal";
import { DeleteCustomerModal } from "@/features/admin/modals/DeleteCustomerModal";
import type { Customer } from "@/types/api";

export default function CustomerListPage() {
  const role = useAuthStore((s) => s.user?.role);
  const currentUserId = useAuthStore((s) => s.user?.userId);
  const { data, isLoading, isError, refetch } = useCustomers();

  const [addOpen, setAddOpen] = useState(false);
  const [importOpen, setImportOpen] = useState(false);
  const [customerToDelete, setCustomerToDelete] = useState<Customer | null>(null);

  const isAdmin = role === "Admin";

  // V3: Customer codes are server-generated via CodeGeneratorService.NextCustomerCodeAsync.
  // The Code column here is render-only. The legacy AddCustomerModal still has a Code input
  // for V2.3 admin manual-create flow; the V3 NewRequisitionPage uses CreateCustomerModal
  // (preview-only Code) for the new-customer-while-creating-req path.
  const columns = useMemo<ColumnDef<Customer>[]>(() => {
    const base: ColumnDef<Customer>[] = [
      { accessorKey: "code", header: "Code", cell: (info) => <span className="font-mono text-xs">{info.getValue() as string}</span> },
      {
        accessorKey: "name",
        header: "Name",
        cell: (info) => {
          const row = info.row.original;
          return (
            <span>
              {row.name}
              {currentUserId !== undefined && row.salesPersonId !== null && row.salesPersonId !== currentUserId && row.salesPersonName && (
                <OwnedByBadge ownerName={row.salesPersonName} prefix="owned by" />
              )}
            </span>
          );
        },
      },
      { accessorKey: "email", header: "Email" },
      { accessorKey: "phoneNumber", header: "Phone" },
      {
        id: "salesPerson",
        header: "Sales Person",
        accessorFn: (row) => row.salesPersonName ?? "—",
      },
    ];

    if (isAdmin) {
      base.push({
        id: "actions",
        header: "",
        cell: (info) => (
          <Button
            variant="ghost"
            size="sm"
            className="text-red-600 hover:text-red-700 dark:text-red-400 dark:hover:text-red-300"
            onClick={() => setCustomerToDelete(info.row.original)}
            aria-label={`Delete customer ${info.row.original.code}`}
          >
            Delete
          </Button>
        ),
      });
    }

    return base;
  }, [currentUserId, isAdmin]);

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold tracking-tight">Customers</h1>
        {isAdmin && (
          <div className="flex gap-2">
            <Button variant="ghost" onClick={() => setImportOpen(true)}>
              Import
            </Button>
            <Button onClick={() => setAddOpen(true)}>Add Customer</Button>
          </div>
        )}
      </div>

      {isError && (
        <Card>
          <CardContent className="flex items-center justify-between">
            <p className="text-destructive">Failed to load customers.</p>
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
        emptyState={<p>No customers found.</p>}
      />

      <AddCustomerModal open={addOpen} onClose={() => setAddOpen(false)} />
      <ImportCustomersModal open={importOpen} onClose={() => setImportOpen(false)} />
      {customerToDelete && (
        <DeleteCustomerModal
          customer={customerToDelete}
          onClose={() => setCustomerToDelete(null)}
        />
      )}
    </div>
  );
}
