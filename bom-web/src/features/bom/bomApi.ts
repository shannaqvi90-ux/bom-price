import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/api/axios";
import type { BomReviewResponse } from "@/types/api";

export const bomKeys = {
  detail: (requisitionId: number) => ["bom", requisitionId] as const,
};

interface BomLinePayload {
  processId: number;
  rawMaterialItemId: number;
  qtyPerKg: number;
  wastagePct: number;
}

export function useBom(requisitionId: number) {
  return useQuery({
    queryKey: bomKeys.detail(requisitionId),
    queryFn: () =>
      api.get<BomReviewResponse>(`/bom/${requisitionId}`).then((r) => r.data),
    enabled: Number.isFinite(requisitionId) && requisitionId > 0,
    retry: false,
  });
}

export function useStartBomItem() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({
      requisitionId,
      requisitionItemId,
    }: {
      requisitionId: number;
      requisitionItemId: number;
    }) =>
      api
        .post<{ id: number }>(`/bom/${requisitionId}/items/${requisitionItemId}/start`)
        .then((r) => r.data),
    onSuccess: (_data, { requisitionId }) => {
      qc.invalidateQueries({ queryKey: bomKeys.detail(requisitionId) });
    },
  });
}

export function useSaveBomItemLines() {
  return useMutation({
    mutationFn: ({
      requisitionId,
      requisitionItemId,
      lines,
    }: {
      requisitionId: number;
      requisitionItemId: number;
      lines: BomLinePayload[];
    }) =>
      api.put(`/bom/${requisitionId}/items/${requisitionItemId}/lines`, { lines }),
  });
}

export function useSubmitBom() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (requisitionId: number) =>
      api.post(`/bom/${requisitionId}/submit`),
    onSuccess: (_data, requisitionId) => {
      qc.invalidateQueries({ queryKey: ["requisitions"] });
      qc.invalidateQueries({ queryKey: bomKeys.detail(requisitionId) });
    },
  });
}
