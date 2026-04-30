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

// V3 — bulk cost-data upsert. Replaces V2.3 per-FG Start→Draft→SubmitItem cycle
// (Decision #17 — accountant costs all FGs together in one shot).
export interface V3RawMaterialCostInput {
  bomLineId: number;
  costPerKg: number;
  currencyCode: string;
}

export interface V3FgCostInput {
  requisitionItemId: number;
  rawMaterialCosts: V3RawMaterialCostInput[];
  printingCostPerKg: number | null;
  printingCostCurrency: string | null;
  fohPerKg: number;
  transportPerKg: number;
  commissionPerKg: number;
}

export interface SaveV3CostDataPayload {
  finishedGoods: V3FgCostInput[];
}

export function useSaveV3CostData() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({
      requisitionId,
      payload,
    }: {
      requisitionId: number;
      payload: SaveV3CostDataPayload;
    }) => api.put(`/costing/${requisitionId}/cost-data`, payload),
    onSuccess: (_d, { requisitionId }) => {
      qc.invalidateQueries({ queryKey: requisitionKeys.detail(requisitionId) });
      qc.invalidateQueries({ queryKey: costingKeys.detail(requisitionId) });
    },
  });
}

// V3 — overall submit (Costing -> MdPricing). The existing /api/costing/{id}/submit
// endpoint takes no body and validates that every FG already has a BomCost row.
export function useSubmitV3Costing() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ requisitionId }: { requisitionId: number }) =>
      api.post(`/costing/${requisitionId}/submit`),
    onSuccess: (_d, { requisitionId }) => {
      qc.invalidateQueries({ queryKey: requisitionKeys.detail(requisitionId) });
      qc.invalidateQueries({ queryKey: requisitionKeys.list() });
      qc.invalidateQueries({ queryKey: costingKeys.detail(requisitionId) });
    },
  });
}
