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
