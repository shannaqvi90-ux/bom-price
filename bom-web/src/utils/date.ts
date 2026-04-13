const rtf = new Intl.RelativeTimeFormat("en", { numeric: "always" });

export function formatRelative(iso: string, now: Date = new Date()): string {
  const then = new Date(iso);
  const diffMs = then.getTime() - now.getTime();
  const absSec = Math.abs(diffMs) / 1000;

  if (absSec < 60) return "just now";
  if (absSec < 3600) return rtf.format(Math.round(diffMs / 60_000), "minute");
  if (absSec < 86_400) return rtf.format(Math.round(diffMs / 3_600_000), "hour");
  if (absSec < 7 * 86_400) return rtf.format(Math.round(diffMs / 86_400_000), "day");

  return then.toLocaleDateString();
}
