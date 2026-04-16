import { useEffect, useMemo, useRef, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { ArrowLeft, Check, X } from "lucide-react";
import { Button } from "@/components/ui/Button";
import { Card, CardContent } from "@/components/ui/Card";
import { SearchableSelect } from "@/components/ui/SearchableSelect";
import { useItems, useProcesses } from "@/api/lookups";
import { useBom, useStartBomItem, useSaveBomItemLines, useSubmitBom } from "./bomApi";
import { useRequisition } from "@/features/requisitions/requisitionsApi";
import type { BomItemResponse, Item, Process } from "@/types/api";

// ─── Local types ──────────────────────────────────────────────────────────────

interface LocalLine {
  localId: string;
  processId: number;
  processName: string;
  rawMaterialItemId: number;
  rawMaterialDescription: string;
  qtyPerKg: number;
  wastagePct: number;
}

interface ProcessSection {
  processId: number;
  processName: string;
}

interface PendingLine {
  rawMaterial: Item | null;
  qtyPerKg: string;
  wastagePct: string;
}

const emptyPending: PendingLine = { rawMaterial: null, qtyPerKg: "", wastagePct: "" };

// ─── Helpers ──────────────────────────────────────────────────────────────────

function calcTotals(processLines: LocalLine[]) {
  const totalQty = processLines.reduce((s, l) => s + l.qtyPerKg, 0);
  const totalWaste = processLines.reduce((s, l) => s + l.qtyPerKg * (l.wastagePct / 100), 0);
  return { totalQty, totalWaste, netQty: totalQty - totalWaste };
}

function bomStatusLabel(status: string) {
  if (status === "Submitted") return "Submitted";
  if (status === "InProgress") return "In Progress";
  return "Not Started";
}

function bomStatusColor(status: string) {
  if (status === "Submitted") return "text-green-500";
  if (status === "InProgress") return "text-yellow-400";
  return "text-muted-foreground";
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function BomEntryPage() {
  const { id } = useParams<{ id: string }>();
  const requisitionId = Number(id);
  const navigate = useNavigate();

  // Server data
  const { data: requisition } = useRequisition(requisitionId);
  const { data: bom, isLoading: bomLoading, refetch: refetchBom } = useBom(requisitionId);
  const { data: allProcesses = [] } = useProcesses();
  const { data: allItems = [] } = useItems();
  const rawMaterials = useMemo(() => allItems.filter((i) => i.type === "RawMaterial"), [allItems]);

  // Mutations
  const startBomItem = useStartBomItem();
  const saveBomItemLines = useSaveBomItemLines();
  const submitBom = useSubmitBom();

  // Local state
  const [selectedItemId, setSelectedItemId] = useState<number | null>(null);
  const [processSections, setProcessSections] = useState<ProcessSection[]>([]);
  const [lines, setLines] = useState<LocalLine[]>([]);
  const [addingToProcess, setAddingToProcess] = useState<number | null>(null);
  const [pendingLine, setPendingLine] = useState<PendingLine>(emptyPending);
  const [addingProcess, setAddingProcess] = useState(false);
  const hasAutoStartedRef = useRef(false);
  const [startError, setStartError] = useState(false);
  const [saveStatus, setSaveStatus] = useState<"idle" | "saving" | "saved" | "error">("idle");
  const [duplicateWarning, setDuplicateWarning] = useState<{ processName: string } | null>(null);
  const [pendingDuplicate, setPendingDuplicate] = useState<LocalLine | null>(null);

  // Auto-select first item when bom loads
  useEffect(() => {
    if (bom && bom.items.length > 0 && selectedItemId === null) {
      setSelectedItemId(bom.items[0].requisitionItemId);
    }
  }, [bom, selectedItemId]);

  // Auto-start first item when BomPending
  useEffect(() => {
    if (
      bom &&
      requisition?.status === "BomPending" &&
      !hasAutoStartedRef.current
    ) {
      const firstNotStarted = bom.items.find((i) => i.bomStatus === "NotStarted");
      if (firstNotStarted) {
        hasAutoStartedRef.current = true;
        startBomItem.mutate(
          { requisitionId, requisitionItemId: firstNotStarted.requisitionItemId },
          {
            onSuccess: () => refetchBom(),
            onError: () => setStartError(true),
          },
        );
      }
    }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [bom, requisition?.status]);

  // Selected item
  const selectedItem: BomItemResponse | undefined = bom?.items.find(
    (i) => i.requisitionItemId === selectedItemId,
  );

  // Hydrate lines + processSections from selected item
  useEffect(() => {
    if (!selectedItem) return;
    const seen = new Set<number>();
    const sections: ProcessSection[] = [];
    for (const l of selectedItem.lines) {
      if (!seen.has(l.processId)) {
        seen.add(l.processId);
        sections.push({ processId: l.processId, processName: l.processName });
      }
    }
    setProcessSections(sections);
    setLines(
      selectedItem.lines.map((l) => ({
        localId: crypto.randomUUID(),
        processId: l.processId,
        processName: l.processName,
        rawMaterialItemId: l.rawMaterialItemId,
        rawMaterialDescription: l.rawMaterialDescription,
        qtyPerKg: l.qtyPerKg,
        wastagePct: l.wastagePct,
      })),
    );
  }, [selectedItem]);

  // ── Derived ────────────────────────────────────────────────────────────────

  const overallTotals = useMemo(() => calcTotals(lines), [lines]);
  const netQtyWarning = Math.abs(overallTotals.netQty - 1.0) > 0.01 && lines.length > 0;

  const availableProcesses = useMemo(
    () => allProcesses.filter((p) => p.isActive && !processSections.some((s) => s.processId === p.id)),
    [allProcesses, processSections],
  );

  const isReadOnly =
    requisition !== undefined &&
    requisition.status !== "BomPending" &&
    requisition.status !== "BomInProgress";

  const canEdit = selectedItem && selectedItem.bomStatus === "InProgress" && !isReadOnly;
  const allItemsReady = bom ? bom.items.every((i) => i.bomStatus !== "NotStarted" && i.lines.length > 0) : false;

  // ── Auto-save helper ───────────────────────────────────────────────────────

  function doSave(newLines: LocalLine[], prevLines: LocalLine[]) {
    if (!selectedItemId) return;
    setSaveStatus("saving");
    saveBomItemLines.mutate(
      {
        requisitionId,
        requisitionItemId: selectedItemId,
        lines: newLines.map((l) => ({ processId: l.processId, rawMaterialItemId: l.rawMaterialItemId, qtyPerKg: l.qtyPerKg, wastagePct: l.wastagePct })),
      },
      {
        onSuccess: () => setSaveStatus("saved"),
        onError: () => {
          setLines(prevLines);
          setSaveStatus("error");
        },
      },
    );
  }

  // ── Process management ─────────────────────────────────────────────────────

  function addProcess(process: Process) {
    setProcessSections((prev) => [...prev, { processId: process.id, processName: process.name }]);
    setAddingProcess(false);
    setAddingToProcess(process.id);
    setPendingLine(emptyPending);
  }

  function removeProcess(processId: number) {
    const prevLines = lines;
    const newLines = lines.filter((l) => l.processId !== processId);
    setProcessSections((prev) => prev.filter((s) => s.processId !== processId));
    setLines(newLines);
    if (addingToProcess === processId) setAddingToProcess(null);
    doSave(newLines, prevLines);
  }

  // ── Line management ────────────────────────────────────────────────────────

  function confirmAddLine(processId: number, processName: string) {
    if (!pendingLine.rawMaterial) return;
    const qty = parseFloat(pendingLine.qtyPerKg);
    const waste = parseFloat(pendingLine.wastagePct || "0");
    if (!qty || qty <= 0) return;

    const newLine: LocalLine = {
      localId: crypto.randomUUID(),
      processId,
      processName,
      rawMaterialItemId: pendingLine.rawMaterial.id,
      rawMaterialDescription: pendingLine.rawMaterial.description,
      qtyPerKg: qty,
      wastagePct: waste,
    };

    const existing = lines.find((l) => l.rawMaterialItemId === newLine.rawMaterialItemId);
    if (existing && !pendingDuplicate) {
      setDuplicateWarning({ processName: existing.processName });
      setPendingDuplicate(newLine);
      return;
    }

    commitAddLine(newLine);
  }

  function commitAddLine(newLine: LocalLine) {
    const prevLines = lines;
    const newLines = [...lines, newLine];
    setLines(newLines);
    setAddingToProcess(null);
    setPendingLine(emptyPending);
    setDuplicateWarning(null);
    setPendingDuplicate(null);
    doSave(newLines, prevLines);
  }

  function removeLine(localId: string) {
    const prevLines = lines;
    const newLines = lines.filter((l) => l.localId !== localId);
    setLines(newLines);
    doSave(newLines, prevLines);
  }

  // ── Start item ────────────────────────────────────────────────────────────

  function handleStartItem(requisitionItemId: number) {
    startBomItem.mutate(
      { requisitionId, requisitionItemId },
      { onSuccess: () => refetchBom() },
    );
  }

  // ── Submit ─────────────────────────────────────────────────────────────────

  function handleSubmit() {
    submitBom.mutate(requisitionId, {
      onSuccess: () => navigate(`/requisitions/${requisitionId}`),
    });
  }

  // ── Render ─────────────────────────────────────────────────────────────────

  if (startError) {
    return (
      <Card className="mx-auto max-w-lg">
        <CardContent className="py-8 text-center text-destructive">
          Failed to start BOM. Please go back and try again.
        </CardContent>
      </Card>
    );
  }

  if (startBomItem.isPending || bomLoading) {
    return <p className="text-sm text-muted-foreground">Loading BOM…</p>;
  }

  if (!requisition || !bom) {
    return <p className="text-sm text-muted-foreground">Loading…</p>;
  }

  return (
    <div className="space-y-4">
      <Link
        to={`/requisitions/${requisitionId}`}
        className="inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground"
      >
        <ArrowLeft className="h-4 w-4" /> Back to {bom.refNo}
      </Link>

      <div className="flex flex-wrap items-start justify-between gap-4">
        <h1 className="text-2xl font-semibold tracking-tight">
          {isReadOnly ? "BOM (read-only)" : "BOM Entry"}
        </h1>
        <span className="text-xs text-muted-foreground">
          {saveStatus === "saving" && "Saving…"}
          {saveStatus === "saved" && "Saved ✓"}
          {saveStatus === "error" && <span className="text-destructive">Save failed</span>}
        </span>
      </div>

      <div className="grid grid-cols-1 gap-4 lg:grid-cols-[220px_1fr]">
        {/* Item selector sidebar */}
        <div className="space-y-1 rounded-lg border border-border p-3">
          <p className="mb-2 text-xs font-semibold text-muted-foreground uppercase">Items</p>
          {bom.items.map((item) => (
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
              <div className={`text-xs ${bomStatusColor(item.bomStatus)}`}>
                {bomStatusLabel(item.bomStatus)}
              </div>
            </button>
          ))}
        </div>

        {/* Lines editor */}
        <div className="space-y-4">
          {selectedItem ? (
            <>
              <div className="flex items-center justify-between">
                <p className="text-sm text-muted-foreground">
                  {selectedItem.itemDescription} — {selectedItem.expectedQty.toLocaleString()} kg
                </p>
                {selectedItem.bomStatus === "NotStarted" && !isReadOnly && (
                  <Button
                    size="sm"
                    disabled={startBomItem.isPending}
                    onClick={() => handleStartItem(selectedItem.requisitionItemId)}
                  >
                    {startBomItem.isPending ? "Starting…" : "Start BOM"}
                  </Button>
                )}
              </div>

              {selectedItem.bomStatus === "NotStarted" ? (
                <p className="text-sm text-muted-foreground">Click "Start BOM" to begin editing lines for this item.</p>
              ) : (
                <>
                  {/* Net Qty warning banner */}
                  {netQtyWarning && (
                    <div className="rounded-md border border-yellow-600 bg-yellow-950 px-4 py-3 text-sm text-yellow-300">
                      <span className="font-semibold">Net Qty/kg is {overallTotals.netQty.toFixed(4)} — expected ~1.0000.</span>{" "}
                      You can still submit — double-check quantities.
                    </div>
                  )}

                  {/* Process sections */}
                  {processSections.map((section) => {
                    const sectionLines = lines.filter((l) => l.processId === section.processId);
                    const { totalQty, totalWaste, netQty } = calcTotals(sectionLines);
                    const isAdding = addingToProcess === section.processId;

                    return (
                      <div key={section.processId} className="rounded-lg border border-border overflow-hidden">
                        <div className="bg-muted/50 px-4 py-3 space-y-2">
                          <div className="flex items-center justify-between">
                            <span className="font-semibold text-sm">{section.processName}</span>
                            {canEdit && (
                              <button
                                type="button"
                                onClick={() => removeProcess(section.processId)}
                                className="text-xs text-destructive hover:underline"
                              >
                                Remove
                              </button>
                            )}
                          </div>
                          <div className="grid grid-cols-3 rounded-md border border-border overflow-hidden text-xs">
                            <div className="px-3 py-2 text-center border-r border-border">
                              <div className="text-muted-foreground">Total Qty</div>
                              <div className="font-mono font-semibold">{totalQty.toFixed(4)} kg</div>
                            </div>
                            <div className="px-3 py-2 text-center border-r border-border">
                              <div className="text-muted-foreground">Total Waste</div>
                              <div className="font-mono font-semibold text-destructive">-{totalWaste.toFixed(4)} kg</div>
                            </div>
                            <div className="px-3 py-2 text-center">
                              <div className="text-muted-foreground">Net Qty</div>
                              <div className="font-mono font-semibold text-green-500">{netQty.toFixed(4)} kg</div>
                            </div>
                          </div>
                        </div>

                        {(sectionLines.length > 0 || isAdding) && (
                          <div className="grid grid-cols-[1fr_100px_90px_32px] gap-2 px-4 py-2 text-xs text-muted-foreground border-b border-border">
                            <span>Raw Material</span>
                            <span>Qty / kg</span>
                            <span>Wastage %</span>
                            <span />
                          </div>
                        )}

                        {sectionLines.map((line) => (
                          <div
                            key={line.localId}
                            className="grid grid-cols-[1fr_100px_90px_32px] gap-2 px-4 py-2 text-sm border-b border-border items-center"
                          >
                            <span>{line.rawMaterialDescription}</span>
                            <span className="font-mono">{line.qtyPerKg.toFixed(4)}</span>
                            <span className="font-mono">{line.wastagePct.toFixed(2)}%</span>
                            {canEdit && (
                              <button
                                type="button"
                                onClick={() => removeLine(line.localId)}
                                className="text-destructive hover:opacity-70"
                                aria-label="Remove line"
                              >
                                <X className="h-4 w-4" />
                              </button>
                            )}
                          </div>
                        ))}

                        {isAdding && (
                          <div className="grid grid-cols-[1fr_100px_90px_64px] gap-2 px-4 py-2 border-b border-border items-center bg-muted/20">
                            <SearchableSelect<Item>
                              options={rawMaterials}
                              value={pendingLine.rawMaterial}
                              onChange={(v) => setPendingLine((p) => ({ ...p, rawMaterial: v }))}
                              getLabel={(i) => i.description}
                              getValue={(i) => i.id}
                              placeholder="Search material…"
                            />
                            <input
                              type="number"
                              step="0.0001"
                              min="0"
                              placeholder="0.0000"
                              value={pendingLine.qtyPerKg}
                              onChange={(e) => setPendingLine((p) => ({ ...p, qtyPerKg: e.target.value }))}
                              className="h-10 rounded-md border border-input bg-background px-3 text-sm font-mono"
                              aria-label="Qty per kg"
                            />
                            <input
                              type="number"
                              step="0.01"
                              min="0"
                              placeholder="0.00"
                              value={pendingLine.wastagePct}
                              onChange={(e) => setPendingLine((p) => ({ ...p, wastagePct: e.target.value }))}
                              className="h-10 rounded-md border border-input bg-background px-3 text-sm font-mono"
                              aria-label="Wastage %"
                            />
                            <div className="flex items-center gap-1">
                              <button
                                type="button"
                                onClick={() => confirmAddLine(section.processId, section.processName)}
                                className="text-green-500 hover:opacity-70"
                                aria-label="Confirm add line"
                              >
                                <Check className="h-4 w-4" />
                              </button>
                              <button
                                type="button"
                                onClick={() => { setAddingToProcess(null); setPendingLine(emptyPending); }}
                                className="text-muted-foreground hover:opacity-70"
                                aria-label="Cancel add line"
                              >
                                <X className="h-4 w-4" />
                              </button>
                            </div>
                          </div>
                        )}

                        {duplicateWarning && addingToProcess === section.processId && (
                          <div className="mx-4 my-2 rounded-md border border-yellow-600 bg-yellow-950 px-3 py-2 text-xs text-yellow-300 flex items-center gap-3">
                            <span>Already added under {duplicateWarning.processName}. Add anyway?</span>
                            <button type="button" className="underline" onClick={() => pendingDuplicate && commitAddLine(pendingDuplicate)}>Yes, add</button>
                            <button type="button" className="underline" onClick={() => { setDuplicateWarning(null); setPendingDuplicate(null); }}>Cancel</button>
                          </div>
                        )}

                        {canEdit && !isAdding && (
                          <div className="px-4 py-2">
                            <button
                              type="button"
                              onClick={() => {
                                setAddingToProcess(section.processId);
                                setPendingLine(emptyPending);
                                setDuplicateWarning(null);
                                setPendingDuplicate(null);
                              }}
                              className="text-xs text-primary hover:underline"
                            >
                              + add raw material
                            </button>
                          </div>
                        )}
                      </div>
                    );
                  })}

                  {/* Add process */}
                  {canEdit && (
                    <div>
                      {addingProcess ? (
                        <div className="flex items-center gap-2">
                          <div className="w-72">
                            <SearchableSelect<Process>
                              options={availableProcesses}
                              value={null}
                              onChange={(p) => p && addProcess(p)}
                              getLabel={(p) => p.name}
                              getValue={(p) => p.id}
                              placeholder="Select process…"
                            />
                          </div>
                          <button
                            type="button"
                            onClick={() => setAddingProcess(false)}
                            className="text-sm text-muted-foreground hover:text-foreground"
                          >
                            Cancel
                          </button>
                        </div>
                      ) : (
                        <button
                          type="button"
                          onClick={() => setAddingProcess(true)}
                          className="w-full rounded-lg border border-dashed border-border py-3 text-sm text-primary hover:bg-muted/20"
                        >
                          + Add Process
                        </button>
                      )}
                    </div>
                  )}

                  {/* Summary bar */}
                  <div className="rounded-lg border border-border px-4 py-3 flex flex-wrap items-center justify-between gap-4">
                    <div className="flex gap-6 text-sm">
                      <div className="text-center">
                        <div className="text-xs text-muted-foreground">Total Qty</div>
                        <div className="font-mono font-semibold">{overallTotals.totalQty.toFixed(4)} kg</div>
                      </div>
                      <div className="text-center">
                        <div className="text-xs text-muted-foreground">Total Waste</div>
                        <div className="font-mono font-semibold text-destructive">-{overallTotals.totalWaste.toFixed(4)} kg</div>
                      </div>
                      <div className="text-center">
                        <div className="text-xs text-muted-foreground">Net Qty / kg</div>
                        <div className={`font-mono font-semibold ${netQtyWarning ? "text-yellow-400" : "text-green-500"}`}>
                          {overallTotals.netQty.toFixed(4)} kg {netQtyWarning && "!"}
                        </div>
                      </div>
                    </div>
                  </div>
                </>
              )}
            </>
          ) : (
            <p className="text-sm text-muted-foreground">Select an item from the sidebar.</p>
          )}

          {/* Submit All button */}
          {!isReadOnly && (
            <div className="flex justify-end">
              <Button
                onClick={handleSubmit}
                disabled={!allItemsReady || submitBom.isPending}
              >
                {submitBom.isPending ? "Submitting…" : "Submit All"}
              </Button>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
