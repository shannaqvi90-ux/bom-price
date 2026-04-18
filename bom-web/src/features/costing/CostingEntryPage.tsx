import { useEffect, useMemo, useRef, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { ArrowLeft } from "lucide-react";
import { notify } from "@/lib/notify";
import { extractFieldErrors } from "@/lib/apiError";
import { Button } from "@/components/ui/Button";
import { useActiveExchangeRates } from "@/api/lookups";
import { useRequisition } from "@/features/requisitions/requisitionsApi";
import {
  useCosting,
  useStartCostingItem,
  useSaveCostingItemDraft,
  useSubmitCostingItem,
} from "./costingApi";
import type { CostingBomLine, CostingItemResponse, LandedCostType } from "@/types/api";

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

function convertCost(amount: number, from: string, to: string, rates: { currencyCode: string; rateToAed: number }[]): number | null {
  const fromRate = rateToAed(from, rates);
  const toRate = rateToAed(to, rates);
  if (fromRate === null || toRate === null) return null;
  return (amount * fromRate) / toRate;
}

function costStatusLabel(status: string) {
  if (status === "Submitted") return "Submitted";
  return "Not Started";
}

function costStatusColor(status: string) {
  if (status === "Submitted") return "text-green-500";
  return "text-muted-foreground";
}

export default function CostingEntryPage() {
  const { id } = useParams<{ id: string }>();
  const requisitionId = Number(id);
  const navigate = useNavigate();

  const { data: requisition } = useRequisition(requisitionId);
  const { data: costingReview, isLoading: costingLoading, refetch: refetchCosting } = useCosting(requisitionId);
  const { data: exchangeRates = [] } = useActiveExchangeRates();

  const startCostingItem = useStartCostingItem();
  const saveDraft = useSaveCostingItemDraft();
  const submitCostingItem = useSubmitCostingItem();

  const [selectedItemId, setSelectedItemId] = useState<number | null>(null);
  const [lines, setLines] = useState<LocalCostLine[]>([]);
  const [landedCostType, setLandedCostType] = useState<LandedCostType>("Percentage");
  const [landedCostValue, setLandedCostValue] = useState<number>(0);
  const [fohAmount, setFohAmount] = useState<number>(0);
  const [saveStatus, setSaveStatus] = useState<"idle" | "saving" | "saved" | "error">("idle");
  const [hydrated, setHydrated] = useState(false);
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const debounceRef = useRef<number | undefined>(undefined);
  const hasAutoStartedRef = useRef(false);

  // Warn before unload when a save is in flight
  useEffect(() => {
    if (saveStatus !== "saving") return;
    const handler = (e: BeforeUnloadEvent) => { e.preventDefault(); e.returnValue = ""; };
    window.addEventListener("beforeunload", handler);
    return () => window.removeEventListener("beforeunload", handler);
  }, [saveStatus]);

  // Auto-select first item
  useEffect(() => {
    if (costingReview && costingReview.items.length > 0 && selectedItemId === null) {
      setSelectedItemId(costingReview.items[0].requisitionItemId);
    }
  }, [costingReview, selectedItemId]);

  // Auto-start first item when CostingPending
  useEffect(() => {
    if (
      costingReview &&
      requisition?.status === "CostingPending" &&
      !hasAutoStartedRef.current
    ) {
      const first = costingReview.items[0];
      if (first && first.costStatus === "NotStarted") {
        hasAutoStartedRef.current = true;
        startCostingItem.mutate(
          { requisitionId, requisitionItemId: first.requisitionItemId },
          { onSuccess: () => refetchCosting() },
        );
      }
    }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [costingReview, requisition?.status]);

  const selectedItem: CostingItemResponse | undefined = costingReview?.items.find(
    (i) => i.requisitionItemId === selectedItemId,
  );

  // Hydrate from selected item
  useEffect(() => {
    if (!selectedItem) return;
    setHydrated(false);
  }, [selectedItemId]);

  useEffect(() => {
    if (!selectedItem || hydrated) return;
    const quoteCurrency = requisition?.currencyCode ?? "AED";

    const draftByLineId = new Map(
      (selectedItem.draft?.lines ?? []).map((l) => [l.bomLineId, l]),
    );

    const local: LocalCostLine[] = selectedItem.bomLines.map((bl: CostingBomLine) => {
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
        currencyCode: draftLine?.currencyCode ?? bl.lastCost?.currencyCode ?? quoteCurrency,
        lastCost: bl.lastCost,
      };
    });
    setLines(local);

    if (selectedItem.draft) {
      setLandedCostType(selectedItem.draft.landedCostType);
      setLandedCostValue(selectedItem.draft.landedCostValue);
      setFohAmount(selectedItem.draft.fohAmount);
    } else {
      setLandedCostType("Percentage");
      setLandedCostValue(0);
      setFohAmount(0);
    }
    setHydrated(true);
  }, [selectedItem, hydrated, requisition]);

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

  const canEditItem = selectedItem && selectedItem.costStatus !== "Submitted" && !isReadOnly;

  // ── Auto-save ──
  function triggerAutoSave(nextLines: LocalCostLine[], nextType: LandedCostType, nextValue: number, nextFoh: number) {
    if (!canEditItem || !selectedItemId) return;
    if (debounceRef.current) window.clearTimeout(debounceRef.current);
    debounceRef.current = window.setTimeout(() => {
      setSaveStatus("saving");
      saveDraft.mutate(
        {
          requisitionId,
          requisitionItemId: selectedItemId,
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

  // ── Totals ──
  const totals = useMemo(() => {
    let rawTotal = 0;
    for (const l of lines) {
      const inQuote = convertCost(l.costPerKg, l.currencyCode, quoteCurrency, exchangeRates);
      if (inQuote === null) continue;
      rawTotal += inQuote * l.qtyPerKg * (1 + l.wastagePct / 100);
    }
    const landed = landedCostType === "Percentage" ? (rawTotal * landedCostValue) / 100 : landedCostValue;
    const total = rawTotal + landed + fohAmount;
    return { rawTotal, landed, foh: fohAmount, total };
  }, [lines, landedCostType, landedCostValue, fohAmount, quoteCurrency, exchangeRates]);

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

  function handleStartItem(requisitionItemId: number) {
    startCostingItem.mutate(
      { requisitionId, requisitionItemId },
      { onSuccess: () => refetchCosting() },
    );
  }

  function handleSubmitItem() {
    if (!selectedItemId) return;
    setFieldErrors({});
    submitCostingItem.mutate(
      {
        requisitionId,
        requisitionItemId: selectedItemId,
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
        onSuccess: () => {
          refetchCosting();
          notify.success("Costing submitted");
          // If there are more items to cost, stay on page; otherwise navigate back
          const remaining = costingReview?.items.filter(
            (i) => i.requisitionItemId !== selectedItemId && i.costStatus !== "Submitted",
          );
          if (!remaining || remaining.length === 0) {
            navigate(`/requisitions/${requisitionId}`);
          }
        },
        onError: (err: unknown) => {
          setFieldErrors(extractFieldErrors(err));
          notify.fromApiError(err, "Failed to submit costing.");
        },
      },
    );
  }

  // ── Render ──
  if (costingLoading || !requisition || !costingReview) {
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
        <h1 className="text-2xl font-semibold tracking-tight">
          {isReadOnly ? "Costing (read-only)" : "Costing Entry"}
        </h1>
        <span className="text-xs text-muted-foreground">
          {saveStatus === "saving" && "Saving…"}
          {saveStatus === "saved" && "Saved ✓"}
          {saveStatus === "error" && <span className="text-destructive">Failed to save draft.</span>}
        </span>
      </div>

      <div className="grid grid-cols-1 gap-4 lg:grid-cols-[220px_1fr]">
        {/* Item selector sidebar */}
        <div className="space-y-1 rounded-lg border border-border p-3">
          <p className="mb-2 text-xs font-semibold text-muted-foreground uppercase">Items</p>
          {costingReview.items.map((item) => (
            <button
              key={item.requisitionItemId}
              type="button"
              onClick={() => setSelectedItemId(item.requisitionItemId)}
              className={`w-full rounded-md px-3 py-2 text-left text-sm ${
                selectedItemId === item.requisitionItemId
                  ? "bg-primary/10 font-medium"
                  : "hover:bg-muted/50"
              }`}
            >
              <div className="truncate">{item.itemDescription}</div>
              <div className={`text-xs ${costStatusColor(item.costStatus)}`}>
                {costStatusLabel(item.costStatus)}
              </div>
            </button>
          ))}
        </div>

        {/* Cost entry for selected item */}
        <div className="space-y-4">
          {selectedItem ? (
            <>
              <div className="flex items-center justify-between">
                <p className="text-sm text-muted-foreground">
                  {selectedItem.itemDescription} — {selectedItem.expectedQty.toLocaleString()} kg · Quote: <span className="font-mono">{quoteCurrency}</span>
                </p>
                {selectedItem.costStatus === "Submitted" && (
                  <span className="text-xs text-green-500">Submitted</span>
                )}
              </div>

              {selectedItem.bomLines.length === 0 ? (
                <p className="text-sm text-muted-foreground">No BOM lines. BOM must be submitted first.</p>
              ) : selectedItem.costStatus === "NotStarted" && !isReadOnly && requisition.status === "CostingPending" ? (
                <div className="space-y-2">
                  <p className="text-sm text-muted-foreground">Click "Start Costing" to begin entering costs for this item.</p>
                  <Button
                    size="sm"
                    disabled={startCostingItem.isPending}
                    onClick={() => handleStartItem(selectedItem.requisitionItemId)}
                  >
                    {startCostingItem.isPending ? "Starting…" : "Start Costing"}
                  </Button>
                </div>
              ) : (
                <>
                  {processGroups.map((group) => {
                    const sectionLines = lines.filter((l) => l.processId === group.processId);
                    return (
                      <div key={group.processId} className="rounded-lg border border-border overflow-hidden">
                        <div className="bg-muted/50 px-4 py-3">
                          <span className="font-semibold text-sm">{group.processName}</span>
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
                          const overallIdx = lines.findIndex((l) => l.bomLineId === line.bomLineId);
                          const costErr = fieldErrors[`rawMaterialCosts.${overallIdx}.costPerKg`];
                          const bomLineErr = fieldErrors[`rawMaterialCosts.${overallIdx}.bomLineId`];
                          const ageDays = line.lastCost ? daysSince(line.lastCost.updatedAt) : null;
                          const stale = ageDays !== null && ageDays > STALE_DAYS;
                          return (
                            <div
                              key={line.bomLineId}
                              className="grid grid-cols-[2fr_80px_80px_120px_90px_2fr] gap-2 px-4 py-2 text-sm border-b border-border items-start"
                            >
                              <span>
                                {line.rawMaterialDescription}
                                {bomLineErr && <p className="text-xs text-destructive">{bomLineErr}</p>}
                              </span>
                              <span className="font-mono text-muted-foreground">{line.qtyPerKg.toFixed(4)}</span>
                              <span className="font-mono text-muted-foreground">{line.wastagePct.toFixed(2)}%</span>
                              <div>
                                <input
                                  type="number"
                                  step="0.0001"
                                  min="0"
                                  disabled={!canEditItem}
                                  value={line.costPerKg || ""}
                                  onChange={(e) => {
                                    setFieldErrors({});
                                    updateLine(line.bomLineId, { costPerKg: parseFloat(e.target.value) || 0 });
                                  }}
                                  className={`h-9 rounded-md border bg-background px-2 text-sm font-mono ${costErr ? "border-destructive" : "border-input"}`}
                                  aria-label={`Cost per kg for ${line.rawMaterialDescription}`}
                                />
                                {costErr && <p className="text-xs text-destructive">{costErr}</p>}
                              </div>
                              <select
                                disabled={!canEditItem}
                                value={line.currencyCode}
                                onChange={(e) => updateLine(line.bomLineId, { currencyCode: e.target.value })}
                                className="h-9 rounded-md border border-input bg-background px-2 text-sm"
                                aria-label={`Currency for ${line.rawMaterialDescription}`}
                              >
                                {currencyOptions.map((c) => (
                                  <option key={c} value={c}>{c}</option>
                                ))}
                              </select>
                              {line.lastCost ? (
                                <span className={`text-xs ${stale ? "text-yellow-400" : "text-muted-foreground"}`}>
                                  {stale && "! "}
                                  {line.lastCost.currencyCode} {line.lastCost.costPerKg.toFixed(4)} · {ageDays} days ago
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
                    <div className="text-xs font-semibold text-muted-foreground">Landed Cost & Overheads</div>
                    <div className="flex flex-wrap items-center gap-4 text-sm">
                      <label className="flex items-center gap-2">
                        <span className="text-muted-foreground">Type</span>
                        <select
                          disabled={!canEditItem}
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
                          disabled={!canEditItem}
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
                          disabled={!canEditItem}
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
                        <div className="font-mono font-semibold">{quoteCurrency} {totals.rawTotal.toFixed(4)}</div>
                      </div>
                      <div className="text-center">
                        <div className="text-xs text-muted-foreground">Landed Cost</div>
                        <div className="font-mono font-semibold">{quoteCurrency} {totals.landed.toFixed(4)}</div>
                      </div>
                      <div className="text-center">
                        <div className="text-xs text-muted-foreground">FOH</div>
                        <div className="font-mono font-semibold">{quoteCurrency} {totals.foh.toFixed(4)}</div>
                      </div>
                      <div className="text-center">
                        <div className="text-xs text-muted-foreground">Total Cost / kg</div>
                        <div className="font-mono font-semibold text-green-500">{quoteCurrency} {totals.total.toFixed(4)}</div>
                      </div>
                    </div>
                    {canEditItem && (
                      <div className="flex flex-col items-end gap-1">
                        <Button
                          onClick={handleSubmitItem}
                          disabled={!canSubmit || submitCostingItem.isPending}
                          title={!canSubmit ? "Enter cost for all lines before submitting" : undefined}
                        >
                          {submitCostingItem.isPending ? "Submitting…" : "Submit Costing"}
                        </Button>
                      </div>
                    )}
                  </div>
                </>
              )}
            </>
          ) : (
            <p className="text-sm text-muted-foreground">Select an item from the sidebar.</p>
          )}
        </div>
      </div>
    </div>
  );
}
