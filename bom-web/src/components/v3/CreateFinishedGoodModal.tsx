import { useState } from "react";
import { useCreateItem } from "@/features/items/itemsApi";
import type { Item } from "@/types/api";
import { Input } from "@/components/ui/Input";

interface Props {
  open: boolean;
  onClose: () => void;
  onCreated: (item: Item) => void;
}

export function CreateFinishedGoodModal({ open, onClose, onCreated }: Props) {
  const [description, setDescription] = useState("");
  const [error, setError] = useState<string | null>(null);
  const createItem = useCreateItem();

  if (!open) return null;

  const onSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    if (!description.trim()) {
      setError("Description is required");
      return;
    }
    try {
      const item = await createItem.mutateAsync({
        code: "", // server-generated FG-XXXX
        description: description.trim(),
        type: "FinishedGood",
        lastPurchasePrice: null,
      });
      onCreated(item);
      onClose();
    } catch (err: unknown) {
      const message =
        (err as { response?: { data?: { error?: string } } } | null)?.response?.data?.error
        ?? "Failed to create item";
      setError(message);
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50">
      <div className="w-full max-w-md rounded-xl bg-white p-6 shadow-xl">
        <h2 className="text-lg font-semibold text-gray-900">Create Finished Good</h2>
        <p className="mt-1 text-xs text-gray-500">Code auto-generated (FG-XXXX). Branch: Alain.</p>
        <form onSubmit={onSubmit} className="mt-4 space-y-3">
          <label className="block">
            <span className="text-sm font-medium text-gray-700">Description</span>
            <Input value={description} onChange={(e) => setDescription(e.target.value)} aria-label="description" autoFocus
              className="mt-1" />
          </label>
          {error && <p className="text-sm text-red-600">{error}</p>}
          <div className="flex justify-end gap-2 pt-2">
            <button type="button" onClick={onClose}
              className="rounded-md border border-gray-300 bg-white px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50">Cancel</button>
            <button type="submit" disabled={createItem.isPending}
              className="rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50">
              {createItem.isPending ? "Creating…" : "Create"}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
