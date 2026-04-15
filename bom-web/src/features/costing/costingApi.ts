import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/api/axios";
import type { CostingDetail, LandedCostType } from "@/types/api";
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
      api.get<CostingDetail>(`/costing/${requisitionId}`).then((r) => r.data),
    enabled: Number.isFinite(requisitionId) && requisitionId > 0,
  });
}

export function useStartCosting() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (requisitionId: number) =>
      api.post(`/costing/${requisitionId}/start`),
    onSuccess: (_d, requisitionId) => {
      qc.invalidateQueries({ queryKey: costingKeys.detail(requisitionId) });
      qc.invalidateQueries({ queryKey: requisitionKeys.detail(requisitionId) });
    },
  });
}

export function useSaveCostingDraft() {
  return useMutation({
    mutationFn: ({
      requisitionId,
      payload,
    }: {
      requisitionId: number;
      payload: SaveCostingDraftPayload;
    }) => api.put(`/costing/${requisitionId}/draft`, payload),
  });
}

export function useSubmitCosting() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({
      requisitionId,
      payload,
    }: {
      requisitionId: number;
      payload: SubmitCostingPayload;
    }) => api.post(`/costing/${requisitionId}/submit`, payload),
    onSuccess: (_d, { requisitionId }) => {
      qc.invalidateQueries({ queryKey: ["requisitions"] });
      qc.invalidateQueries({ queryKey: costingKeys.detail(requisitionId) });
    },
  });
}
