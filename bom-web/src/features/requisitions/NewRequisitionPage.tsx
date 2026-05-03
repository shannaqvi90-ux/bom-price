import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { toast } from "sonner";
import {
  useCustomers,
  useCustomerImplicitItems,
} from "@/features/customers/customersApi";
import { useItems } from "@/api/lookups";
import {
  useCreateV3Requisition,
  useSubmitRequisition,
} from "@/features/requisitions/requisitionsApi";
import { BomEditorTable, type BomLineRow } from "@/components/v3/BomEditorTable";
import { CreateCustomerModal } from "@/components/v3/CreateCustomerModal";
import { CreateFinishedGoodModal } from "@/components/v3/CreateFinishedGoodModal";
import { Input } from "@/components/ui/Input";
import { Select } from "@/components/ui/Select";
import { Textarea } from "@/components/ui/Textarea";
import type { Item } from "@/types/api";

interface FgCardState {
  itemId: number;
  expectedQtyKg: number;
  printing: boolean;
  bomLines: BomLineRow[];
}

const CURRENCIES = ["AED", "USD", "EUR", "GBP", "JPY"];

export function NewRequisitionPage() {
  const navigate = useNavigate();
  const [customerId, setCustomerId] = useState<number>(0);
  const [currency, setCurrency] = useState("AED");
  const [referenceNumber, setReferenceNumber] = useState("");
  const [notes, setNotes] = useState("");
  const [fgs, setFgs] = useState<FgCardState[]>([]);

  const [createCustomerOpen, setCreateCustomerOpen] = useState(false);
  const [createFgOpen, setCreateFgOpen] = useState(false);
  // Tracks which FG card opened the "+ New FG" modal, so we can auto-select
  // the freshly-created item back into that card on success.
  const [creatingFgForIdx, setCreatingFgForIdx] = useState<number | null>(null);
  // Holds FGs created in this session. Customer-implicit items (the default
  // pool when a customer is selected) are sourced from historical reqs, so a
  // brand-new FG won't appear there until at least one quote uses it. We
  // merge these into the dropdown pool so the user sees what they just made.
  const [recentlyCreatedFgs, setRecentlyCreatedFgs] = useState<Item[]>([]);

  const customers = useCustomers();
  const allFgs = useItems({ type: "FinishedGood" });
  const customerFgs = useCustomerImplicitItems(customerId || null);

  const createReq = useCreateV3Requisition();
  const submitReq = useSubmitRequisition();

  // Q20: when customer selected, prefer their historical FGs; else allow
  // browsing all FGs (UX safety). Sales can still inline-create new FGs
  // via "+ New FG".
  const baseFgPool = customerId
    ? (customerFgs.data ?? [])
    : (allFgs.data ?? []);
  const fgItemPool = [
    ...baseFgPool,
    ...recentlyCreatedFgs.filter(
      (rc) => !baseFgPool.some((p) => p.id === rc.id),
    ),
  ];

  const updateFg = (idx: number, patch: Partial<FgCardState>) =>
    setFgs((s) => s.map((fg, i) => (i === idx ? { ...fg, ...patch } : fg)));
  const removeFg = (idx: number) => setFgs((s) => s.filter((_, i) => i !== idx));
  const addFg = () =>
    setFgs((s) => [
      ...s,
      { itemId: 0, expectedQtyKg: 0, printing: false, bomLines: [] },
    ]);

  const onSave = async (submit: boolean) => {
    if (!customerId) {
      toast.error("Pick a customer first");
      return;
    }
    if (fgs.length === 0) {
      toast.error("Add at least one finished good");
      return;
    }
    for (const fg of fgs) {
      if (fg.itemId === 0) {
        toast.error("Each FG must have an item selected");
        return;
      }
      if (fg.bomLines.length === 0) {
        toast.error("Each FG must have at least one BOM line");
        return;
      }
    }

    try {
      const created = await createReq.mutateAsync({
        customerId,
        quotationCurrency: currency,
        referenceNumber: referenceNumber || undefined,
        notes: notes || undefined,
        finishedGoods: fgs.map((fg) => ({
          itemId: fg.itemId,
          expectedQtyKg: fg.expectedQtyKg,
          printing: fg.printing,
          bomLines: fg.bomLines.map((b) => ({
            itemId: b.itemId,
            qtyPerKg: b.qtyPerKg,
            micron: b.micron,
            processId: b.processId,
          })),
        })),
      });

      if (submit) {
        await submitReq.mutateAsync(created.id);
        toast.success(`Submitted: ${created.refNo}`);
      } else {
        toast.success(`Saved as draft: ${created.refNo}`);
      }
      navigate(`/requisitions/${created.id}`);
    } catch (err: unknown) {
      const message =
        (err as { response?: { data?: { error?: string } } } | null)?.response
          ?.data?.error ?? "Failed to save requisition";
      toast.error(message);
    }
  };

  return (
    <div className="mx-auto max-w-5xl p-6">
      <h1 className="text-2xl font-semibold text-foreground">New Requisition</h1>

      <div className="mt-6 grid grid-cols-2 gap-4">
        <label className="block">
          <span className="text-sm font-medium text-foreground">Customer</span>
          <div className="mt-1 flex gap-2">
            <Select
              aria-label="customer"
              value={customerId}
              onChange={(e) => setCustomerId(parseInt(e.target.value))}
              className="flex-1"
            >
              <option value={0}>— select —</option>
              {(customers.data ?? []).map((c) => (
                <option key={c.id} value={c.id}>
                  {c.code} · {c.name}
                </option>
              ))}
            </Select>
            <button
              type="button"
              onClick={() => setCreateCustomerOpen(true)}
              className="rounded-md border border-blue-300 bg-blue-50 px-3 py-2 text-xs font-medium text-blue-700 hover:bg-blue-100"
            >
              + New
            </button>
          </div>
        </label>

        <label className="block">
          <span className="text-sm font-medium text-foreground">Currency</span>
          <Select
            aria-label="currency"
            value={currency}
            onChange={(e) => setCurrency(e.target.value)}
            className="mt-1"
          >
            {CURRENCIES.map((c) => (
              <option key={c} value={c}>
                {c}
              </option>
            ))}
          </Select>
        </label>

        <label className="block col-span-2">
          <span className="text-sm font-medium text-foreground">
            Reference (optional)
          </span>
          <Input
            value={referenceNumber}
            onChange={(e) => setReferenceNumber(e.target.value)}
            placeholder="PO-9941"
            className="mt-1"
          />
        </label>
      </div>

      <h2 className="mt-8 text-lg font-semibold text-foreground">Finished Goods</h2>
      {fgs.length === 0 && (
        <p className="mt-2 text-sm text-muted-foreground">No finished goods added yet.</p>
      )}
      <div className="mt-3 space-y-4">
        {fgs.map((fg, idx) => (
          <div key={idx} className="rounded-lg border border-border p-4">
            <div className="flex justify-between">
              <h3 className="font-medium text-foreground">FG #{idx + 1}</h3>
              <button
                type="button"
                onClick={() => removeFg(idx)}
                className="text-xs text-red-600"
              >
                Remove FG
              </button>
            </div>

            <div className="mt-3 grid grid-cols-3 gap-3">
              <label className="block col-span-2">
                <span className="text-sm font-medium text-foreground">FG Item</span>
                <div className="mt-1 flex gap-2">
                  <Select
                    aria-label="fg item"
                    value={fg.itemId}
                    onChange={(e) =>
                      updateFg(idx, { itemId: parseInt(e.target.value) })
                    }
                    className="flex-1"
                  >
                    <option value={0}>— select —</option>
                    {fgItemPool.map((i) => (
                      <option key={i.id} value={i.id}>
                        {i.code} · {i.description}
                      </option>
                    ))}
                  </Select>
                  <button
                    type="button"
                    onClick={() => {
                      setCreatingFgForIdx(idx);
                      setCreateFgOpen(true);
                    }}
                    className="rounded-md border border-blue-300 bg-blue-50 px-3 py-2 text-xs font-medium text-blue-700 hover:bg-blue-100"
                  >
                    + New FG
                  </button>
                </div>
              </label>

              <label className="block">
                <span className="text-sm font-medium text-foreground">
                  Quantity (KG)
                </span>
                <Input
                  type="number"
                  aria-label="quantity"
                  value={fg.expectedQtyKg || ""}
                  onChange={(e) =>
                    updateFg(idx, {
                      expectedQtyKg: parseFloat(e.target.value) || 0,
                    })
                  }
                  className="mt-1"
                />
              </label>
            </div>

            <label className="mt-3 inline-flex items-center gap-2">
              <input
                type="checkbox"
                checked={fg.printing}
                onChange={(e) => updateFg(idx, { printing: e.target.checked })}
              />
              <span className="text-sm text-foreground">Printing required</span>
            </label>

            <h4 className="mt-4 text-sm font-medium text-foreground">BOM Recipe</h4>
            <div className="mt-2">
              <BomEditorTable
                lines={fg.bomLines}
                onChange={(lines) => updateFg(idx, { bomLines: lines })}
              />
            </div>
          </div>
        ))}
        <button
          type="button"
          onClick={addFg}
          className="rounded-md border border-blue-300 bg-blue-50 px-4 py-2 text-sm font-medium text-blue-700 hover:bg-blue-100"
        >
          + Add Finished Good
        </button>
      </div>

      <h2 className="mt-8 text-lg font-semibold text-foreground">Notes</h2>
      <Textarea
        value={notes}
        onChange={(e) => setNotes(e.target.value)}
        rows={3}
        className="mt-2"
      />

      <div className="mt-8 flex justify-end gap-3">
        <button
          type="button"
          onClick={() => navigate(-1)}
          className="rounded-md border border-border bg-card px-4 py-2 text-sm font-medium text-foreground hover:bg-muted"
        >
          Cancel
        </button>
        <button
          type="button"
          onClick={() => onSave(false)}
          disabled={createReq.isPending}
          className="rounded-md border border-border bg-card px-4 py-2 text-sm font-medium text-foreground hover:bg-muted disabled:opacity-50"
        >
          Save Draft
        </button>
        <button
          type="button"
          onClick={() => onSave(true)}
          disabled={createReq.isPending || submitReq.isPending}
          className="rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50"
        >
          Submit
        </button>
      </div>

      <CreateCustomerModal
        open={createCustomerOpen}
        onClose={() => setCreateCustomerOpen(false)}
        onCreated={(cust) => setCustomerId(cust.id)}
      />
      <CreateFinishedGoodModal
        open={createFgOpen}
        onClose={() => {
          setCreateFgOpen(false);
          setCreatingFgForIdx(null);
        }}
        onCreated={(item) => {
          // Make the new FG visible in the dropdown immediately. The
          // customer-implicit-items query won't return this id until the req
          // is saved + appears in customer history, so we hold the new item
          // in local state and merge it into fgItemPool.
          setRecentlyCreatedFgs((prev) =>
            prev.some((p) => p.id === item.id) ? prev : [...prev, item],
          );
          // Auto-select on the FG card that triggered the modal.
          if (creatingFgForIdx !== null) {
            updateFg(creatingFgForIdx, { itemId: item.id });
          }
          setCreatingFgForIdx(null);
        }}
      />
    </div>
  );
}
