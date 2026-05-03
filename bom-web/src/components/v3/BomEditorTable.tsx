import { useState } from "react";
import { useItems } from "@/api/lookups";
import { CreateRawMaterialModal } from "./CreateRawMaterialModal";
import { Input } from "@/components/ui/Input";
import { Select } from "@/components/ui/Select";

export interface BomLineRow {
  itemId: number;
  qtyPerKg: number;
  micron: string | null;
  processId: number;
}

interface Props {
  lines: BomLineRow[];
  onChange?: (lines: BomLineRow[]) => void;
  readOnly?: boolean;
}

const DEFAULT_PROCESS_ID = 1; // Extrusion — V3 cutover-day default

export function BomEditorTable({ lines, onChange, readOnly = false }: Props) {
  const [createOpen, setCreateOpen] = useState(false);
  const items = useItems({ type: "RawMaterial" });

  const itemMap = new Map((items.data ?? []).map((i) => [i.id, i]));

  const updateLine = (idx: number, patch: Partial<BomLineRow>) => {
    if (!onChange) return;
    onChange(lines.map((l, i) => (i === idx ? { ...l, ...patch } : l)));
  };
  const removeLine = (idx: number) => {
    if (!onChange) return;
    onChange(lines.filter((_, i) => i !== idx));
  };
  const addLine = () => {
    if (!onChange) return;
    onChange([
      ...lines,
      { itemId: 0, qtyPerKg: 0, micron: "", processId: DEFAULT_PROCESS_ID },
    ]);
  };

  return (
    <div className="space-y-2">
      <table className="w-full text-sm">
        <thead className="bg-gray-50">
          <tr>
            <th className="px-2 py-1 text-left font-medium text-gray-700">Item</th>
            <th className="px-2 py-1 text-left font-medium text-gray-700">Qty/KG</th>
            <th className="px-2 py-1 text-left font-medium text-gray-700">Micron</th>
            {!readOnly && <th />}
          </tr>
        </thead>
        <tbody className="divide-y divide-gray-100">
          {lines.map((line, idx) => {
            const item = itemMap.get(line.itemId);
            return (
              <tr key={idx}>
                <td className="px-2 py-1">
                  {readOnly ? (
                    <span>{item?.description ?? "—"}</span>
                  ) : (
                    <Select
                      value={line.itemId}
                      onChange={(e) => updateLine(idx, { itemId: parseInt(e.target.value) })}
                      className="h-8 w-full px-2 text-sm"
                      aria-label={`item-${idx}`}
                    >
                      <option value={0}>— select —</option>
                      {(items.data ?? []).map((i) => (
                        <option key={i.id} value={i.id}>
                          {i.code} · {i.description}
                        </option>
                      ))}
                    </Select>
                  )}
                </td>
                <td className="px-2 py-1">
                  {readOnly ? (
                    <span>{line.qtyPerKg}</span>
                  ) : (
                    <Input
                      type="number"
                      step="0.001"
                      value={line.qtyPerKg}
                      onChange={(e) => updateLine(idx, { qtyPerKg: parseFloat(e.target.value) || 0 })}
                      className="h-8 w-24 px-2 text-sm"
                      aria-label={`qty-${idx}`}
                    />
                  )}
                </td>
                <td className="px-2 py-1">
                  {readOnly ? (
                    <span>{line.micron ?? "—"}</span>
                  ) : (
                    <Input
                      type="text"
                      value={line.micron ?? ""}
                      onChange={(e) => updateLine(idx, { micron: e.target.value })}
                      className="h-8 w-20 px-2 text-sm"
                      aria-label={`micron-${idx}`}
                    />
                  )}
                </td>
                {!readOnly && (
                  <td className="px-2 py-1 text-right">
                    <button
                      type="button"
                      onClick={() => removeLine(idx)}
                      className="text-xs text-red-600 hover:text-red-700"
                    >
                      Remove
                    </button>
                  </td>
                )}
              </tr>
            );
          })}
        </tbody>
      </table>

      {!readOnly && (
        <div className="flex gap-2">
          <button
            type="button"
            onClick={addLine}
            className="rounded-md border border-gray-300 bg-white px-3 py-1.5 text-xs font-medium text-gray-700 hover:bg-gray-50"
          >
            + Add Raw Material
          </button>
          <button
            type="button"
            onClick={() => setCreateOpen(true)}
            className="rounded-md border border-blue-300 bg-blue-50 px-3 py-1.5 text-xs font-medium text-blue-700 hover:bg-blue-100"
          >
            + Create new RM
          </button>
        </div>
      )}

      <CreateRawMaterialModal
        open={createOpen}
        onClose={() => setCreateOpen(false)}
        onCreated={(item) => {
          if (onChange) {
            onChange([
              ...lines,
              { itemId: item.id, qtyPerKg: 0, micron: "", processId: DEFAULT_PROCESS_ID },
            ]);
          }
        }}
      />
    </div>
  );
}
