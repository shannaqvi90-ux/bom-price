export function extractApiError(err: unknown, fallback = "Something went wrong"): string {
  if (err && typeof err === "object" && "response" in err) {
    const resp = (err as { response?: { data?: { detail?: unknown } } }).response;
    const detail = resp?.data?.detail;
    if (typeof detail === "string" && detail.length > 0) return detail;
  }
  return fallback;
}

export function extractFieldErrors(err: unknown): Record<string, string> {
  if (!err || typeof err !== "object" || !("response" in err)) return {};
  const raw = (err as { response?: { data?: { errors?: unknown } } }).response?.data?.errors;
  if (!raw || typeof raw !== "object") return {};

  const out: Record<string, string> = {};
  for (const [key, value] of Object.entries(raw)) {
    if (Array.isArray(value) && typeof value[0] === "string") {
      out[normalizeFieldKey(key)] = value[0];
    }
  }
  return out;
}

function normalizeFieldKey(key: string): string {
  // "Items[2].ExpectedQty" → "items.2.expectedQty"
  return key
    .replace(/\[(\d+)\]/g, ".$1")
    .split(".")
    .map((seg) => (seg === "" || /^\d+$/.test(seg) ? seg : seg.charAt(0).toLowerCase() + seg.slice(1)))
    .join(".");
}
