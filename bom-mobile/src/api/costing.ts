import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "./client";
import type {
  CostingReviewResponse,
  SaveCostingDraftRequest,
  SubmitCostingRequest,
} from "@/types/api";

export const costingKeys = {
  review: (requisitionId: number) => ["costing", "review", requisitionId] as const,
};

export function useCostingReview(requisitionId: number, enabled = true) {
  return useQuery({
    queryKey: costingKeys.review(requisitionId),
    queryFn: async () => {
      const res = await api.get<CostingReviewResponse>(`/api/costing/${requisitionId}`);
      return res.data;
    },
    enabled: enabled && Number.isFinite(requisitionId) && requisitionId > 0,
    staleTime: 10_000,
    retry: false,
  });
}

export function useStartCostingItem(requisitionId: number) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (requisitionItemId: number) => {
      await api.post(
        `/api/costing/${requisitionId}/items/${requisitionItemId}/start`,
      );
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: costingKeys.review(requisitionId) });
      qc.invalidateQueries({ queryKey: ["requisition", requisitionId] });
    },
  });
}

export function useSaveCostingItemDraft(requisitionId: number, requisitionItemId: number) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (payload: SaveCostingDraftRequest) => {
      await api.put(
        `/api/costing/${requisitionId}/items/${requisitionItemId}/draft`,
        payload,
      );
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: costingKeys.review(requisitionId) }),
  });
}

export function useSubmitCostingItem(requisitionId: number, requisitionItemId: number) {
  const qc = useQueryClient();
  return useMutation({
    // Plan said no-arg; backend requires SubmitCostingRequest body. Corrected here.
    mutationFn: async (payload: SubmitCostingRequest) => {
      await api.post(
        `/api/costing/${requisitionId}/items/${requisitionItemId}/submit`,
        payload,
      );
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: costingKeys.review(requisitionId) });
      qc.invalidateQueries({ queryKey: ["requisition", requisitionId] });
      qc.invalidateQueries({ queryKey: ["requisitions"] });
    },
  });
}
