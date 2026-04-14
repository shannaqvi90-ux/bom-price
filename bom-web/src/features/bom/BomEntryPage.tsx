import { useEffect, useMemo, useRef, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { ArrowLeft, Check, X } from "lucide-react";
import { Button } from "@/components/ui/Button";
import { Card, CardContent } from "@/components/ui/Card";
import { SearchableSelect } from "@/components/ui/SearchableSelect";
import { useItems, useProcesses } from "@/api/lookups";
import { useBom, useStartBom, useSaveBomLines, useSubmitBom } from "./bomApi";
import { useRequisition } from "@/features/requisitions/requisitionsApi";
import type { Item, Process } from "@/types/api";

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
  const startBom = useStartBom();
  const saveBomLines = useSaveBomLines();
  const submitBom = useSubmitBom();

  // Local state
  const [processSections, setProcessSections] = useState<ProcessSection[]>([]);
  const [lines, setLines] = useState<LocalLine[]>([]);
  const [addingToProcess, setAddingToProcess] = useState<number | null>(null);
  const [pendingLine, setPendingLine] = useState<PendingLine>(emptyPending);
  const [addingProcess, setAddingProcess] = useState(false);
  const hasStartedRef = useRef(false);
  const [startError, setStartError] = useState(false);
  const [saveStatus, setSaveStatus] = useState<"idle" | "saving" | "saved" | "error">("idle");
  const [duplicateWarning, setDuplicateWarning] = useState<{ processName: string } | null>(null);
  const [pendingDuplicate, setPendingDuplicate] = useState<LocalLine | null>(null);

  // Auto-start when BomPending and no BOM yet
  useEffect(() => {
    if (
      requisition?.status === "BomPending" &&
      !bom &&
      !bomLoading &&
      !hasStartedRef.current
    ) {
      hasStartedRef.current = true;
      startBom.mutate(requisitionId, {
        onSuccess: () => refetchBom(),
        onError: () => setStartError(true),
      });
    }
  }, [requisition?.status, bom, bomLoading]);

  // Hydrate lines + processSections from fetched BOM
  useEffect(() => {
    if (!bom) return;
    const seen = new Set<number>();
    const sections: ProcessSection[] = [];
    for (const l of bom.lines) {
      if (!seen.has(l.processId)) {
        seen.add(l.processId);
        sections.push({ processId: l.processId, processName: l.processName });
      }
    }
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setProcessSections(sections);
    setLines(
      bom.lines.map((l) => ({
        localId: crypto.randomUUID(),
        processId: l.processId,
        processName: l.processName,
        rawMaterialItemId: l.rawMaterialItemId,
        rawMaterialDescription: l.rawMaterialDescription,
        qtyPerKg: l.qtyPerKg,
        wastagePct: l.wastagePct,
      })),
    );
  }, [bom]);

  // ── Derived ────────────────────────────────────────────────────────────────

  const overallTotals = useMemo(() => calcTotals(lines), [lines]);
  const netQtyWarning = Math.abs(overallTotals.netQty - 1.0) > 0.01 && lines.length > 0;

  // Processes not yet in processSections
  const availableProcesses = useMemo(
    () => allProcesses.filter((p) => p.isActive && !processSections.some((s) => s.processId === p.id)),
    [allProcesses, processSections],
  );

  // ── Auto-save helper ───────────────────────────────────────────────────────

  function doSave(newLines: LocalLine[], prevLines: LocalLine[]) {
    setSaveStatus("saving");
    saveBomLines.mutate(
      { requisitionId, lines: newLines.map((l) => ({ processId: l.processId, rawMaterialItemId: l.rawMaterialItemId, qtyPerKg: l.qtyPerKg, wastagePct: l.wastagePct })) },
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

    // Duplicate check
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

  // ── Submit ─────────────────────────────────────────────────────────────────

  function handleSubmit() {
    submitBom.mutate(
      { requisitionId, lines: lines.map((l) => ({ processId: l.processId, rawMaterialItemId: l.rawMaterialItemId, qtyPerKg: l.qtyPerKg, wastagePct: l.wastagePct })) },
      { onSuccess: () => navigate(`/requisitions/${requisitionId}`) },
    );
  }

  // ── Read-only mode (status is past BomInProgress) ─────────────────────────

  const isReadOnly =
    requisition !== undefined &&
    requisition.status !== "BomPending" &&
    requisition.status !== "BomInProgress";

  // ── Render helpers ─────────────────────────────────────────────────────────

  if (startError) {
    return (
      <Card className="mx-auto max-w-lg">
        <CardContent className="py-8 text-center text-destructive">
          Failed to start BOM. Please go back and try again.
        </CardContent>
      </Card>
    );
  }

  if (startBom.isPending || bomLoading || (!bom && requisition?.status === "BomPending")) {
    return <p className="text-sm text-muted-foreground">Starting BOM…</p>;
  }

  if (!requisition || !bom) {
    return <p className="text-sm text-muted-foreground">Loading…</p>;
  }

  return (
    <div className="space-y-4">
      {/* Header */}
      <Link
        to={`/requisitions/${requisitionId}`}
        className="inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground"
      >
        <ArrowLeft className="h-4 w-4" /> Back to {requisition.refNo}
      </Link>

      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">
            {isReadOnly ? "BOM (read-only)" : "BOM Entry"}
          </h1>
          <p className="mt-1 text-sm text-muted-foreground">
            {requisition.itemDescription} — {requisition.customerName}
          </p>
        </div>
        <span className="text-xs text-muted-foreground">
          {saveStatus === "saving" && "Saving…"}
          {saveStatus === "saved" && "Saved ✓"}
          {saveStatus === "error" && <span className="text-destructive">Save failed</span>}
        </span>
      </div>

      {/* Net Qty warning banner */}
      {netQtyWarning && (
        <div className="rounded-md border border-yellow-600 bg-yellow-950 px-4 py-3 text-sm text-yellow-300">
          <span className="font-semibold">⚠ Net Qty/kg is {overallTotals.netQty.toFixed(4)} — expected ~1.0000.</span>{" "}
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
            {/* Process header */}
            <div className="bg-muted/50 px-4 py-3 space-y-2">
              <div className="flex items-center justify-between">
                <span className="font-semibold text-sm">⚙ {section.processName}</span>
                {!isReadOnly && (
                  <button
                    type="button"
                    onClick={() => removeProcess(section.processId)}
                    className="text-xs text-destructive hover:underline"
                  >
                    Remove process ✕
                  </button>
                )}
              </div>
              {/* Totals bar */}
              <div className="grid grid-cols-3 rounded-md border border-border overflow-hidden text-xs">
                <div className="px-3 py-2 text-center border-r border-border">
                  <div className="text-muted-foreground">Total Qty</div>
                  <div className="font-mono font-semibold">{totalQty.toFixed(4)} kg</div>
                </div>
                <div className="px-3 py-2 text-center border-r border-border">
                  <div className="text-muted-foreground">Total Waste</div>
                  <div className="font-mono font-semibold text-destructive">− {totalWaste.toFixed(4)} kg</div>
                </div>
                <div className="px-3 py-2 text-center">
                  <div className="text-muted-foreground">Net Qty</div>
                  <div className="font-mono font-semibold text-green-500">{netQty.toFixed(4)} kg</div>
                </div>
              </div>
            </div>

            {/* Column headers */}
            {(sectionLines.length > 0 || isAdding) && (
              <div className="grid grid-cols-[1fr_100px_90px_32px] gap-2 px-4 py-2 text-xs text-muted-foreground border-b border-border">
                <span>Raw Material</span>
                <span>Qty / kg</span>
                <span>Wastage %</span>
                <span />
              </div>
            )}

            {/* Lines */}
            {sectionLines.map((line) => (
              <div
                key={line.localId}
                className="grid grid-cols-[1fr_100px_90px_32px] gap-2 px-4 py-2 text-sm border-b border-border items-center"
              >
                <span>{line.rawMaterialDescription}</span>
                <span className="font-mono">{line.qtyPerKg.toFixed(4)}</span>
                <span className="font-mono">{line.wastagePct.toFixed(2)}%</span>
                {!isReadOnly && (
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

            {/* Inline add row */}
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

            {/* Duplicate warning */}
            {duplicateWarning && addingToProcess === section.processId && (
              <div className="mx-4 my-2 rounded-md border border-yellow-600 bg-yellow-950 px-3 py-2 text-xs text-yellow-300 flex items-center gap-3">
                <span>⚠ Already added under {duplicateWarning.processName}. Add anyway?</span>
                <button
                  type="button"
                  className="underline"
                  onClick={() => pendingDuplicate && commitAddLine(pendingDuplicate)}
                >
                  Yes, add
                </button>
                <button
                  type="button"
                  className="underline"
                  onClick={() => { setDuplicateWarning(null); setPendingDuplicate(null); }}
                >
                  Cancel
                </button>
              </div>
            )}

            {/* Add raw material link */}
            {!isReadOnly && !isAdding && (
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
      {!isReadOnly && (
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

      {/* Summary bar + Submit */}
      <div className="rounded-lg border border-border px-4 py-3 flex flex-wrap items-center justify-between gap-4">
        <div className="flex gap-6 text-sm">
          <div className="text-center">
            <div className="text-xs text-muted-foreground">Total Qty</div>
            <div className="font-mono font-semibold">{overallTotals.totalQty.toFixed(4)} kg</div>
          </div>
          <div className="text-center">
            <div className="text-xs text-muted-foreground">Total Waste</div>
            <div className="font-mono font-semibold text-destructive">− {overallTotals.totalWaste.toFixed(4)} kg</div>
          </div>
          <div className="text-center">
            <div className="text-xs text-muted-foreground">Net Qty / kg</div>
            <div className={`font-mono font-semibold ${netQtyWarning ? "text-yellow-400" : "text-green-500"}`}>
              {overallTotals.netQty.toFixed(4)} kg {netQtyWarning && "⚠"}
            </div>
          </div>
        </div>
        {!isReadOnly && (
          <Button
            onClick={handleSubmit}
            disabled={lines.length === 0 || submitBom.isPending}
          >
            {submitBom.isPending ? "Submitting…" : "Submit BOM ↗"}
          </Button>
        )}
      </div>
    </div>
  );
}
