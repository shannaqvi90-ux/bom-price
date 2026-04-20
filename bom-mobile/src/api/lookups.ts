import { useQuery } from "@tanstack/react-query";
import { api } from "./client";
import type { Customer, Item, ExchangeRate } from "@/types/api";

export function useCustomers() {
  return useQuery({
    queryKey: ["customers"],
    queryFn: async () => {
      const res = await api.get<Customer[]>("/api/customers");
      return res.data;
    },
    staleTime: 60_000,
  });
}

export function useItems() {
  return useQuery({
    queryKey: ["items"],
    queryFn: async () => {
      const res = await api.get<Item[]>("/api/items");
      return res.data;
    },
    staleTime: 60_000,
    select: (items) => items.filter((i) => i.isActive),
  });
}

export function useExchangeRates() {
  return useQuery({
    queryKey: ["exchange-rates"],
    queryFn: async () => {
      const res = await api.get<ExchangeRate[]>("/api/exchange-rates");
      return res.data;
    },
    staleTime: 300_000,
    select: (rates) => rates.filter((r) => r.isActive),
  });
}
