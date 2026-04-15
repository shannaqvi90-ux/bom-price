import { useRef, useState } from "react";
import { Dialog } from "@/components/ui/Dialog";
import { Button } from "@/components/ui/Button";
import { useImportItems, downloadItemTemplate } from "./itemsApi";
import type { ImportResult } from "@/types/api";

interface Props {
  open: boolean;
  onClose: () => void;
  branches: { id: number; name: string }[];
}

export function ImportItemsModal({ open, onClose, branches }: Props) {
  const fileRef = useRef<HTMLInputElement>(null);
  const [fileName, setFileName] = useState<string | null>(null);
  const [branchId, setBranchId] = useState<number>(branches[0]?.id ?? 1);
  const [result, setResult] = useState<ImportResult | null>(null);
  const importMutation = useImportItems();

  function handleFileChange(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0] ?? null;
    setFileName(file?.name ?? null);
    importMutation.reset();
    setResult(null);
  }

  async function handleImport() {
    const file = fileRef.current?.files?.[0];
    if (!file) return;
    const r = await importMutation.mutateAsync({ file, branchId });
    setResult(r);
  }

  function handleClose() {
    if (fileRef.current) fileRef.current.value = "";
    setFileName(null);
    setResult(null);
    importMutation.reset();
    onClose();
  }

  const isDone = result !== null;

  return (
    <Dialog open={open} onClose={handleClose} title="Import Items">
      <div className="space-y-4">
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

        <button
          type="button"
          onClick={() => downloadItemTemplate()}
          className="text-sm text-primary underline-offset-4 hover:underline"
        >
          Download template (.xlsx)
        </button>

        <div className="space-y-1">
          <label className="text-sm font-medium">File (.xlsx or .csv)</label>
          <input
            ref={fileRef}
            type="file"
            accept=".xlsx,.csv"
            onChange={handleFileChange}
            className="block w-full text-sm text-foreground file:mr-3 file:rounded-md file:border-0 file:bg-muted file:px-3 file:py-1.5 file:text-sm file:font-medium"
          />
          {fileName && !isDone && (
            <p className="text-xs text-muted-foreground">{fileName}</p>
          )}
        </div>

        {importMutation.isError && (
          <p className="text-sm text-destructive">
            {(importMutation.error as { response?: { data?: { message?: string } } })?.response
              ?.data?.message ?? "Import failed"}
          </p>
        )}

        {isDone && result && (
          <div className="rounded-md border border-border bg-muted/40 p-3 text-sm space-y-1">
            <p className="font-medium">Import complete</p>
            <p>Imported: <span className="font-semibold text-green-600">{result.imported}</span></p>
            <p>Skipped: <span className="font-semibold">{result.skipped}</span></p>
            {result.errors.length > 0 && (
              <ul className="mt-1 list-disc pl-4 text-xs text-destructive space-y-0.5">
                {result.errors.map((e, i) => <li key={i}>{e}</li>)}
              </ul>
            )}
          </div>
        )}

        <div className="flex justify-end gap-2 pt-2">
          <Button type="button" variant="ghost" onClick={handleClose}>
            {isDone ? "Close" : "Cancel"}
          </Button>
          {!isDone && (
            <Button
              type="button"
              onClick={handleImport}
              disabled={!fileName || importMutation.isPending}
            >
              {importMutation.isPending ? "Importing…" : "Import"}
            </Button>
          )}
        </div>
      </div>
    </Dialog>
  );
}
