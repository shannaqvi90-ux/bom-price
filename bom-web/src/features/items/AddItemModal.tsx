import { useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { Dialog } from "@/components/ui/Dialog";
import { Button } from "@/components/ui/Button";
import { Input } from "@/components/ui/Input";
import { Label } from "@/components/ui/Label";
import { BranchPicker } from "@/components/BranchPicker";
import { useAuthStore } from "@/store/authStore";
import { useCreateItem } from "./itemsApi";

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
  onClose: () => void;
}

export function AddItemModal({ open, onClose }: Props) {
  const create = useCreateItem();
  // Admin has BranchId=null in the JWT, so the API can't infer a target
  // branch — admin must pick one in the modal. Other roles inherit their
  // branch from the JWT and skip the picker.
  const role = useAuthStore((s) => s.user?.role);
  const userBranchId = useAuthStore((s) => s.user?.branchId ?? null);
  const isAdmin = role === "Admin";
  const [pickedBranchId, setPickedBranchId] = useState<number | null>(userBranchId);
  const [branchError, setBranchError] = useState<string | null>(null);
  const {
    register,
    handleSubmit,
    reset,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { code: "", description: "", type: "FinishedGood", lastPurchasePrice: null },
  });

  const onSubmit = handleSubmit(async (values) => {
    if (isAdmin && pickedBranchId === null) {
      setBranchError("Branch is required.");
      return;
    }
    setBranchError(null);
    await create.mutateAsync({
      ...values,
      branchId: isAdmin ? pickedBranchId : undefined,
    });
    reset();
    setPickedBranchId(userBranchId);
    onClose();
  });

  function handleClose() {
    reset();
    setPickedBranchId(userBranchId);
    setBranchError(null);
    onClose();
  }

  return (
    <Dialog open={open} onClose={handleClose} title="Add Item">
      <form onSubmit={onSubmit} className="space-y-4" noValidate>
        {isAdmin && (
          <div className="space-y-1">
            <Label htmlFor="item-branch">Branch</Label>
            <BranchPicker
              id="item-branch"
              value={pickedBranchId}
              onChange={(v) => {
                setPickedBranchId(v);
                if (v !== null) setBranchError(null);
              }}
            />
            {branchError && <p className="text-xs text-destructive">{branchError}</p>}
          </div>
        )}

        <div className="space-y-1">
          <Label htmlFor="item-code">Code</Label>
          <Input id="item-code" {...register("code")} />
          {errors.code && <p className="text-xs text-destructive">{errors.code.message}</p>}
        </div>

        <div className="space-y-1">
          <Label htmlFor="item-desc">Description</Label>
          <Input id="item-desc" {...register("description")} />
          {errors.description && <p className="text-xs text-destructive">{errors.description.message}</p>}
        </div>

        <div className="space-y-1">
          <Label htmlFor="item-type">Type</Label>
          <select
            id="item-type"
            className="flex h-10 w-full rounded-md border border-input bg-background px-3 py-2 text-sm"
            {...register("type")}
          >
            <option value="FinishedGood">Finished Good</option>
            <option value="RawMaterial">Raw Material</option>
          </select>
        </div>

        <div className="space-y-1">
          <Label htmlFor="item-price">Last Purchase Price</Label>
          <Input
            id="item-price"
            type="number"
            step="0.0001"
            placeholder="Optional"
            {...register("lastPurchasePrice")}
          />
          {errors.lastPurchasePrice && (
            <p className="text-xs text-destructive">{errors.lastPurchasePrice.message}</p>
          )}
        </div>

        {create.isError && (
          <p className="text-sm text-destructive">
            {(create.error as { response?: { data?: { message?: string } } })?.response?.data
              ?.message ?? "Failed to create item"}
          </p>
        )}

        <div className="flex justify-end gap-2 pt-2">
          <Button type="button" variant="ghost" onClick={handleClose}>
            Cancel
          </Button>
          <Button type="submit" disabled={isSubmitting || create.isPending}>
            {create.isPending ? "Saving…" : "Save"}
          </Button>
        </div>
      </form>
    </Dialog>
  );
}
