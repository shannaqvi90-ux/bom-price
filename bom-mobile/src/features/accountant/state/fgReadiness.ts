export interface RawMaterialCostState {
  bomLineId: number;
  costPerKg: string;
  currencyCode: string;
}

export interface FgDraftState {
  requisitionItemId: number;
  hasPrinting: boolean;
  rawMaterialCosts: RawMaterialCostState[];
  printingCostPerKg: string;
  printingCostCurrency: string;
  fohPerKg: string;
  transportPerKg: string;
  commissionPerKg: string;
}

export type FgReadiness = "not_started" | "in_progress" | "ready";

function isPositive(s: string): boolean {
  if (s === "" || s === "-") return false;
  const n = parseFloat(s);
  return Number.isFinite(n) && n > 0;
}

function isNonEmpty(s: string): boolean {
  if (s === "" || s === "-") return false;
  const n = parseFloat(s);
  return Number.isFinite(n);
}

export function fgReadiness(s: FgDraftState): FgReadiness {
  const anyTouched =
    s.rawMaterialCosts.some((rc) => rc.costPerKg !== "" || rc.currencyCode !== "")
    || s.fohPerKg !== "" || s.transportPerKg !== "" || s.commissionPerKg !== ""
    || s.printingCostPerKg !== "" || s.printingCostCurrency !== "";
  if (!anyTouched) return "not_started";

  const allRmReady = s.rawMaterialCosts.every(
    (rc) => isPositive(rc.costPerKg) && rc.currencyCode !== ""
  );
  const printingReady = !s.hasPrinting
    || (isPositive(s.printingCostPerKg) && s.printingCostCurrency !== "");
  const otherReady = isNonEmpty(s.fohPerKg) && isNonEmpty(s.transportPerKg) && isNonEmpty(s.commissionPerKg);

  if (allRmReady && printingReady && otherReady) return "ready";
  return "in_progress";
}
