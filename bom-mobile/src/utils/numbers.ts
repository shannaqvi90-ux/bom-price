export function formatMoney(n: number | null | undefined): string {
  if (n == null || Number.isNaN(n)) return "-";
  return n.toFixed(4);
}

export function formatPct(n: number | null | undefined): string {
  if (n == null || Number.isNaN(n)) return "-";
  return `${n.toFixed(1)}%`;
}

const currencyFormatter = new Intl.NumberFormat("en-US", {
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
});

export function formatCurrency(
  n: number | null | undefined,
  code: string
): string {
  if (n == null || Number.isNaN(n)) return "-";
  return `${code} ${currencyFormatter.format(n)}`;
}
