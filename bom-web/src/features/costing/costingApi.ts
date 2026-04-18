import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/api/axios";
import type { CostingReviewResponse, LandedCostType } from "@/types/api";
import { requisitionKeys } from "@/features/requisitions/requisitionsApi";

export const costingKeys = {
  detail: (requisitionId: number) => ["costing", requisitionId] as const,
};

export interface CostingLinePayload {
  bomLineId: number;
  costPerKg: number;
  currencyCode: string;
}

export interface SaveCostingDraftPayload {
  lines: CostingLinePayload[];
  landedCostType: LandedCostType;
  landedCostValue: number;
  fohAmount: number;
}

export interface SubmitCostingPayload {
  rawMaterialCosts: CostingLinePayload[];
  landedCostType: LandedCostType;
  landedCostValue: number;
  fohAmount: number;
}

export function useCosting(requisitionId: number) {
  return useQuery({
    queryKey: costingKeys.detail(requisitionId),
    queryFn: () =>
      api.get<CostingReviewResponse>(`/costing/${requisitionId}`).then((r) => r.data),
    enabled: Number.isFinite(requisitionId) && requisitionId > 0,
    staleTime: 30_000,
  });
}

export function useStartCostingItem() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({
      requisitionId,
      requisitionItemId,
    }: {
      requisitionId: number;
      requisitionItemId: number;
    }) =>
      api.post(`/costing/${requisitionId}/items/${requisitionItemId}/start`),
    onSuccess: (_d, { requisitionId }) => {
      qc.invalidateQueries({ queryKey: costingKeys.detail(requisitionId) });
      qc.invalidateQueries({ queryKey: requisitionKeys.detail(requisitionId) });
    },
  });
}

export function useSaveCostingItemDraft() {
  return useMutation({
    mutationFn: ({
      requisitionId,
      requisitionItemId,
      payload,
    }: {
      requisitionId: number;
      requisitionItemId: number;
      payload: SaveCostingDraftPayload;
    }) =>
      api.put(`/costing/${requisitionId}/items/${requisitionItemId}/draft`, payload),
  });
}

export function useSubmitCostingItem() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({
      requisitionId,
      requisitionItemId,
      payload,
    }: {
      requisitionId: number;
      requisitionItemId: number;
      payload: SubmitCostingPayload;
    }) =>
      api.post(`/costing/${requisitionId}/items/${requisitionItemId}/submit`, payload),
    onSuccess: (_d, { requisitionId }) => {
      qc.invalidateQueries({ queryKey: requisitionKeys.detail(requisitionId) });
      qc.invalidateQueries({ queryKey: requisitionKeys.list() });
      qc.invalidateQueries({ queryKey: costingKeys.detail(requisitionId) });
    },
  });
}
