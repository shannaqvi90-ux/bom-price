import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { Dialog } from "@/components/ui/Dialog";
import { Button } from "@/components/ui/Button";
import { Input } from "@/components/ui/Input";
import { Label } from "@/components/ui/Label";
import { useCreateRate } from "./exchangeRatesApi";

const schema = z.object({
  currencyCode: z
    .string()
    .min(1, "Currency code is required")
    .transform((v) => v.toUpperCase()),
  currencyName: z.string().min(1, "Currency name is required"),
  rateToAed: z.coerce.number().positive("Rate must be greater than 0"),
  effectiveDate: z.string().min(1, "Effective date is required"),
});

type FormValues = z.infer<typeof schema>;

interface Props {
  open: boolean;
  onClose: () => void;
}

export function AddRateModal({ open, onClose }: Props) {
  const create = useCreateRate();
  const {
    register,
    handleSubmit,
    reset,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      currencyCode: "",
      currencyName: "",
      rateToAed: 0,
      effectiveDate: new Date().toISOString().split("T")[0],
    },
  });

  const onSubmit = handleSubmit(async (values) => {
    await create.mutateAsync(values);
    reset();
    onClose();
  });

  function handleClose() {
    reset();
    onClose();
  }

  return (
    <Dialog open={open} onClose={handleClose} title="Add Exchange Rate">
      <form onSubmit={onSubmit} className="space-y-4" noValidate>
        <div className="space-y-1">
          <Label htmlFor="rate-code">Currency Code</Label>
          <Input id="rate-code" placeholder="USD" {...register("currencyCode")} />
          {errors.currencyCode && (
            <p className="text-xs text-destructive">{errors.currencyCode.message}</p>
          )}
        </div>

        <div className="space-y-1">
          <Label htmlFor="rate-name">Currency Name</Label>
          <Input id="rate-name" placeholder="US Dollar" {...register("currencyName")} />
          {errors.currencyName && (
            <p className="text-xs text-destructive">{errors.currencyName.message}</p>
          )}
        </div>

        <div className="space-y-1">
          <Label htmlFor="rate-value">Rate to AED</Label>
          <Input
            id="rate-value"
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
          <Label htmlFor="rate-date">Effective Date</Label>
          <Input id="rate-date" type="date" {...register("effectiveDate")} />
          {errors.effectiveDate && (
            <p className="text-xs text-destructive">{errors.effectiveDate.message}</p>
          )}
        </div>

        {create.isError && (
          <p className="text-sm text-destructive">
            {(create.error as { response?: { data?: { message?: string } } })?.response
              ?.data?.message ?? "Failed to save"}
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
