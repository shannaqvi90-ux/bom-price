import { toast } from "sonner";
import { extractApiError } from "./apiError";

export const notify = {
  error(message: string, opts?: { duration?: number }) {
    toast.error(message, { duration: opts?.duration ?? 6000 });
  },

  success(message: string, opts?: { duration?: number }) {
    toast.success(message, { duration: opts?.duration ?? 3000 });
  },

  info(
    message: string,
    opts?: { duration?: number; action?: { label: string; onClick: () => void } },
  ) {
    toast(message, {
      duration: opts?.duration ?? 4000,
      action: opts?.action,
    });
  },

  fromApiError(err: unknown, fallback = "Something went wrong") {
    toast.error(extractApiError(err, fallback), { duration: 6000 });
  },
};
