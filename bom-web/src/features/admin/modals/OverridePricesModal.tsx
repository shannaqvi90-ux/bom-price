import { useEffect, useMemo, useState } from "react";
import {
  useCurrentApproval,
  useOverridePrices,
  type CurrentApprovalItem,
  type OverridePricesItemPayload,
} from "@/api/admin";
import { Button } from "@/components/ui/Button";
import { Dialog } from "@/components/ui/Dialog";

interface Props {
  requisition: { id: number; refNo: string };
  onClose: () => void;
}

interface Row {
  requisitionItemId: number;
  itemDescription: string;
  expectedQty: number;
  salesPricePerKgAed: string;
  salesPricePerKgForeign: string;
  profitMarginPct: string;
  materialCostPct: string;
  otherCostPct: string;
}

const PERCENT_SUM_TOLERANCE = 0.01;

function toRow(item: CurrentApprovalItem): Row {
  return {
    requisitionItemId: item.requisitionItemId,
    itemDescription: item.itemDescription,
    expectedQty: item.expectedQty,
    salesPricePerKgAed: String(item.salesPricePerKgAed),
    salesPricePerKgForeign:
      item.salesPricePerKgForeign === null ? "" : String(item.salesPricePerKgForeign),
    profitMarginPct: String(item.profitMarginPct),
    materialCostPct: String(item.materialCostPct),
    otherCostPct: String(item.otherCostPct),
  };
}

function parseNum(s: string): number | null {
  if (s.trim() === "") return null;
  const n = Number(s);
  return Number.isFinite(n) ? n : null;
}

interface RowError {
  message: string;
}

function validateRow(row: Row, isForeignRequired: boolean): RowError | null {
  const aed = parseNum(row.salesPricePerKgAed);
  const margin = parseNum(row.profitMarginPct);
  const mat = parseNum(row.materialCostPct);
  const other = parseNum(row.otherCostPct);
  const foreign = parseNum(row.salesPricePerKgForeign);

  if (aed === null || margin === null || mat === null || other === null) {
    return { message: "All numeric fields are required" };
  }
  if (aed < 0 || margin < 0 || mat < 0 || other < 0 || (foreign !== null && foreign < 0)) {
    return { message: "Negative values not allowed" };
  }
  if (isForeignRequired && foreign === null) {
    return { message: "Foreign price required for non-AED currency" };
  }
  const sum = margin + mat + other;
  if (Math.abs(sum - 100) > PERCENT_SUM_TOLERANCE) {
    return { message: `Margin + Mat + Other must sum to 100 (currently ${sum.toFixed(2)})` };
  }
  return null;
}

export function OverridePricesModal({ requisition, onClose }: Props) {
  const { data: approval, isLoading, isError, error: fetchError } = useCurrentApproval(requisition.id);
  const mutation = useOverridePrices(requisition.id);

  const [reason, setReason] = useState("");
  const [rows, setRows] = useState<Row[]>([]);
  const [hasInitialized, setHasInitialized] = useState(false);

  const isForeignRequired = approval?.currencyCode !== "AED" && approval !== undefined;

  // Initialize editable rows once when the async-fetched approval arrives.
  // The hasInitialized flag prevents subsequent fetches from clobbering
  // edits the user has made in-place. A "key on approval.id" pattern in
  // the parent would also work but would require routing the prop down a
  // dedicated wrapper component.
  useEffect(() => {
    if (approval && !hasInitialized) {
      /* eslint-disable react-hooks/set-state-in-effect -- intentional one-time init from async-fetched approval */
      setRows(approval.items.map(toRow));
      setHasInitialized(true);
      /* eslint-enable react-hooks/set-state-in-effect */
    }
  }, [approval, hasInitialized]);

  const rowErrors = useMemo(() => {
    if (rows.length === 0 || !approval) return [];
    return rows.map((r) => validateRow(r, isForeignRequired));
  }, [rows, approval, isForeignRequired]);

  const reasonValid = reason.trim().length >= 5;
  const allRowsValid = rowErrors.every((e) => e === null);
  const canSubmit = !!approval && reasonValid && allRowsValid && rows.length > 0;

  function updateRow(idx: number, patch: Partial<Row>) {
    setRows((prev) => prev.map((r, i) => (i === idx ? { ...r, ...patch } : r)));
  }

  async function handleSubmit() {
    if (!canSubmit) return;
    const payload: OverridePricesItemPayload[] = rows.map((r) => ({
      requisitionItemId: r.requisitionItemId,
      salesPricePerKgAed: parseNum(r.salesPricePerKgAed)!,
      salesPricePerKgForeign: isForeignRequired ? parseNum(r.salesPricePerKgForeign) : null,
      profitMarginPct: parseNum(r.profitMarginPct)!,
      materialCostPct: parseNum(r.materialCostPct)!,
      otherCostPct: parseNum(r.otherCostPct)!,
    }));
    try {
      await mutation.mutateAsync({ reason: reason.trim(), items: payload });
      onClose();
    } catch {
      // surfaced via mutation.error below
    }
  }

  const submitError = mutation.error
    ? (mutation.error as { response?: { data?: { error?: string } } }).response?.data?.error ??
      (mutation.error as Error).message
    : null;

  const fetchErrText = isError
    ? (fetchError as { response?: { data?: { error?: string } } } | null)?.response?.data?.error ??
      (fetchError as Error | null)?.message ??
      "Failed to load current approval"
    : null;

  return (
    <Dialog
      open
      title={`Override ${requisition.refNo} — supersede approval`}
      onClose={onClose}
      className="max-w-5xl"
    >
      <p className="mb-3 text-sm text-amber-800">
        This will mark the current approval as superseded and create a new approval with your
        prices. Status remains <strong>Approved</strong>. A new PDF will be generated and emailed
        to the SP (not the customer) for forwarding. Exchange rate will be re-snapped server-side
        to today's active rate{approval?.currencyCode && approval.currencyCode !== "AED"
          ? ` for ${approval.currencyCode}`
          : ""}.
      </p>

      {isLoading && (
        <div className="py-6 text-center text-sm text-muted-foreground">Loading current approval…</div>
      )}

      {fetchErrText && !isLoading && (
        <div className="mb-3 rounded border border-red-300 bg-red-50 p-3 text-sm text-red-700">
          {fetchErrText}
        </div>
      )}

      {approval && hasInitialized && (
        <>
          <div className="mb-2 text-xs text-muted-foreground">
            Currency: <span className="font-medium">{approval.currencyCode}</span>
            {approval.rateSnapshot !== null && (
              <>
                {" · "}Original rate snapshot: <span className="font-mono">{approval.rateSnapshot}</span>
              </>
            )}
          </div>

          <div className="overflow-x-auto rounded border border-border">
            <table className="w-full text-xs">
              <thead className="bg-muted/50">
                <tr>
                  <th className="px-2 py-2 text-left font-medium">#</th>
                  <th className="px-2 py-2 text-left font-medium">Item</th>
                  <th className="px-2 py-2 text-right font-medium">Qty</th>
                  <th className="px-2 py-2 text-right font-medium">Sales/Kg AED</th>
                  <th className="px-2 py-2 text-right font-medium">
                    Sales/Kg {approval.currencyCode !== "AED" ? approval.currencyCode : "(n/a)"}
                  </th>
                  <th className="px-2 py-2 text-right font-medium">Margin %</th>
                  <th className="px-2 py-2 text-right font-medium">Mat %</th>
                  <th className="px-2 py-2 text-right font-medium">Other %</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((row, idx) => {
                  const err = rowErrors[idx];
                  return (
                    <Row
                      key={row.requisitionItemId}
                      idx={idx}
                      row={row}
                      err={err}
                      foreignDisabled={!isForeignRequired}
                      onChange={(patch) => updateRow(idx, patch)}
                    />
                  );
                })}
              </tbody>
            </table>
          </div>

          <label className="mt-3 block">
            <span className="text-sm font-medium">Reason (required, min 5 chars)</span>
            <textarea
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              className="mt-1 w-full rounded border border-input bg-background p-2 text-sm"
              rows={2}
              placeholder="Why are prices being overridden?"
            />
          </label>

          {submitError && <p className="mt-2 text-sm text-red-600">Error: {submitError}</p>}
        </>
      )}

      <div className="mt-4 flex justify-end gap-2">
        <Button variant="outline" onClick={onClose}>
          Cancel
        </Button>
        <Button disabled={!canSubmit || mutation.isPending} onClick={handleSubmit}>
          {mutation.isPending ? "Submitting…" : "Override Prices"}
        </Button>
      </div>
    </Dialog>
  );
}

function Row({
  idx,
  row,
  err,
  foreignDisabled,
  onChange,
}: {
  idx: number;
  row: Row;
  err: RowError | null;
  foreignDisabled: boolean;
  onChange: (patch: Partial<Row>) => void;
}) {
  const numCell = (
    value: string,
    onValue: (v: string) => void,
    extraProps: React.InputHTMLAttributes<HTMLInputElement> = {},
  ) => (
    <td className="px-1 py-1">
      <input
        type="number"
        step="any"
        min={0}
        value={value}
        onChange={(e) => onValue(e.target.value)}
        className="w-full rounded border border-input bg-background px-2 py-1 text-right text-xs"
        {...extraProps}
      />
    </td>
  );

  return (
    <>
      <tr className={err ? "border-t border-red-200 bg-red-50/50" : "border-t border-border"}>
        <td className="px-2 py-1 text-muted-foreground">{idx + 1}</td>
        <td className="px-2 py-1">{row.itemDescription || `#${row.requisitionItemId}`}</td>
        <td className="px-2 py-1 text-right text-muted-foreground">{row.expectedQty}</td>
        {numCell(row.salesPricePerKgAed, (v) => onChange({ salesPricePerKgAed: v }))}
        {numCell(
          row.salesPricePerKgForeign,
          (v) => onChange({ salesPricePerKgForeign: v }),
          { disabled: foreignDisabled, placeholder: foreignDisabled ? "n/a" : undefined },
        )}
        {numCell(row.profitMarginPct, (v) => onChange({ profitMarginPct: v }))}
        {numCell(row.materialCostPct, (v) => onChange({ materialCostPct: v }))}
        {numCell(row.otherCostPct, (v) => onChange({ otherCostPct: v }))}
      </tr>
      {err && (
        <tr className="bg-red-50/50">
          <td colSpan={8} className="px-2 py-1 text-xs text-red-700">
            {err.message}
          </td>
        </tr>
      )}
    </>
  );
}
