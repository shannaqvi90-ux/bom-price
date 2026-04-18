import { useForm } from "react-hook-form";
import type { Path } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { Link, useNavigate, useParams } from "react-router-dom";
import { ArrowLeft } from "lucide-react";
import { useEffect, useRef } from "react";

import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/Card";
import { Button } from "@/components/ui/Button";
import { useItems } from "@/api/lookups";
import { useRequisition, useResubmitRequisition } from "./requisitionsApi";
import { RequisitionItemsEditor } from "./components/RequisitionItemsEditor";
import { notify } from "@/lib/notify";
import { extractFieldErrors } from "@/lib/apiError";
import { useAuthStore } from "@/store/authStore";

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
});

type FormValues = z.infer<typeof schema>;

export default function EditRequisitionPage() {
  const { id } = useParams<{ id: string }>();
  const numericId = Number(id);
  const navigate = useNavigate();
  const detailQ = useRequisition(numericId);
  const itemsQ = useItems();
  const resubmit = useResubmitRequisition(numericId);
  const userId = useAuthStore((s) => s.user?.userId);

  const {
    control,
    handleSubmit,
    register,
    reset,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { items: [] },
  });

  const hydratedRef = useRef(false);

  useEffect(() => {
    if (!detailQ.data || hydratedRef.current) return;
    reset({
      items: detailQ.data.items.map((ri) => ({
        item: { id: ri.itemId },
        expectedQty: ri.expectedQty,
      })),
    });
    hydratedRef.current = true;
  }, [detailQ.data, reset]);

  if (detailQ.isLoading || itemsQ.isLoading) {
    return <p className="text-sm text-muted-foreground">Loading…</p>;
  }

  if (detailQ.isError || itemsQ.isError) {
    return (
      <Card className="mx-auto max-w-lg">
        <CardContent className="py-8 text-center text-destructive">
          Failed to load requisition.
        </CardContent>
      </Card>
    );
  }

  const r = detailQ.data!;

  if (r.status !== "Rejected") {
    return (
      <Card className="mx-auto max-w-lg">
        <CardContent className="py-8 text-center">
          <p className="text-sm">
            Cannot edit — requisition status is <strong>{r.status}</strong>.
          </p>
          <Link to={`/requisitions/${id}`} className="mt-4 inline-block text-sm underline">
            Back to requisition
          </Link>
        </CardContent>
      </Card>
    );
  }

  if (userId !== r.salesPersonId) {
    return (
      <Card className="mx-auto max-w-lg">
        <CardContent className="py-8 text-center">
          <p className="text-sm">Only the owning sales person can edit this requisition.</p>
          <Link to={`/requisitions/${id}`} className="mt-4 inline-block text-sm underline">
            Back to requisition
          </Link>
        </CardContent>
      </Card>
    );
  }

  const onSubmit = handleSubmit(async (values) => {
    try {
      await resubmit.mutateAsync({
        items: values.items.map((row) => ({
          itemId: row.item!.id,
          expectedQty: row.expectedQty,
        })),
      });
      notify.success("Requisition resubmitted");
      navigate(`/requisitions/${id}`, { replace: true });
    } catch (e) {
      const fields = extractFieldErrors(e);
      for (const [key, msg] of Object.entries(fields)) {
        setError(key as Path<FormValues>, { type: "server", message: msg });
      }
      notify.fromApiError(e, "Failed to resubmit requisition");
    }
  });

  return (
    <div className="mx-auto max-w-2xl space-y-4">
      <Link
        to={`/requisitions/${id}`}
        className="inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground"
      >
        <ArrowLeft className="h-4 w-4" /> Back to requisition
      </Link>

      <Card>
        <CardHeader>
          <CardTitle>Edit &amp; Resubmit {r.refNo}</CardTitle>
        </CardHeader>
        <CardContent className="space-y-4">
          {r.approval?.notes && (
            <div className="rounded-md border border-destructive/40 bg-destructive/5 p-3 text-sm">
              <p className="font-medium text-destructive">Previous rejection reason</p>
              <p className="mt-1 whitespace-pre-wrap">{r.approval.notes}</p>
            </div>
          )}

          <div className="text-sm text-muted-foreground">
            Customer: <span className="text-foreground">{r.customerName}</span> • Currency:{" "}
            <span className="text-foreground">{r.currencyCode}</span>
          </div>

          <form onSubmit={onSubmit} className="space-y-4" noValidate>
            <RequisitionItemsEditor
              control={control}
              register={register}
              errors={errors}
              availableItems={itemsQ.data ?? []}
            />
            <Button type="submit" disabled={isSubmitting || resubmit.isPending}>
              {resubmit.isPending ? "Resubmitting…" : "Resubmit for BOM"}
            </Button>
          </form>
        </CardContent>
      </Card>
    </div>
  );
}
