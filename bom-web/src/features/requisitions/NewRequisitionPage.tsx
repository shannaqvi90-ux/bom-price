import { useState } from "react";
import { useForm, Controller } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { useNavigate } from "react-router-dom";

import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/Card";
import { Button } from "@/components/ui/Button";
import { SearchableSelect } from "@/components/ui/SearchableSelect";
import { useCustomers, useItems, useActiveExchangeRates } from "@/api/lookups";
import { useCreateRequisition } from "./requisitionsApi";
import type { Customer, Item } from "@/types/api";

const schema = z.object({
  customer: z
    .object({ id: z.number() })
    .nullable()
    .refine((v) => v !== null, { message: "Customer is required" }),
  item: z
    .object({ id: z.number() })
    .nullable()
    .refine((v) => v !== null, { message: "Item is required" }),
  expectedQty: z
    .number({ invalid_type_error: "Expected qty is required" })
    .positive("Qty must be greater than zero"),
  currencyCode: z.string().min(1, "Currency is required"),
});

type FormValues = z.infer<typeof schema>;

export default function NewRequisitionPage() {
  const navigate = useNavigate();
  const customersQ = useCustomers();
  const itemsQ = useItems();
  const ratesQ = useActiveExchangeRates();
  const create = useCreateRequisition();
  const [serverError, setServerError] = useState<string | null>(null);

  const {
    control,
    handleSubmit,
    register,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      customer: null as unknown as { id: number },
      item: null as unknown as { id: number },
      expectedQty: undefined as unknown as number,
      currencyCode: "AED",
    },
  });

  const isLoadingLookups = customersQ.isLoading || itemsQ.isLoading || ratesQ.isLoading;
  const isErrorLookups = customersQ.isError || itemsQ.isError || ratesQ.isError;

  const currencies = ["AED", ...(ratesQ.data?.map((r) => r.currencyCode) ?? [])];
  const uniqueCurrencies = Array.from(new Set(currencies)).map((code) => ({ code }));

  const onSubmit = handleSubmit(async (values) => {
    setServerError(null);
    try {
      const created = await create.mutateAsync({
        customerId: values.customer!.id,
        itemId: values.item!.id,
        expectedQty: values.expectedQty,
        currencyCode: values.currencyCode,
      });
      navigate(`/requisitions/${created.id}`, { replace: true });
    } catch (e) {
      const msg =
        (e as { response?: { data?: { message?: string } } }).response?.data?.message ??
        "Failed to create requisition";
      setServerError(msg);
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
                <label htmlFor="item" className="text-sm font-medium">
                  Item
                </label>
                <Controller
                  control={control}
                  name="item"
                  render={({ field }) => (
                    <SearchableSelect<Item>
                      id="item"
                      options={itemsQ.data ?? []}
                      value={field.value as Item | null}
                      onChange={field.onChange}
                      getLabel={(i) => i.description}
                      getValue={(i) => i.id}
                      placeholder="Search items…"
                    />
                  )}
                />
                {errors.item && (
                  <p className="text-xs text-destructive">{errors.item.message as string}</p>
                )}
              </div>

              <div className="space-y-2">
                <label htmlFor="expectedQty" className="text-sm font-medium">
                  Expected Qty
                </label>
                <input
                  id="expectedQty"
                  type="number"
                  step="0.0001"
                  className="flex h-10 w-full rounded-md border border-input bg-background px-3 py-2 text-sm"
                  {...register("expectedQty", { valueAsNumber: true })}
                />
                {errors.expectedQty && (
                  <p className="text-xs text-destructive">{errors.expectedQty.message}</p>
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

              {serverError && (
                <p className="text-sm text-destructive">{serverError}</p>
              )}

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
