import { useState } from "react";
import { useCreateCustomer } from "@/features/customers/customersApi";
import type { Customer } from "@/types/api";
import { Input } from "@/components/ui/Input";

interface Props {
  open: boolean;
  onClose: () => void;
  onCreated: (customer: Customer) => void;
}

export function CreateCustomerModal({ open, onClose, onCreated }: Props) {
  const [name, setName] = useState("");
  const [email, setEmail] = useState("");
  const [phoneNumber, setPhoneNumber] = useState("");
  const [address, setAddress] = useState("");
  const [error, setError] = useState<string | null>(null);
  const createCustomer = useCreateCustomer();

  if (!open) return null;

  const onSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    if (!name.trim()) {
      setError("Name is required");
      return;
    }
    try {
      // code is auto-generated server-side; send "" to satisfy existing TS
      // CreateCustomerRequest contract — backend ignores it.
      const created = await createCustomer.mutateAsync({
        code: "",
        name: name.trim(),
        email: email.trim(),
        phoneNumber: phoneNumber.trim(),
        address: address.trim(),
      });
      onCreated(created);
      onClose();
    } catch (err: unknown) {
      const message =
        (err as { response?: { data?: { error?: string } } } | null)?.response?.data?.error
        ?? "Failed to create customer";
      setError(message);
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50">
      <div className="w-full max-w-md rounded-xl bg-card p-6 shadow-xl">
        <h2 className="text-lg font-semibold text-foreground">Create Customer</h2>
        <p className="mt-1 text-xs text-muted-foreground">Code is auto-generated as CUST-XXXX on save.</p>

        <form onSubmit={onSubmit} className="mt-4 space-y-3">
          <label className="block">
            <span className="text-sm font-medium text-foreground">Name</span>
            <Input value={name} onChange={(e) => setName(e.target.value)}
              className="mt-1" aria-label="name" autoFocus />
          </label>
          <label className="block">
            <span className="text-sm font-medium text-foreground">Email</span>
            <Input type="email" value={email} onChange={(e) => setEmail(e.target.value)}
              className="mt-1" aria-label="email" />
          </label>
          <label className="block">
            <span className="text-sm font-medium text-foreground">Phone</span>
            <Input value={phoneNumber} onChange={(e) => setPhoneNumber(e.target.value)}
              className="mt-1" aria-label="phone" />
          </label>
          <label className="block">
            <span className="text-sm font-medium text-foreground">Address</span>
            <Input value={address} onChange={(e) => setAddress(e.target.value)}
              className="mt-1" aria-label="address" />
          </label>

          {error && <p className="text-sm text-red-600">{error}</p>}

          <div className="flex justify-end gap-2 pt-2">
            <button type="button" onClick={onClose}
              className="rounded-md border border-border bg-card px-4 py-2 text-sm font-medium text-foreground hover:bg-muted">
              Cancel
            </button>
            <button type="submit" disabled={createCustomer.isPending}
              className="rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50">
              {createCustomer.isPending ? "Creating…" : "Create"}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
