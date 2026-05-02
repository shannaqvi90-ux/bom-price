import type { V3FinalPrice } from "@/types/api";

interface Props {
  finalPrice: V3FinalPrice;
}

export function FinalPriceSummary({ finalPrice }: Props) {
  return (
    <div className="rounded-lg border border-gray-200 bg-white p-4">
      <div className="text-xs font-bold uppercase tracking-wider text-gray-500">
        Quote Summary
      </div>
      <table className="mt-3 w-full text-sm">
        <thead>
          <tr className="text-left text-gray-500">
            <th className="px-1 py-1 font-medium">Finished Good</th>
            <th className="px-1 py-1 text-right font-medium">Qty (KG)</th>
            <th className="px-1 py-1 text-right font-medium">Sale/KG</th>
            <th className="px-1 py-1 text-right font-medium">Total</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-gray-100">
          {finalPrice.perFg.map((fg) => (
            <tr key={fg.requisitionItemId}>
              <td className="px-1 py-2 text-gray-900">{fg.description}</td>
              <td className="px-1 py-2 text-right text-gray-700">
                {fg.expectedQty.toLocaleString()}
              </td>
              <td className="px-1 py-2 text-right text-gray-700">
                {finalPrice.currencyCode} {fg.salePerKg.toFixed(2)}
              </td>
              <td className="px-1 py-2 text-right font-medium text-gray-900">
                AED{" "}
                {fg.totalAed.toLocaleString(undefined, { maximumFractionDigits: 2 })}
              </td>
            </tr>
          ))}
        </tbody>
        <tfoot>
          <tr className="border-t-2 border-gray-300">
            <td colSpan={3} className="px-1 py-3 text-right text-sm font-bold text-gray-900">
              GRAND TOTAL
            </td>
            <td className="px-1 py-3 text-right text-lg font-bold text-blue-700">
              AED{" "}
              {finalPrice.totalAed.toLocaleString(undefined, { maximumFractionDigits: 2 })}
            </td>
          </tr>
        </tfoot>
      </table>
      {finalPrice.rateSnapshot != null && finalPrice.currencyCode !== "AED" ? (
        <p className="mt-2 text-xs text-gray-500">
          Quote currency {finalPrice.currencyCode} converted to AED at rate{" "}
          {finalPrice.rateSnapshot.toFixed(4)} (snapped at margin save).
        </p>
      ) : null}
    </div>
  );
}
