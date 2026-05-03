import type { V3FinishedGood } from "@/types/api";

interface Props {
  fg: V3FinishedGood;
  index: number;
  marginInput: string;
  onMarginChange: (value: string) => void;
  livePerFg: { salePerKg: number; totalAed: number } | null;
  currencyCode: string;
}

export function MdFgPricingCard({
  fg,
  index,
  marginInput,
  onMarginChange,
  livePerFg,
  currencyCode,
}: Props) {
  const costs = fg.costs;
  const costPerKg = costs?.totalCostPerKg ?? 0;

  return (
    <div className="rounded-lg border border-border bg-card p-4 shadow-sm">
      <div className="flex items-start justify-between gap-3">
        <div>
          <div className="text-xs font-bold uppercase tracking-wider text-muted-foreground">
            FG {index + 1}
          </div>
          <div className="mt-1 text-base font-semibold text-foreground">
            {fg.item.description}
          </div>
          <div className="mt-0.5 text-xs text-muted-foreground">
            {fg.item.code} · {fg.expectedQty.toLocaleString()} KG
          </div>
        </div>
        <div className="text-right">
          <div className="text-xs text-muted-foreground">Cost/KG</div>
          <div className="text-lg font-semibold text-foreground">
            {currencyCode} {costPerKg.toFixed(2)}
          </div>
        </div>
      </div>

      {fg.bomLines && fg.bomLines.length > 0 ? (
        <details className="mt-3 rounded-md border border-border bg-muted px-3 py-2">
          <summary className="cursor-pointer text-xs font-medium text-muted-foreground">
            BOM lines ({fg.bomLines.length})
          </summary>
          <table className="mt-2 w-full text-xs">
            <thead className="text-muted-foreground">
              <tr>
                <th className="px-1 py-1 text-left font-medium">Raw Material</th>
                <th className="px-1 py-1 text-right font-medium">Qty/KG</th>
                <th className="px-1 py-1 text-right font-medium">Wastage %</th>
                <th className="px-1 py-1 text-right font-medium">Cost/KG</th>
                <th className="px-1 py-1 text-right font-medium">Total/KG</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-border">
              {fg.bomLines.map((line) => {
                const lineCost = costs?.lines.find((l) => l.bomLineId === line.id);
                const wastageMult =
                  lineCost?.wastagePercent != null
                    ? 1 + lineCost.wastagePercent / 100
                    : 1;
                const lineTotal =
                  lineCost?.purchaseValuePerKg != null
                    ? lineCost.purchaseValuePerKg * line.qtyPerKg * wastageMult
                    : null;
                return (
                  <tr key={line.id}>
                    <td className="px-1 py-1 text-foreground">{line.item.description}</td>
                    <td className="px-1 py-1 text-right text-foreground">
                      {line.qtyPerKg.toFixed(4)}
                    </td>
                    <td className="px-1 py-1 text-right text-foreground">
                      {lineCost?.wastagePercent != null
                        ? `${lineCost.wastagePercent.toFixed(2)}%`
                        : "—"}
                    </td>
                    <td className="px-1 py-1 text-right text-foreground">
                      {lineCost?.purchaseValuePerKg != null
                        ? `${lineCost.purchaseCurrency ?? ""} ${lineCost.purchaseValuePerKg.toFixed(2)}`
                        : "—"}
                    </td>
                    <td className="px-1 py-1 text-right font-medium text-foreground">
                      {lineTotal != null
                        ? `${lineCost?.purchaseCurrency ?? currencyCode} ${lineTotal.toFixed(2)}`
                        : "—"}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
          {costs ? (
            <div className="mt-2 grid grid-cols-2 gap-x-4 gap-y-1 border-t border-border pt-2 text-xs text-muted-foreground">
              {costs.printingCostPerKg != null ? (
                <div className="flex justify-between">
                  <span>Printing/KG</span>
                  <span className="font-medium">
                    {costs.printingCostCurrency ?? currencyCode}{" "}
                    {costs.printingCostPerKg.toFixed(2)}
                  </span>
                </div>
              ) : null}
              <div className="flex justify-between">
                <span>FOH/KG</span>
                <span className="font-medium">
                  {currencyCode} {costs.fohPerKg.toFixed(2)}
                </span>
              </div>
              <div className="flex justify-between">
                <span>Transport/KG</span>
                <span className="font-medium">
                  {currencyCode} {costs.transportPerKg.toFixed(2)}
                </span>
              </div>
              <div className="flex justify-between">
                <span>Commission/KG</span>
                <span className="font-medium">
                  {currencyCode} {costs.commissionPerKg.toFixed(2)}
                </span>
              </div>
            </div>
          ) : null}
        </details>
      ) : null}

      <div className="mt-3 flex items-center gap-3 rounded-md bg-blue-50 px-3 py-2 dark:bg-blue-900/30">
        <label className="flex-1 text-sm font-medium text-blue-900 dark:text-blue-300">
          Margin/KG ({currencyCode})
        </label>
        {(() => {
          const marginNum = parseFloat(marginInput);
          const showPct = costPerKg > 0 && !isNaN(marginNum) && marginNum >= 0;
          const salePerKg = costPerKg + marginNum;
          const pct = showPct && salePerKg > 0 ? (marginNum / salePerKg) * 100 : null;
          return pct != null ? (
            <span className="text-xs font-semibold text-blue-700 dark:text-blue-300">
              {pct.toFixed(1)}%
            </span>
          ) : null;
        })()}
        <input
          type="number"
          step="0.01"
          value={marginInput}
          onChange={(e) => onMarginChange(e.target.value)}
          placeholder="0.00"
          className="w-32 rounded border border-input bg-background px-2 py-1 text-right text-sm"
        />
      </div>

      {livePerFg ? (
        <div className="mt-2 flex items-center justify-between rounded-md bg-emerald-50 px-3 py-2 text-sm dark:bg-emerald-900/30">
          <div>
            <span className="text-foreground">Sale/KG</span>{" "}
            <span className="font-semibold text-foreground">
              {currencyCode} {livePerFg.salePerKg.toFixed(2)}
            </span>
          </div>
          <div>
            <span className="text-foreground">Total</span>{" "}
            <span className="font-semibold text-foreground">
              {currencyCode}{" "}
              {livePerFg.totalAed.toLocaleString(undefined, { maximumFractionDigits: 2 })}
            </span>
          </div>
        </div>
      ) : null}
    </div>
  );
}
