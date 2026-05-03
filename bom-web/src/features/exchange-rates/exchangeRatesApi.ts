import { useMemo } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/api/axios";
import type {
  ExchangeRate,
  CreateExchangeRateRequest,
  UpdateExchangeRateRequest,
} from "@/types/api";

export const exchangeRateKeys = {
  all: ["exchange-rates"] as const,
  list: () => [...exchangeRateKeys.all, "list"] as const,
};

export function useExchangeRates() {
  return useQuery({
    queryKey: exchangeRateKeys.list(),
    queryFn: () =>
      api.get<ExchangeRate[]>("/exchange-rates").then((r) => r.data),
  });
}

// Active currency codes for use in pickers (requisition currency, RM cost
// currency, printing currency, etc.). Always includes AED (the base currency,
// never present in the rates table) plus every active rate's currency code.
// While loading, returns AED only as a safe fallback so pickers don't show
// an empty dropdown. Sorted alphabetically after AED.
export function useActiveCurrencies(): string[] {
  const { data } = useExchangeRates();
  return useMemo(() => {
    const codes = (data ?? [])
      .filter((r) => r.isActive)
      .map((r) => r.currencyCode.toUpperCase())
      .filter((c) => c !== "AED");
    const unique = Array.from(new Set(codes)).sort();
    return ["AED", ...unique];
  }, [data]);
}

export function useCreateRate() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateExchangeRateRequest) =>
      api.post<ExchangeRate>("/exchange-rates", body).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: exchangeRateKeys.all }),
  });
}

export function useUpdateRate() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, data }: { id: number; data: UpdateExchangeRateRequest }) =>
      api.put(`/exchange-rates/${id}`, data),
    onSuccess: () => qc.invalidateQueries({ queryKey: exchangeRateKeys.all }),
  });
}
