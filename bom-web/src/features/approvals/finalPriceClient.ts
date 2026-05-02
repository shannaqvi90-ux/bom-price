// Pure compute, mirrors backend FinalPriceComputer.
// Used by MdMarginPage for live preview before margin set.
//
// Note on FX: backend re-snaps the exchange rate at set-margin time.
// During preview, callers pass rateSnapshot:null, in which case
// salePerKgAed equals salePerKg (i.e. expressed in quote currency).
// The grand total card label honestly reads "AED" only when rateSnapshot
// is provided; otherwise it represents quote-currency totals.

export interface FinalPriceInputItem {
  requisitionItemId: number;
  expectedQty: number;
  costPerKg: number;
  marginPerKg: number;
}

export interface FinalPriceInput {
  currencyCode: string;
  rateSnapshot: number | null;
  perFg: FinalPriceInputItem[];
}

export interface FinalPriceClientPerFg extends FinalPriceInputItem {
  salePerKg: number;
  salePerKgAed: number;
  totalAed: number;
}

export interface FinalPriceClientResult {
  totalAed: number;
  perFg: FinalPriceClientPerFg[];
}

export function computeFinalPrice(input: FinalPriceInput): FinalPriceClientResult {
  const perFg = input.perFg.map((fg) => {
    const salePerKg = fg.costPerKg + fg.marginPerKg;
    const salePerKgAed = input.rateSnapshot != null ? salePerKg * input.rateSnapshot : salePerKg;
    const totalAed = salePerKgAed * fg.expectedQty;
    return { ...fg, salePerKg, salePerKgAed, totalAed };
  });
  return {
    totalAed: perFg.reduce((s, p) => s + p.totalAed, 0),
    perFg,
  };
}
