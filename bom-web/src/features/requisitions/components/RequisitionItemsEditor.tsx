import { Controller, useFieldArray, useWatch } from "react-hook-form";
import type { Control, FieldErrors, UseFormRegister } from "react-hook-form";
import { Plus, Trash2 } from "lucide-react";

import { Button } from "@/components/ui/Button";
import { SearchableSelect } from "@/components/ui/SearchableSelect";
import type { Item } from "@/types/api";

export interface ItemsFormShape {
  items: {
    item: { id: number } | null;
    expectedQty: number;
  }[];
}

interface Props<T extends ItemsFormShape> {
  control: Control<T>;
  register: UseFormRegister<T>;
  errors: FieldErrors<T>;
  availableItems: Item[];
}

export function RequisitionItemsEditor<T extends ItemsFormShape>({
  control,
  register,
  errors,
  availableItems,
}: Props<T>) {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const { fields, append, remove } = useFieldArray({ control: control as any, name: "items" as any });
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const watchedItems = useWatch({ control: control as any, name: "items" as any }) as ItemsFormShape["items"] | undefined;

  const availableFor = (rowIndex: number): Item[] => {
    const takenIds = new Set(
      (watchedItems ?? [])
        .map((row, i) => (i !== rowIndex ? row?.item?.id : undefined))
        .filter((v): v is number => typeof v === "number"),
    );
    return availableItems.filter((it) => !takenIds.has(it.id));
  };

  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const itemsErrors = (errors as any).items;

  return (
    <div className="space-y-2">
      <label className="text-sm font-medium">Items</label>
      {fields.map((field, index) => (
        <div key={field.id} className="flex items-start gap-2">
          <div className="flex-1">
            <Controller
              // eslint-disable-next-line @typescript-eslint/no-explicit-any
              control={control as any}
              // eslint-disable-next-line @typescript-eslint/no-explicit-any
              name={`items.${index}.item` as any}
              render={({ field: f }) => (
                <SearchableSelect<Item>
                  id={`item-${index}`}
                  options={availableFor(index)}
                  value={f.value as Item | null}
                  onChange={f.onChange}
                  getLabel={(i) => i.description}
                  getValue={(i) => i.id}
                  placeholder="Search items…"
                />
              )}
            />
            {itemsErrors?.[index]?.item && (
              <p className="text-xs text-destructive">
                {itemsErrors[index].item?.message as string}
              </p>
            )}
          </div>
          <div className="w-32">
            <input
              type="number"
              step="0.0001"
              className="flex h-10 w-full rounded-md border border-input bg-background px-3 py-2 text-sm"
              placeholder="Qty"
              // eslint-disable-next-line @typescript-eslint/no-explicit-any
              {...register(`items.${index}.expectedQty` as any, { valueAsNumber: true })}
            />
            {itemsErrors?.[index]?.expectedQty && (
              <p className="text-xs text-destructive">
                {itemsErrors[index].expectedQty?.message as string}
              </p>
            )}
          </div>
          <Button
            type="button"
            variant="ghost"
            size="icon"
            disabled={fields.length <= 1}
            onClick={() => remove(index)}
          >
            <Trash2 className="h-4 w-4" />
          </Button>
        </div>
      ))}
      <Button
        type="button"
        variant="outline"
        size="sm"
        onClick={() =>
          append({
            item: null as unknown as { id: number },
            expectedQty: undefined as unknown as number,
            // eslint-disable-next-line @typescript-eslint/no-explicit-any
          } as any)
        }
      >
        <Plus className="mr-1 h-4 w-4" /> Add Item
      </Button>
      {itemsErrors?.root && (
        <p className="text-xs text-destructive">{itemsErrors.root.message as string}</p>
      )}
    </div>
  );
}
