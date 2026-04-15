import { useEffect } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { Dialog } from "@/components/ui/Dialog";
import { Button } from "@/components/ui/Button";
import { Input } from "@/components/ui/Input";
import { Label } from "@/components/ui/Label";
import { useUpdateItem } from "./itemsApi";
import type { Item } from "@/types/api";

const schema = z.object({
  code: z.string().min(1, "Code is required"),
  description: z.string().min(1, "Description is required"),
  type: z.enum(["FinishedGood", "RawMaterial"]),
  lastPurchasePrice: z.preprocess(
    (v) => (v === "" || v === null || v === undefined ? null : Number(v)),
    z.number().positive("Must be positive").nullable(),
  ),
});

type FormValues = z.infer<typeof schema>;

interface Props {
  open: boolean;
  item: Item | null;
  onClose: () => void;
}

export function EditItemModal({ open, item, onClose }: Props) {
  const update = useUpdateItem();
  const {
    register,
    handleSubmit,
    reset,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { code: "", description: "", type: "FinishedGood", lastPurchasePrice: null },
  });

  useEffect(() => {
    if (item) {
      reset({
        code: item.code,
        description: item.description,
        type: item.type as "FinishedGood" | "RawMaterial",
        lastPurchasePrice: item.lastPurchasePrice ?? null,
      });
    }
  }, [item, reset]);

  const onSubmit = handleSubmit(async (values) => {
    if (!item) return;
    await update.mutateAsync({ id: item.id, data: values });
    onClose();
  });

  function handleClose() {
    reset();
    onClose();
  }

  return (
    <Dialog open={open} onClose={handleClose} title="Edit Item">
      <form onSubmit={onSubmit} className="space-y-4" noValidate>
        <div className="space-y-1">
          <Label htmlFor="edit-item-code">Code</Label>
          <Input id="edit-item-code" {...register("code")} />
          {errors.code && <p className="text-xs text-destructive">{errors.code.message}</p>}
        </div>

        <div className="space-y-1">
          <Label htmlFor="edit-item-desc">Description</Label>
          <Input id="edit-item-desc" {...register("description")} />
          {errors.description && (
            <p className="text-xs text-destructive">{errors.description.message}</p>
          )}
        </div>

        <div className="space-y-1">
          <Label htmlFor="edit-item-type">Type</Label>
          <select
            id="edit-item-type"
            className="flex h-10 w-full rounded-md border border-input bg-background px-3 py-2 text-sm"
            {...register("type")}
          >
            <option value="FinishedGood">Finished Good</option>
            <option value="RawMaterial">Raw Material</option>
          </select>
        </div>

        <div className="space-y-1">
          <Label htmlFor="edit-item-price">Last Purchase Price</Label>
          <Input
            id="edit-item-price"
            type="number"
            step="0.0001"
            placeholder="Optional"
            {...register("lastPurchasePrice")}
          />
          {errors.lastPurchasePrice && (
            <p className="text-xs text-destructive">{errors.lastPurchasePrice.message}</p>
          )}
        </div>

        {update.isError && (
          <p className="text-sm text-destructive">
            {(update.error as { response?: { data?: { message?: string } } })?.response?.data
              ?.message ?? "Failed to update item"}
          </p>
        )}

        <div className="flex justify-end gap-2 pt-2">
          <Button type="button" variant="ghost" onClick={handleClose}>
            Cancel
          </Button>
          <Button type="submit" disabled={isSubmitting || update.isPending}>
            {update.isPending ? "Saving…" : "Save"}
          </Button>
        </div>
      </form>
    </Dialog>
  );
}
