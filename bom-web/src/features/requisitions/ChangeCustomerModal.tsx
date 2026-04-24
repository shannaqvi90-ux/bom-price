import { useState } from "react";
import { Dialog } from "@/components/ui/Dialog";
import { Button } from "@/components/ui/Button";
import { SearchableSelect } from "@/components/ui/SearchableSelect";
import { useCustomers } from "@/api/lookups";
import { useChangeRequisitionCustomer } from "./requisitionsApi";
import { AddCustomerModal } from "@/features/customers/AddCustomerModal";
import { notify } from "@/lib/notify";
import type { Customer } from "@/types/api";

interface Props {
  open: boolean;
  onClose: () => void;
  requisitionId: number;
  currentCustomerId: number;
  currentCustomerName: string;
}

export function ChangeCustomerModal({
  open,
  onClose,
  requisitionId,
  currentCustomerId,
  currentCustomerName,
}: Props) {
  const customersQ = useCustomers();
  const mutation = useChangeRequisitionCustomer(requisitionId);
  const [newCustomer, setNewCustomer] = useState<Customer | null>(null);
  const [reason, setReason] = useState("");
  const [addOpen, setAddOpen] = useState(false);

  const options = (customersQ.data ?? []).filter((c) => c.id !== currentCustomerId);

  async function onConfirm() {
    if (!newCustomer) return;
    try {
      await mutation.mutateAsync({
        customerId: newCustomer.id,
        reason: reason.trim() || null,
      });
      notify.success("Customer changed. Logged in audit history.");
      setNewCustomer(null);
      setReason("");
      onClose();
    } catch (e) {
      notify.fromApiError(e, "Failed to change customer");
    }
  }

  return (
    <Dialog open={open} onClose={onClose} title="Change customer">
      <div className="space-y-4">
        <div>
          <p className="text-xs text-muted-foreground">Current customer</p>
          <p className="text-sm font-medium">{currentCustomerName}</p>
        </div>

        <div className="space-y-2">
          <div className="flex items-center justify-between">
            <label htmlFor="new-customer" className="text-sm font-medium">
              New customer
            </label>
            <button
              type="button"
              onClick={() => setAddOpen(true)}
              className="text-sm text-primary hover:underline"
            >
              + Add new customer
            </button>
          </div>
          <SearchableSelect<Customer>
            id="new-customer"
            options={options}
            value={newCustomer}
            onChange={setNewCustomer}
            getLabel={(c) => c.name}
            getValue={(c) => c.id}
            placeholder="Search customers…"
          />
        </div>

        <div className="space-y-1">
          <label htmlFor="reason" className="text-sm font-medium">
            Reason (optional)
          </label>
          <textarea
            id="reason"
            rows={3}
            maxLength={500}
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            className="w-full rounded-md border px-3 py-2 text-sm"
            placeholder="Why is this changing? (visible in audit history)"
          />
        </div>

        <div className="flex justify-end gap-2">
          <Button variant="ghost" onClick={onClose}>
            Cancel
          </Button>
          <Button onClick={onConfirm} disabled={!newCustomer || mutation.isPending}>
            {mutation.isPending ? "Saving…" : "Confirm"}
          </Button>
        </div>
      </div>

      <AddCustomerModal
        open={addOpen}
        onClose={() => setAddOpen(false)}
        onCreated={(c) => setNewCustomer(c)}
      />
    </Dialog>
  );
}
