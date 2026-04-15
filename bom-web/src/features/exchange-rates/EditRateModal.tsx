import { useEffect } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { Dialog } from "@/components/ui/Dialog";
import { Button } from "@/components/ui/Button";
import { Input } from "@/components/ui/Input";
import { Label } from "@/components/ui/Label";
import { useUpdateRate } from "./exchangeRatesApi";
import type { ExchangeRate } from "@/types/api";

const schema = z.object({
  rateToAed: z.coerce.number().positive("Rate must be greater than 0"),
  effectiveDate: z.string().min(1, "Effective date is required"),
  isActive: z.boolean(),
});

type FormValues = z.infer<typeof schema>;

interface Props {
  open: boolean;
  rate: ExchangeRate | null;
  onClose: () => void;
}

export function EditRateModal({ open, rate, onClose }: Props) {
  const update = useUpdateRate();
  const {
    register,
    handleSubmit,
    reset,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
  });

  useEffect(() => {
    if (rate) {
      reset({
        rateToAed: rate.rateToAed,
        effectiveDate: rate.effectiveDate.split("T")[0],
        isActive: rate.isActive,
      });
    }
  }, [rate, reset]);

  function handleClose() {
    update.reset();
    onClose();
  }

  const onSubmit = handleSubmit(async (values) => {
    if (!rate) return;
    await update.mutateAsync({ id: rate.id, data: values });
    update.reset();
    onClose();
  });

  return (
    <Dialog open={open} onClose={handleClose} title="Edit Exchange Rate">
      <form onSubmit={onSubmit} className="space-y-4" noValidate>
        <div className="space-y-1">
          <Label>Currency</Label>
          <p className="font-mono text-sm">
            {rate?.currencyCode} — {rate?.currencyName}
          </p>
        </div>

        <div className="space-y-1">
          <Label htmlFor="edit-rate-value">Rate to AED</Label>
          <Input
            id="edit-rate-value"
            type="number"
            step="0.0001"
            min="0"
            {...register("rateToAed")}
          />
          {errors.rateToAed && (
            <p className="text-xs text-destructive">{errors.rateToAed.message}</p>
          )}
        </div>

        <div className="space-y-1">
          <Label htmlFor="edit-rate-date">Effective Date</Label>
          <Input id="edit-rate-date" type="date" {...register("effectiveDate")} />
          {errors.effectiveDate && (
            <p className="text-xs text-destructive">{errors.effectiveDate.message}</p>
          )}
        </div>

        <div className="flex items-center gap-2">
          <input
            id="edit-rate-active"
            type="checkbox"
            className="h-4 w-4 rounded border-input"
            {...register("isActive")}
          />
          <Label htmlFor="edit-rate-active">Active</Label>
        </div>

        {update.isError && (
          <p className="text-sm text-destructive">
            {(update.error as { response?: { data?: { message?: string } } })?.response
              ?.data?.message ?? "Failed to save"}
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
