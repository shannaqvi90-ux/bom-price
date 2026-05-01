import { useMutation, useQueryClient } from "@tanstack/react-query";
import { api } from "./client";
import type { SaveV3CostDataPayload } from "@/types/v3";
import { requisitionKeys } from "./requisitions";

export function useSaveV3CostData(requisitionId: number) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (payload: SaveV3CostDataPayload) => {
      await api.put(`/api/costing/${requisitionId}/cost-data`, payload);
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: requisitionKeys.detail(requisitionId) });
    },
  });
}

export function useSubmitV3Costing(requisitionId: number) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async () => {
      await api.post(`/api/costing/${requisitionId}/submit`);
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: requisitionKeys.detail(requisitionId) });
      qc.invalidateQueries({ queryKey: requisitionKeys.lists() });
      qc.invalidateQueries({ queryKey: ["stats", "accountantDashboardV3"] });
    },
  });
}
