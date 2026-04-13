import { useQuery } from "@tanstack/react-query";
import { api } from "@/api/axios";
import type { Customer, Item, ExchangeRate } from "@/types/api";

const FIVE_MINUTES = 5 * 60 * 1000;

export function useCustomers() {
  return useQuery({
    queryKey: ["customers"],
    queryFn: () => api.get<Customer[]>("/customers").then((r) => r.data),
    staleTime: FIVE_MINUTES,
  });
}

export function useItems() {
  return useQuery({
    queryKey: ["items"],
    queryFn: () => api.get<Item[]>("/items").then((r) => r.data),
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
