import { useCallback, useMemo, useState } from "react";
import type { V3Requisition, V3FinishedGoodDto } from "../../../types/v3";
import { fgReadiness, type FgDraftState, type FgReadiness, type RawMaterialCostState } from "./fgReadiness";

function initFromFg(fg: V3FinishedGoodDto): FgDraftState {
  const existing = fg.costs ?? null;
  const lines = existing?.lines ?? [];
  return {
    requisitionItemId: fg.id,
    hasPrinting: fg.hasPrinting ?? false,
    rawMaterialCosts: (fg.bomLines ?? []).map((bl) => {
      const existingCost = lines.find((c) => c.bomLineId === bl.id);
      return {
        bomLineId: bl.id,
        costPerKg: existingCost?.purchaseValuePerKg != null ? String(existingCost.purchaseValuePerKg) : "",
        currencyCode: existingCost?.purchaseCurrency ?? "AED",
      };
    }),
    printingCostPerKg: existing?.printingCostPerKg != null ? String(existing.printingCostPerKg) : "",
    printingCostCurrency: existing?.printingCostCurrency ?? "AED",
    fohPerKg: existing?.fohPerKg != null ? String(existing.fohPerKg) : "",
    transportPerKg: existing?.transportPerKg != null ? String(existing.transportPerKg) : "",
    commissionPerKg: existing?.commissionPerKg != null ? String(existing.commissionPerKg) : "",
  };
}

export interface UseCostingDraftState {
  drafts: FgDraftState[];
  readiness: FgReadiness[];
  allReady: boolean;
  setFg: (idx: number, partial: Partial<FgDraftState>) => void;
  setRmCost: (fgIdx: number, rmIdx: number, partial: Partial<RawMaterialCostState>) => void;
  isDirtyVsBaseline: (idx: number, baseline: FgDraftState) => number;
}

export function useCostingDraftState(req: V3Requisition): UseCostingDraftState {
  const [drafts, setDrafts] = useState<FgDraftState[]>(() => req.finishedGoods.map(initFromFg));

  const readiness = useMemo(() => drafts.map(fgReadiness), [drafts]);
  const allReady = readiness.every((r) => r === "ready");

  const setFg = useCallback((idx: number, partial: Partial<FgDraftState>) => {
    setDrafts((prev) => {
      const next = [...prev];
      next[idx] = { ...next[idx], ...partial };
      return next;
    });
  }, []);

  const setRmCost = useCallback((fgIdx: number, rmIdx: number, partial: Partial<RawMaterialCostState>) => {
    setDrafts((prev) => {
      const next = [...prev];
      next[fgIdx] = { ...next[fgIdx], rawMaterialCosts: [...next[fgIdx].rawMaterialCosts] };
      next[fgIdx].rawMaterialCosts[rmIdx] = { ...next[fgIdx].rawMaterialCosts[rmIdx], ...partial };
      return next;
    });
  }, []);

  const isDirtyVsBaseline = useCallback((idx: number, baseline: FgDraftState) => {
    const cur = drafts[idx];
    let diff = 0;
    if (cur.fohPerKg !== baseline.fohPerKg) diff++;
    if (cur.transportPerKg !== baseline.transportPerKg) diff++;
    if (cur.commissionPerKg !== baseline.commissionPerKg) diff++;
    if (cur.printingCostPerKg !== baseline.printingCostPerKg) diff++;
    if (cur.printingCostCurrency !== baseline.printingCostCurrency) diff++;
    cur.rawMaterialCosts.forEach((rc, i) => {
      const b = baseline.rawMaterialCosts[i];
      if (b && rc.costPerKg !== b.costPerKg) diff++;
      if (b && rc.currencyCode !== b.currencyCode) diff++;
    });
    return diff;
  }, [drafts]);

  return { drafts, readiness, allReady, setFg, setRmCost, isDirtyVsBaseline };
}
