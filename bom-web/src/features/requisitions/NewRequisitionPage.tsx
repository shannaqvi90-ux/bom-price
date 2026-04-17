import { useForm, Controller, useFieldArray, useWatch } from "react-hook-form";
import type { Path } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { useNavigate } from "react-router-dom";
import { Plus, Trash2 } from "lucide-react";

import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/Card";
import { Button } from "@/components/ui/Button";
import { SearchableSelect } from "@/components/ui/SearchableSelect";
import { useCustomers, useItems, useActiveExchangeRates } from "@/api/lookups";
import { useCreateRequisition } from "./requisitionsApi";
import { notify } from "@/lib/notify";
import { extractFieldErrors } from "@/lib/apiError";
import type { Customer, Item } from "@/types/api";

const itemRowSchema = z.object({
  item: z
    .object({ id: z.number() })
    .nullable()
    .refine((v) => v !== null, { message: "Item is required" }),
  expectedQty: z
    .number({ invalid_type_error: "Qty is required" })
    .positive("Qty must be greater than zero"),
});

const schema = z.object({
  customer: z
    .object({ id: z.number() })
    .nullable()
    .refine((v) => v !== null, { message: "Customer is required" }),
  items: z
    .array(itemRowSchema)
    .min(1, "At least one item is required")
    .refine(
      (arr) => {
        const ids = arr
          .map((r) => r.item?.id)
          .filter((v): v is number => typeof v === "number");
        return new Set(ids).size === ids.length;
      },
      { message: "Duplicate items not allowed" },
    ),
  currencyCode: z.string().min(1, "Currency is required"),
});

type FormValues = z.infer<typeof schema>;

export default function NewRequisitionPage() {
  const navigate = useNavigate();
  const customersQ = useCustomers();
  const itemsQ = useItems();
  const ratesQ = useActiveExchangeRates();
  const create = useCreateRequisition();

  const {
    control,
    handleSubmit,
    register,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      customer: null as unknown as { id: number },
      items: [{ item: null as unknown as { id: number }, expectedQty: undefined as unknown as number }],
      currencyCode: "AED",
    },
  });

  const { fields, append, remove } = useFieldArray({ control, name: "items" });

  const watchedItems = useWatch({ control, name: "items" });

  const availableItemsFor = (rowIndex: number): Item[] => {
    const base = itemsQ.data ?? [];
    const takenIds = new Set(
      (watchedItems ?? [])
        .map((row, i) => (i !== rowIndex ? row?.item?.id : undefined))
        .filter((v): v is number => typeof v === "number"),
    );
    return base.filter((it) => !takenIds.has(it.id));
  };

  const isLoadingLookups = customersQ.isLoading || itemsQ.isLoading || ratesQ.isLoading;
  const isErrorLookups = customersQ.isError || itemsQ.isError || ratesQ.isError;

  const currencies = ["AED", ...(ratesQ.data?.map((r) => r.currencyCode) ?? [])];
  const uniqueCurrencies = Array.from(new Set(currencies)).map((code) => ({ code }));

  const onSubmit = handleSubmit(async (values) => {
    try {
      const created = await create.mutateAsync({
        customerId: values.customer!.id,
        items: values.items.map((row) => ({
          itemId: row.item!.id,
          expectedQty: row.expectedQty,
        })),
        currencyCode: values.currencyCode,
      });
      notify.success("Requisition created");
      navigate(`/requisitions/${created.id}`, { replace: true });
    } catch (e) {
      const fields = extractFieldErrors(e);
      for (const [key, msg] of Object.entries(fields)) {
        setError(key as Path<FormValues>, { type: "server", message: msg });
      }
      notify.fromApiError(e, "Failed to create requisition");
    }
  });

  return (
    <div className="mx-auto max-w-2xl space-y-4">
      <Card>
        <CardHeader>
          <CardTitle>New Requisition</CardTitle>
        </CardHeader>
        <CardContent>
          {isLoadingLookups ? (
            <p className="text-sm text-muted-foreground">Loading…</p>
          ) : isErrorLookups ? (
            <p className="text-sm text-destructive">Failed to load form data. Please refresh.</p>
          ) : (
            <form onSubmit={onSubmit} className="space-y-4" noValidate>
              <div className="space-y-2">
                <label htmlFor="customer" className="text-sm font-medium">
                  Customer
                </label>
                <Controller
                  control={control}
                  name="customer"
                  render={({ field }) => (
                    <SearchableSelect<Customer>
                      id="customer"
                      options={customersQ.data ?? []}
                      value={field.value as Customer | null}
                      onChange={field.onChange}
                      getLabel={(c) => c.name}
                      getValue={(c) => c.id}
                      placeholder="Search customers…"
                    />
                  )}
                />
                {errors.customer && (
                  <p className="text-xs text-destructive">{errors.customer.message as string}</p>
                )}
              </div>

              <div className="space-y-2">
                <label className="text-sm font-medium">Items</label>
                {fields.map((field, index) => (
                  <div key={field.id} className="flex items-start gap-2">
                    <div className="flex-1">
                      <Controller
                        control={control}
                        name={`items.${index}.item`}
                        render={({ field: f }) => (
                          <SearchableSelect<Item>
                            id={`item-${index}`}
                            options={availableItemsFor(index)}
                            value={f.value as Item | null}
                            onChange={f.onChange}
                            getLabel={(i) => i.description}
                            getValue={(i) => i.id}
                            placeholder="Search items…"
                          />
                        )}
                      />
                      {errors.items?.[index]?.item && (
                        <p className="text-xs text-destructive">
                          {errors.items[index].item?.message as string}
                        </p>
                      )}
                    </div>
                    <div className="w-32">
                      <input
                        type="number"
                        step="0.0001"
                        className="flex h-10 w-full rounded-md border border-input bg-background px-3 py-2 text-sm"
                        placeholder="Qty"
                        {...register(`items.${index}.expectedQty`, { valueAsNumber: true })}
                      />
                      {errors.items?.[index]?.expectedQty && (
                        <p className="text-xs text-destructive">
                          {errors.items[index].expectedQty?.message}
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
                    append({ item: null as unknown as { id: number }, expectedQty: undefined as unknown as number })
                  }
                >
                  <Plus className="mr-1 h-4 w-4" /> Add Item
                </Button>
                {errors.items?.root && (
                  <p className="text-xs text-destructive">{errors.items.root.message}</p>
                )}
              </div>

              <div className="space-y-2">
                <label htmlFor="currencyCode" className="text-sm font-medium">
                  Currency
                </label>
                <Controller
                  control={control}
                  name="currencyCode"
                  render={({ field }) => (
                    <SearchableSelect<{ code: string }>
                      id="currencyCode"
                      options={uniqueCurrencies}
                      value={field.value ? { code: field.value } : null}
                      onChange={(v) => field.onChange(v?.code ?? "")}
                      getLabel={(c) => c.code}
                      getValue={(c) => c.code}
                      placeholder="Select currency…"
                    />
                  )}
                />
                {errors.currencyCode && (
                  <p className="text-xs text-destructive">{errors.currencyCode.message}</p>
                )}
              </div>

              <Button type="submit" disabled={isSubmitting || create.isPending}>
                {create.isPending ? "Creating…" : "Create"}
              </Button>
            </form>
          )}
        </CardContent>
      </Card>
    </div>
  );
}
