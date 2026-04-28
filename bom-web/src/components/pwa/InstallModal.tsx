import { Share, Plus } from "lucide-react";
import { usePwaInstall } from "@/hooks/usePwaInstall";

export function InstallModal() {
  const { shouldShowIosModal, dismissIosModal } = usePwaInstall();

  if (!shouldShowIosModal) return null;

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/70 px-4">
      <div className="w-full max-w-md rounded-2xl bg-white p-6 shadow-2xl">
        <div className="mb-4 flex items-center gap-3">
          <div className="rounded-xl bg-blue-700 p-2">
            <img src="/apple-touch-icon-180x180.png" alt="" className="h-10 w-10 rounded-lg" />
          </div>
          <h2 className="text-xl font-semibold">Install FPF Quotations</h2>
        </div>

        <p className="mb-4 text-sm text-gray-600">
          Get the full app experience on your home screen — faster access, notifications, and offline support.
        </p>

        <div className="mb-6 space-y-3">
          <Step n={1} icon={<Share className="h-5 w-5 text-blue-600" />}>
            Tap the <b>Share</b> button at the bottom of Safari
          </Step>
          <Step n={2} icon={<Plus className="h-5 w-5 text-blue-600" />}>
            Scroll and tap <b>Add to Home Screen</b>
          </Step>
          <Step n={3}>
            Tap <b>Add</b> in the top right
          </Step>
        </div>

        <button
          type="button"
          onClick={dismissIosModal}
          className="w-full rounded-lg border border-gray-300 px-4 py-2 text-sm font-medium hover:bg-gray-50"
        >
          I'll do it later
        </button>
      </div>
    </div>
  );
}

function Step({ n, icon, children }: { n: number; icon?: React.ReactNode; children: React.ReactNode }) {
  return (
    <div className="flex items-start gap-3">
      <div className="flex h-7 w-7 shrink-0 items-center justify-center rounded-full bg-blue-100 text-sm font-semibold text-blue-700">
        {n}
      </div>
      <div className="flex flex-1 items-center gap-2 pt-0.5 text-sm text-gray-800">
        {icon} <span>{children}</span>
      </div>
    </div>
  );
}
