import { useEffect, useMemo, useRef, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { ArrowLeft } from "lucide-react";
import { Button } from "@/components/ui/Button";
import { Card, CardContent } from "@/components/ui/Card";
import { useActiveExchangeRates } from "@/api/lookups";
import { useRequisition } from "@/features/requisitions/requisitionsApi";
import {
  useCosting,
  useStartCosting,
  useSaveCostingDraft,
  useSubmitCosting,
} from "./costingApi";
import type { CostingBomLine, LandedCostType } from "@/types/api";

interface LocalCostLine {
  bomLineId: number;
  processId: number;
  processName: string;
  rawMaterialItemId: number;
  rawMaterialDescription: string;
  qtyPerKg: number;
  wastagePct: number;
  costPerKg: number;
  currencyCode: string;
  lastCost: { costPerKg: number; currencyCode: string; updatedAt: string } | null;
}

const STALE_DAYS = 10;
const DEBOUNCE_MS = 800;

function daysSince(iso: string): number {
  const diff = Date.now() - new Date(iso).getTime();
  return Math.floor(diff / 86_400_000);
}

function rateToAed(currency: string, rates: { currencyCode: string; rateToAed: number }[]): number | null {
  if (currency === "AED") return 1;
  const row = rates.find((r) => r.currencyCode === currency);
  return row ? row.rateToAed : null;
}

function convert(amount: number, from: string, to: string, rates: { currencyCode: string; rateToAed: number }[]): number | null {
  const fromRate = rateToAed(from, rates);
  const toRate = rateToAed(to, rates);
  if (fromRate === null || toRate === null) return null;
  return (amount * fromRate) / toRate;
}

export default function CostingEntryPage() {
  const { id } = useParams<{ id: string }>();
  const requisitionId = Number(id);
  const navigate = useNavigate();

  const { data: requisition } = useRequisition(requisitionId);
  const { data: costing, isLoading: costingLoading, refetch } = useCosting(requisitionId);
  const { data: exchangeRates = [] } = useActiveExchangeRates();

  const startCosting = useStartCosting();
  const saveDraft = useSaveCostingDraft();
  const submitCosting = useSubmitCosting();

  const [lines, setLines] = useState<LocalCostLine[]>([]);
  const [landedCostType, setLandedCostType] = useState<LandedCostType>("Percentage");
  const [landedCostValue, setLandedCostValue] = useState<number>(0);
  const [fohAmount, setFohAmount] = useState<number>(0);
  const [saveStatus, setSaveStatus] = useState<"idle" | "saving" | "saved" | "error">("idle");
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [hydrated, setHydrated] = useState(false);
  const hasStartedRef = useRef(false);
  const debounceRef = useRef<number | undefined>(undefined);

  // Auto-start when CostingPending
  useEffect(() => {
    if (
      requisition?.status === "CostingPending" &&
      !hasStartedRef.current &&
      !startCosting.isPending
    ) {
      hasStartedRef.current = true;
      startCosting.mutate(requisitionId, { onSuccess: () => refetch() });
    }
  }, [requisition?.status, requisitionId]);

  // Hydrate local state from server once
  useEffect(() => {
    if (!costing || hydrated) return;
    const quoteCurrency = requisition?.currencyCode ?? "AED";

    const draftByLineId = new Map(
      (costing.draft?.lines ?? []).map((l) => [l.bomLineId, l]),
    );

    const local: LocalCostLine[] = costing.bomLines.map((bl: CostingBomLine) => {
      const draftLine = draftByLineId.get(bl.bomLineId);
      return {
        bomLineId: bl.bomLineId,
        processId: bl.processId,
        processName: bl.processName,
        rawMaterialItemId: bl.rawMaterialItemId,
        rawMaterialDescription: bl.rawMaterialDescription,
        qtyPerKg: bl.qtyPerKg,
        wastagePct: bl.wastagePct,
        costPerKg: draftLine?.costPerKg ?? bl.lastCost?.costPerKg ?? 0,
        currencyCode:
          draftLine?.currencyCode ?? bl.lastCost?.currencyCode ?? quoteCurrency,
        lastCost: bl.lastCost,
      };
    });
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setLines(local);

    if (costing.draft) {
      setLandedCostType(costing.draft.landedCostType);
      setLandedCostValue(costing.draft.landedCostValue);
      setFohAmount(costing.draft.fohAmount);
    }
    setHydrated(true);
  }, [costing, hydrated, requisition]);

  const quoteCurrency = requisition?.currencyCode ?? "AED";
  const currencyOptions = useMemo(() => {
    const codes = new Set(exchangeRates.map((r) => r.currencyCode));
    codes.add("AED");
    return Array.from(codes).sort();
  }, [exchangeRates]);

  const isReadOnly =
    requisition !== undefined &&
    requisition.status !== "CostingPending" &&
    requisition.status !== "CostingInProgress";

  // ── Auto-save ──
  function triggerAutoSave(nextLines: LocalCostLine[], nextType: LandedCostType, nextValue: number, nextFoh: number) {
    if (isReadOnly) return;
    if (debounceRef.current) window.clearTimeout(debounceRef.current);
    debounceRef.current = window.setTimeout(() => {
      setSaveStatus("saving");
      saveDraft.mutate(
        {
          requisitionId,
          payload: {
            lines: nextLines.map((l) => ({
              bomLineId: l.bomLineId,
              costPerKg: l.costPerKg,
              currencyCode: l.currencyCode,
            })),
            landedCostType: nextType,
            landedCostValue: nextValue,
            fohAmount: nextFoh,
          },
        },
        {
          onSuccess: () => setSaveStatus("saved"),
          onError: () => setSaveStatus("error"),
        },
      );
    }, DEBOUNCE_MS);
  }

  function updateLine(bomLineId: number, patch: Partial<LocalCostLine>) {
    const next = lines.map((l) => (l.bomLineId === bomLineId ? { ...l, ...patch } : l));
    setLines(next);
    triggerAutoSave(next, landedCostType, landedCostValue, fohAmount);
  }

  function updateLandedType(v: LandedCostType) {
    setLandedCostType(v);
    triggerAutoSave(lines, v, landedCostValue, fohAmount);
  }
  function updateLandedValue(v: number) {
    setLandedCostValue(v);
    triggerAutoSave(lines, landedCostType, v, fohAmount);
  }
  function updateFoh(v: number) {
    setFohAmount(v);
    triggerAutoSave(lines, landedCostType, landedCostValue, v);
  }

  // ── Totals (live preview in quote currency) ──
  const totals = useMemo(() => {
    let rawTotal = 0;
    for (const l of lines) {
      const inQuote = convert(l.costPerKg, l.currencyCode, quoteCurrency, exchangeRates);
      if (inQuote === null) continue;
      rawTotal += inQuote * l.qtyPerKg * (1 + l.wastagePct / 100);
    }
    const landed =
      landedCostType === "Percentage" ? (rawTotal * landedCostValue) / 100 : landedCostValue;
    const total = rawTotal + landed + fohAmount;
    return { rawTotal, landed, foh: fohAmount, total };
  }, [lines, landedCostType, landedCostValue, fohAmount, quoteCurrency, exchangeRates]);

  // Group by process for display
  const processGroups = useMemo(() => {
    const order: { processId: number; processName: string }[] = [];
    const seen = new Set<number>();
    for (const l of lines) {
      if (!seen.has(l.processId)) {
        seen.add(l.processId);
        order.push({ processId: l.processId, processName: l.processName });
      }
    }
    return order;
  }, [lines]);

  const canSubmit = lines.length > 0 && lines.every((l) => l.costPerKg > 0);

  function handleSubmit() {
    setSubmitError(null);
    submitCosting.mutate(
      {
        requisitionId,
        payload: {
          rawMaterialCosts: lines.map((l) => ({
            bomLineId: l.bomLineId,
            costPerKg: l.costPerKg,
            currencyCode: l.currencyCode,
          })),
          landedCostType,
          landedCostValue,
          fohAmount,
        },
      },
      {
        onSuccess: () => navigate(`/requisitions/${requisitionId}`),
        onError: (err: unknown) => {
          const e = err as { response?: { status?: number; data?: { message?: string } } };
          if (e.response?.status === 400 && e.response.data?.message) {
            setSubmitError(e.response.data.message);
          } else {
            setSubmitError("Failed to submit costing.");
          }
        },
      },
    );
  }

  // ── Render ──
  if (startCosting.isError) {
    return (
      <Card className="mx-auto max-w-lg">
        <CardContent className="py-8 text-center text-destructive">
          Failed to start costing. Please go back and try again.
        </CardContent>
      </Card>
    );
  }

  if (costingLoading || startCosting.isPending || !requisition || !costing) {
    return <p className="text-sm text-muted-foreground">Loading costing…</p>;
  }

  return (
    <div className="space-y-4">
      <Link
        to={`/requisitions/${requisitionId}`}
        className="inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground"
      >
        <ArrowLeft className="h-4 w-4" /> Back to {requisition.refNo}
      </Link>

      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">
            {isReadOnly ? "Costing (read-only)" : "Costing Entry"}
          </h1>
          <p className="mt-1 text-sm text-muted-foreground">
            {requisition.itemDescription} — {requisition.customerName} · Quote currency:{" "}
            <span className="font-mono">{quoteCurrency}</span>
          </p>
        </div>
        <span className="text-xs text-muted-foreground">
          {saveStatus === "saving" && "Saving…"}
          {saveStatus === "saved" && "Saved ✓"}
          {saveStatus === "error" && <span className="text-destructive">Failed to save draft.</span>}
        </span>
      </div>

      {processGroups.map((group) => {
        const sectionLines = lines.filter((l) => l.processId === group.processId);
        return (
          <div key={group.processId} className="rounded-lg border border-border overflow-hidden">
            <div className="bg-muted/50 px-4 py-3">
              <span className="font-semibold text-sm">⚙ {group.processName}</span>
            </div>
            <div className="grid grid-cols-[2fr_80px_80px_120px_90px_2fr] gap-2 px-4 py-2 text-xs text-muted-foreground border-b border-border">
              <span>Raw Material</span>
              <span>Qty / kg</span>
              <span>Waste %</span>
              <span>Cost / kg</span>
              <span>Currency</span>
              <span>Last Price</span>
            </div>
            {sectionLines.map((line) => {
              const ageDays = line.lastCost ? daysSince(line.lastCost.updatedAt) : null;
              const stale = ageDays !== null && ageDays > STALE_DAYS;
              return (
                <div
                  key={line.bomLineId}
                  className="grid grid-cols-[2fr_80px_80px_120px_90px_2fr] gap-2 px-4 py-2 text-sm border-b border-border items-center"
                >
                  <span>{line.rawMaterialDescription}</span>
                  <span className="font-mono text-muted-foreground">{line.qtyPerKg.toFixed(4)}</span>
                  <span className="font-mono text-muted-foreground">{line.wastagePct.toFixed(2)}%</span>
                  <input
                    type="number"
                    step="0.0001"
                    min="0"
                    disabled={isReadOnly}
                    value={line.costPerKg || ""}
                    onChange={(e) =>
                      updateLine(line.bomLineId, { costPerKg: parseFloat(e.target.value) || 0 })
                    }
                    className="h-9 rounded-md border border-input bg-background px-2 text-sm font-mono"
                    aria-label={`Cost per kg for ${line.rawMaterialDescription}`}
                  />
                  <select
                    disabled={isReadOnly}
                    value={line.currencyCode}
                    onChange={(e) => updateLine(line.bomLineId, { currencyCode: e.target.value })}
                    className="h-9 rounded-md border border-input bg-background px-2 text-sm"
                    aria-label={`Currency for ${line.rawMaterialDescription}`}
                  >
                    {currencyOptions.map((c) => (
                      <option key={c} value={c}>
                        {c}
                      </option>
                    ))}
                  </select>
                  {line.lastCost ? (
                    <span className={`text-xs ${stale ? "text-yellow-400" : "text-muted-foreground"}`}>
                      {stale && "⚠ "}
                      {line.lastCost.currencyCode} {line.lastCost.costPerKg.toFixed(4)} · {ageDays} days ago
                      {stale && " — verify from ERP"}
                    </span>
                  ) : (
                    <span className="text-xs text-muted-foreground/60">No previous price</span>
                  )}
                </div>
              );
            })}
          </div>
        );
      })}

      {/* Landed cost & FOH */}
      <div className="rounded-lg border border-border px-4 py-3 space-y-3">
        <div className="text-xs font-semibold text-muted-foreground">Landed Cost &amp; Overheads</div>
        <div className="flex flex-wrap items-center gap-4 text-sm">
          <label className="flex items-center gap-2">
            <span className="text-muted-foreground">Type</span>
            <select
              disabled={isReadOnly}
              value={landedCostType}
              onChange={(e) => updateLandedType(e.target.value as LandedCostType)}
              className="h-9 rounded-md border border-input bg-background px-2 text-sm"
            >
              <option value="Percentage">Percentage</option>
              <option value="FixedValue">Fixed Value</option>
            </select>
          </label>
          <label className="flex items-center gap-2">
            <span className="text-muted-foreground">Value</span>
            <input
              type="number"
              step="0.0001"
              min="0"
              disabled={isReadOnly}
              value={landedCostValue || ""}
              onChange={(e) => updateLandedValue(parseFloat(e.target.value) || 0)}
              className="h-9 w-28 rounded-md border border-input bg-background px-2 text-sm font-mono"
            />
            <span className="text-xs text-muted-foreground">
              {landedCostType === "Percentage" ? "% of raw material total" : `${quoteCurrency} per kg`}
            </span>
          </label>
          <label className="flex items-center gap-2">
            <span className="text-muted-foreground">FOH (per kg)</span>
            <input
              type="number"
              step="0.0001"
              min="0"
              disabled={isReadOnly}
              value={fohAmount || ""}
              onChange={(e) => updateFoh(parseFloat(e.target.value) || 0)}
              className="h-9 w-28 rounded-md border border-input bg-background px-2 text-sm font-mono"
            />
            <span className="text-xs text-muted-foreground">{quoteCurrency}</span>
          </label>
        </div>
      </div>

      {/* Summary + Submit */}
      <div className="rounded-lg border border-border px-4 py-3 flex flex-wrap items-center justify-between gap-4">
        <div className="flex gap-6 text-sm">
          <div className="text-center">
            <div className="text-xs text-muted-foreground">Raw Material Total</div>
            <div className="font-mono font-semibold">
              {quoteCurrency} {totals.rawTotal.toFixed(4)}
            </div>
          </div>
          <div className="text-center">
            <div className="text-xs text-muted-foreground">Landed Cost</div>
            <div className="font-mono font-semibold">
              {quoteCurrency} {totals.landed.toFixed(4)}
            </div>
          </div>
          <div className="text-center">
            <div className="text-xs text-muted-foreground">FOH</div>
            <div className="font-mono font-semibold">
              {quoteCurrency} {totals.foh.toFixed(4)}
            </div>
          </div>
          <div className="text-center">
            <div className="text-xs text-muted-foreground">Total Cost / kg</div>
            <div className="font-mono font-semibold text-green-500">
              {quoteCurrency} {totals.total.toFixed(4)}
            </div>
          </div>
        </div>
        {!isReadOnly && (
          <div className="flex flex-col items-end gap-1">
            <Button
              onClick={handleSubmit}
              disabled={!canSubmit || submitCosting.isPending}
              title={!canSubmit ? "Enter cost for all lines before submitting" : undefined}
            >
              {submitCosting.isPending ? "Submitting…" : "Submit Costing ↗"}
            </Button>
            {submitError && <span className="text-xs text-destructive">{submitError}</span>}
          </div>
        )}
      </div>
    </div>
  );
}
