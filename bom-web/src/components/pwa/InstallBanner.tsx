import { useState } from "react";
import { Download, X } from "lucide-react";
import { usePwaInstall } from "@/hooks/usePwaInstall";

export function InstallBanner() {
  const { canPromptInstall, promptInstall } = usePwaInstall();
  const [dismissed, setDismissed] = useState(false);

  if (!canPromptInstall || dismissed) return null;

  return (
    <div className="fixed top-3 right-3 z-40 flex items-center gap-2 rounded-lg bg-blue-700 px-3 py-2 text-sm text-white shadow-lg">
      <Download className="h-4 w-4" />
      <span>Install FPF Quotations</span>
      <button
        type="button"
        onClick={promptInstall}
        className="rounded bg-card px-2 py-0.5 text-xs font-medium text-blue-700"
      >
        Install
      </button>
      <button
        type="button"
        onClick={() => setDismissed(true)}
        className="ml-1 opacity-70 hover:opacity-100"
        aria-label="Dismiss"
      >
        <X className="h-4 w-4" />
      </button>
    </div>
  );
}
