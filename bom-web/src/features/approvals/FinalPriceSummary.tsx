import type { V3FinalPrice } from "@/types/api";

interface Props {
  finalPrice: V3FinalPrice;
  /** When true, shows a "preview — not yet finalized" caption.
   *  Pass true for MdPricing/CustomerConfirm/MdFinalSign statuses;
   *  false (default) for Signed. */
  previewMode?: boolean;
}

export function FinalPriceSummary({ finalPrice, previewMode = false }: Props) {
  return (
    <div className="rounded-lg border border-border bg-card p-4">
      <div className="flex items-baseline justify-between">
        <div className="text-xs font-bold uppercase tracking-wider text-muted-foreground">
          Quote Summary{previewMode ? " (preview)" : ""}
        </div>
        {previewMode ? (
          <div className="text-[11px] text-amber-700 dark:text-amber-300">
            Subject to MD final-sign — not yet locked
          </div>
        ) : null}
      </div>
      <table className="mt-3 w-full text-sm">
        <thead>
          <tr className="text-left text-muted-foreground">
            <th className="px-1 py-1 font-medium">Finished Good</th>
            <th className="px-1 py-1 text-right font-medium">Qty (KG)</th>
            <th className="px-1 py-1 text-right font-medium">Sale/KG</th>
            <th className="px-1 py-1 text-right font-medium">Total</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-border">
          {finalPrice.perFg.map((fg) => (
            <tr key={fg.requisitionItemId}>
              <td className="px-1 py-2 text-foreground">{fg.description}</td>
              <td className="px-1 py-2 text-right text-foreground">
                {fg.expectedQty.toLocaleString()}
              </td>
              <td className="px-1 py-2 text-right text-foreground">
                {finalPrice.currencyCode} {fg.salePerKg.toFixed(2)}
              </td>
              <td className="px-1 py-2 text-right font-medium text-foreground">
                AED{" "}
                {fg.totalAed.toLocaleString(undefined, { maximumFractionDigits: 2 })}
              </td>
            </tr>
          ))}
        </tbody>
        <tfoot>
          <tr className="border-t-2 border-border">
            <td colSpan={3} className="px-1 py-3 text-right text-sm font-bold text-foreground">
              GRAND TOTAL
            </td>
            <td className="px-1 py-3 text-right text-lg font-bold text-blue-700 dark:text-blue-300">
              AED{" "}
              {finalPrice.totalAed.toLocaleString(undefined, { maximumFractionDigits: 2 })}
            </td>
          </tr>
        </tfoot>
      </table>
      {finalPrice.rateSnapshot != null && finalPrice.currencyCode !== "AED" ? (
        <p className="mt-2 text-xs text-muted-foreground">
          Quote currency {finalPrice.currencyCode} converted to AED at rate{" "}
          {finalPrice.rateSnapshot.toFixed(4)} (snapped at margin save).
        </p>
      ) : null}
    </div>
  );
}
