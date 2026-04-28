import { useEffect, useState } from "react";
import { WifiOff } from "lucide-react";

export function OfflineBanner() {
  const [isOffline, setIsOffline] = useState(() => !navigator.onLine);

  useEffect(() => {
    const onOnline = () => setIsOffline(false);
    const onOffline = () => setIsOffline(true);
    window.addEventListener("online", onOnline);
    window.addEventListener("offline", onOffline);
    return () => {
      window.removeEventListener("online", onOnline);
      window.removeEventListener("offline", onOffline);
    };
  }, []);

  if (!isOffline) return null;

  return (
    <div className="sticky top-0 z-40 flex items-center justify-center gap-2 bg-red-600 px-4 py-2 text-sm font-medium text-white">
      <WifiOff className="h-4 w-4" />
      Offline — showing last cached data. New requisitions will fail until reconnected.
    </div>
  );
}
