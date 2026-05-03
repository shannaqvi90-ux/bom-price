import { useState } from "react";
import { toast } from "sonner";
import {
  useUploadSignature,
  useOwnSignatureBlobUrl,
} from "@/features/profile/profileApi";

export function ProfileSignaturePage() {
  const [file, setFile] = useState<File | null>(null);
  const [previewUrl, setPreviewUrl] = useState<string | null>(null);
  const upload = useUploadSignature();
  const ownSig = useOwnSignatureBlobUrl();

  const onFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const f = e.target.files?.[0] ?? null;
    setFile(f);
    if (f) setPreviewUrl(URL.createObjectURL(f));
  };

  const onUpload = async () => {
    if (!file) {
      toast.error("Pick a file first");
      return;
    }
    if (file.size > 500 * 1024) {
      toast.error("File too large (max 500KB)");
      return;
    }
    try {
      await upload.mutateAsync(file);
      toast.success("Signature uploaded");
    } catch (err: unknown) {
      const message =
        (err as { response?: { data?: { error?: string } } } | null)?.response?.data?.error
        ?? "Upload failed";
      toast.error(message);
    }
  };

  return (
    <div className="mx-auto max-w-2xl p-6">
      <h1 className="text-2xl font-semibold text-foreground">Profile · Signature</h1>
      <p className="mt-1 text-sm text-muted-foreground">
        Uploaded signature appears on signed quotation PDFs. PNG/JPG ≤ 500KB.
        ~600×200px recommended.
      </p>

      <div className="mt-6 rounded-lg border border-border bg-card p-6">
        <h2 className="text-sm font-medium text-foreground">Current signature</h2>
        <div className="mt-2 flex h-24 items-center justify-center rounded-md border border-dashed border-border bg-muted">
          {ownSig.data ? (
            <img src={ownSig.data} alt="Current signature" className="max-h-20" />
          ) : (
            <span className="text-xs text-muted-foreground">[no signature uploaded]</span>
          )}
        </div>

        <h2 className="mt-6 text-sm font-medium text-foreground">Upload new</h2>
        <input
          type="file"
          accept="image/png,image/jpeg"
          onChange={onFileChange}
          aria-label="upload signature"
          className="mt-2 block w-full rounded-md border border-input bg-background px-3 py-2 text-sm text-foreground file:mr-3 file:rounded file:border-0 file:bg-blue-50 file:px-3 file:py-1 file:text-sm file:text-blue-700 hover:file:bg-blue-100"
        />

        {previewUrl && (
          <div className="mt-3">
            <p className="text-xs text-muted-foreground">Preview:</p>
            <img
              src={previewUrl}
              alt="Preview"
              className="mt-1 max-h-24 rounded border border-border"
            />
          </div>
        )}

        <button
          onClick={onUpload}
          disabled={!file || upload.isPending}
          className="mt-4 rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50"
        >
          {upload.isPending ? "Uploading…" : "Upload"}
        </button>
      </div>
    </div>
  );
}
