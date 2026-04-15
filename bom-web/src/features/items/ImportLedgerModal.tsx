import { useRef, useState } from "react";
import { Dialog } from "@/components/ui/Dialog";
import { Button } from "@/components/ui/Button";
import { useLedgerHeaders, useLedgerImport } from "./itemsApi";
import type { LedgerImportResult } from "@/types/api";

interface Props {
  open: boolean;
  onClose: () => void;
  branches: { id: number; name: string }[];
}

type Step = 1 | 2 | 3;

export function ImportLedgerModal({ open, onClose, branches }: Props) {
  const headersMutation = useLedgerHeaders();
  const importMutation = useLedgerImport();
  const fileRef = useRef<HTMLInputElement>(null);

  const [step, setStep] = useState<Step>(1);
  const [file, setFile] = useState<File | null>(null);
  const [branchId, setBranchId] = useState<number>(branches[0]?.id ?? 1);
  const [headers, setHeaders] = useState<string[]>([]);
  const [codeCol, setCodeCol] = useState("");
  const [dateCol, setDateCol] = useState("");
  const [priceCol, setPriceCol] = useState("");
  const [result, setResult] = useState<LedgerImportResult | null>(null);
  const [error, setError] = useState<string | null>(null);

  function reset() {
    setStep(1);
    setFile(null);
    setHeaders([]);
    setCodeCol("");
    setDateCol("");
    setPriceCol("");
    setResult(null);
    setError(null);
    headersMutation.reset();
    importMutation.reset();
    if (fileRef.current) fileRef.current.value = "";
  }

  function handleClose() {
    reset();
    onClose();
  }

  async function goToMapping() {
    if (!file) return;
    setError(null);
    try {
      const r = await headersMutation.mutateAsync(file);
      setHeaders(r.headers);
      if (r.headers.length > 0) {
        setCodeCol(r.headers[0]);
        setDateCol(r.headers[Math.min(1, r.headers.length - 1)]);
        setPriceCol(r.headers[Math.min(2, r.headers.length - 1)]);
      }
      setStep(2);
    } catch (e: unknown) {
      const err = e as { response?: { data?: { message?: string } } };
      setError(err.response?.data?.message ?? "Failed to read headers");
    }
  }

  async function runImport() {
    if (!file || !codeCol || !dateCol || !priceCol) return;
    setError(null);
    try {
      const r = await importMutation.mutateAsync({
        file, itemCodeColumn: codeCol, dateColumn: dateCol,
        unitPriceColumn: priceCol, branchId,
      });
      setResult(r);
      setStep(3);
    } catch (e: unknown) {
      const err = e as { response?: { data?: { message?: string } } };
      setError(err.response?.data?.message ?? "Import failed");
    }
  }

  const titles: Record<Step, string> = {
    1: "Import Ledger — Step 1 of 3: Upload",
    2: "Import Ledger — Step 2 of 3: Map Columns",
    3: "Import Ledger — Step 3 of 3: Result",
  };

  return (
    <Dialog open={open} onClose={handleClose} title={titles[step]}>
      <div className="space-y-4">
        {/* Step 1: file + branch */}
        {step === 1 && (
          <>
            <div className="space-y-1">
              <label className="text-sm font-medium">Branch</label>
              <select
                className="flex h-10 w-full rounded-md border border-input bg-background px-3 py-2 text-sm"
                value={branchId}
                onChange={(e) => setBranchId(Number(e.target.value))}
              >
                {branches.map((b) => (
                  <option key={b.id} value={b.id}>{b.name}</option>
                ))}
              </select>
            </div>

            <div className="space-y-1">
              <label className="text-sm font-medium">ERP Excel file (.xlsx)</label>
              <input
                ref={fileRef}
                type="file"
                accept=".xlsx"
                onChange={(e) => setFile(e.target.files?.[0] ?? null)}
                className="block w-full text-sm text-foreground file:mr-3 file:rounded-md file:border-0 file:bg-muted file:px-3 file:py-1.5 file:text-sm file:font-medium"
              />
            </div>
          </>
        )}

        {/* Step 2: column mapping */}
        {step === 2 && (
          <>
            <p className="text-sm text-muted-foreground">
              Map the columns from your file to the required fields.
            </p>
            {(
              [
                ["Item Code column", codeCol, setCodeCol],
                ["Date column", dateCol, setDateCol],
                ["Unit Price column", priceCol, setPriceCol],
              ] as [string, string, (v: string) => void][]
            ).map(([label, value, setter]) => (
              <div key={label} className="space-y-1">
                <label className="text-sm font-medium">{label}</label>
                <select
                  className="flex h-10 w-full rounded-md border border-input bg-background px-3 py-2 text-sm"
                  value={value}
                  onChange={(e) => setter(e.target.value)}
                >
                  {headers.map((h) => (
                    <option key={h} value={h}>{h}</option>
                  ))}
                </select>
              </div>
            ))}
          </>
        )}

        {/* Step 3: result */}
        {step === 3 && result && (
          <div className="space-y-3">
            <div className="rounded-md border border-border bg-muted/40 p-3 text-sm space-y-1">
              <p className="font-medium">Import complete</p>
              <p>Updated: <span className="font-semibold text-green-600">{result.updated}</span></p>
              <p>Skipped: <span className="font-semibold">{result.skipped}</span></p>
            </div>
            {result.unmatchedCodes.length > 0 && (
              <div className="space-y-1">
                <p className="text-sm font-medium">Codes not found in system:</p>
                <ul className="list-disc pl-4 text-xs text-muted-foreground space-y-0.5">
                  {result.unmatchedCodes.slice(0, 20).map((c) => (
                    <li key={c}>{c}</li>
                  ))}
                  {result.unmatchedCodes.length > 20 && (
                    <li>…and {result.unmatchedCodes.length - 20} more</li>
                  )}
                </ul>
              </div>
            )}
          </div>
        )}

        {error && <p className="text-sm text-destructive">{error}</p>}

        <div className="flex justify-end gap-2 pt-2">
          {step === 1 && (
            <>
              <Button type="button" variant="ghost" onClick={handleClose}>Cancel</Button>
              <Button
                type="button"
                onClick={goToMapping}
                disabled={!file || headersMutation.isPending}
              >
                {headersMutation.isPending ? "Reading…" : "Next"}
              </Button>
            </>
          )}
          {step === 2 && (
            <>
              <Button type="button" variant="ghost" onClick={() => setStep(1)}>Back</Button>
              <Button
                type="button"
                onClick={runImport}
                disabled={importMutation.isPending}
              >
                {importMutation.isPending ? "Importing…" : "Import"}
              </Button>
            </>
          )}
          {step === 3 && (
            <Button type="button" onClick={handleClose}>Done</Button>
          )}
        </div>
      </div>
    </Dialog>
  );
}
