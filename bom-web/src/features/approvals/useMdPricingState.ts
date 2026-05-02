import { useMemo, useState } from "react";
import type { V3Requisition } from "@/types/api";
import { computeFinalPrice } from "./finalPriceClient";

export function useMdPricingState(req: V3Requisition) {
  const [margins, setMargins] = useState<Record<number, string>>({});
  const [notes, setNotes] = useState("");

  const setMargin = (riId: number, value: string) =>
    setMargins((m) => ({ ...m, [riId]: value }));

  const parsed = useMemo(() => {
    const result: Record<number, number | null> = {};
    for (const fg of req.finishedGoods) {
      const raw = margins[fg.id] ?? "";
      if (raw.trim() === "") {
        result[fg.id] = null;
      } else {
        const n = parseFloat(raw);
        result[fg.id] = isNaN(n) || n < 0 ? null : n;
      }
    }
    return result;
  }, [margins, req.finishedGoods]);

  const isValid = req.finishedGoods.every((fg) => parsed[fg.id] != null);

  const livePreview = useMemo(() => {
    if (!isValid) return null;
    return computeFinalPrice({
      currencyCode: req.currencyCode,
      rateSnapshot: null,
      perFg: req.finishedGoods.map((fg) => ({
        requisitionItemId: fg.id,
        expectedQty: fg.expectedQty,
        costPerKg: fg.costs?.totalCostPerKg ?? 0,
        marginPerKg: parsed[fg.id]!,
      })),
    });
  }, [parsed, req, isValid]);

  return { margins, setMargin, notes, setNotes, isValid, livePreview, parsed };
}
