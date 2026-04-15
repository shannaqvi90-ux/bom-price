import { useRef, useState } from "react";
import { Dialog } from "@/components/ui/Dialog";
import { Button } from "@/components/ui/Button";
import { useImportCustomers } from "./customersApi";
import { api } from "@/api/axios";

interface Props {
  open: boolean;
  onClose: () => void;
}

export function ImportCustomersModal({ open, onClose }: Props) {
  const fileRef = useRef<HTMLInputElement>(null);
  const [fileName, setFileName] = useState<string | null>(null);
  const importMutation = useImportCustomers();

  function handleFileChange(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0] ?? null;
    setFileName(file?.name ?? null);
    importMutation.reset();
  }

  async function handleImport() {
    const file = fileRef.current?.files?.[0];
    if (!file) return;
    try {
      await importMutation.mutateAsync(file);
    } catch {
      // error displayed via importMutation.isError
    }
  }

  function handleClose() {
    if (fileRef.current) fileRef.current.value = "";
    setFileName(null);
    importMutation.reset();
    onClose();
  }

  async function handleDownloadTemplate() {
    const resp = await api.get("/customers/import/template", { responseType: "blob" });
    const url = URL.createObjectURL(resp.data as Blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = "customers-template.xlsx";
    a.click();
    URL.revokeObjectURL(url);
  }

  const result = importMutation.data;
  const isDone = importMutation.isSuccess;

  return (
    <Dialog open={open} onClose={handleClose} title="Import Customers">
      <div className="space-y-4">
        <button
          type="button"
          onClick={handleDownloadTemplate}
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
