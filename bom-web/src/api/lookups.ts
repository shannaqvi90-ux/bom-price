import { useQuery } from "@tanstack/react-query";
import { api } from "@/api/axios";
import type { Customer, Item, ExchangeRate, Process } from "@/types/api";

const FIVE_MINUTES = 5 * 60 * 1000;

export function useBranches(options?: { enabled?: boolean }) {
  return useQuery({
    queryKey: ["branches"],
    queryFn: () =>
      api.get<{ id: number; name: string }[]>("/branches").then((r) => r.data),
    staleTime: FIVE_MINUTES,
    enabled: options?.enabled ?? true,
  });
}

export function useCustomers() {
  return useQuery({
    queryKey: ["customers"],
    queryFn: () => api.get<Customer[]>("/customers").then((r) => r.data),
    staleTime: FIVE_MINUTES,
  });
}

export interface UseItemsOptions {
  branchId?: number;
  type?: "FinishedGood" | "RawMaterial";
}

export function useItems(opts: UseItemsOptions = {}) {
  const params = new URLSearchParams();
  if (opts.branchId) params.append("branchId", String(opts.branchId));
  if (opts.type) params.append("type", opts.type);
  const qs = params.toString();

  return useQuery({
    queryKey: ["items", "list", { branchId: opts.branchId, type: opts.type }],
    queryFn: async () =>
      (await api.get<Item[]>(`/items${qs ? `?${qs}` : ""}`)).data,
    staleTime: FIVE_MINUTES,
  });
}

export function useActiveExchangeRates() {
  return useQuery({
    queryKey: ["exchangeRates", "active"],
    queryFn: () =>
      api.get<ExchangeRate[]>("/exchange-rates/active").then((r) => r.data),
    staleTime: FIVE_MINUTES,
  });
}

export function useProcesses() {
  return useQuery({
    queryKey: ["processes"],
    queryFn: () => api.get<Process[]>("/processes").then((r) => r.data),
    staleTime: FIVE_MINUTES,
  });
}
