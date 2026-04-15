import { useState } from "react";
import type { ColumnDef } from "@tanstack/react-table";
import { DataTable } from "@/components/ui/DataTable";
import { Button } from "@/components/ui/Button";
import { Card, CardContent } from "@/components/ui/Card";
import { useAuthStore } from "@/store/authStore";
import { useCustomers } from "./customersApi";
import { AddCustomerModal } from "./AddCustomerModal";
import { ImportCustomersModal } from "./ImportCustomersModal";
import type { Customer } from "@/types/api";

const columns: ColumnDef<Customer>[] = [
  { accessorKey: "code", header: "Code", cell: (info) => <span className="font-mono text-xs">{info.getValue() as string}</span> },
  { accessorKey: "name", header: "Name" },
  { accessorKey: "email", header: "Email" },
  { accessorKey: "phoneNumber", header: "Phone" },
  {
    id: "salesPerson",
    header: "Sales Person",
    accessorFn: (row) => row.salesPersonName ?? "—",
  },
];

export default function CustomerListPage() {
  const role = useAuthStore((s) => s.user?.role);
  const { data, isLoading, isError, refetch } = useCustomers();
  const [addOpen, setAddOpen] = useState(false);
  const [importOpen, setImportOpen] = useState(false);

  const isAdmin = role === "Admin";

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
    </div>
  );
}
