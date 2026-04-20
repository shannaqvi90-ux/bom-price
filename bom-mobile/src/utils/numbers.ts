export function formatMoney(n: number | null | undefined): string {
  if (n == null || Number.isNaN(n)) return "-";
  return n.toFixed(4);
}

export function formatPct(n: number | null | undefined): string {
  if (n == null || Number.isNaN(n)) return "-";
  return `${n.toFixed(1)}%`;
}
