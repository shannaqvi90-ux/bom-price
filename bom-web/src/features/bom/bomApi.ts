import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/api/axios";
import type { BomDetail } from "@/types/api";

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
      api.get<BomDetail>(`/bom/${requisitionId}`).then((r) => r.data),
    enabled: Number.isFinite(requisitionId) && requisitionId > 0,
    retry: false,
  });
}

export function useStartBom() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (requisitionId: number) =>
      api.post<{ id: number }>(`/bom/${requisitionId}/start`).then((r) => r.data),
    onSuccess: (_data, requisitionId) => {
      qc.invalidateQueries({ queryKey: bomKeys.detail(requisitionId) });
    },
  });
}

export function useSaveBomLines() {
  return useMutation({
    mutationFn: ({ requisitionId, lines }: { requisitionId: number; lines: BomLinePayload[] }) =>
      api.put(`/bom/${requisitionId}/lines`, { lines }),
  });
}

export function useSubmitBom() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ requisitionId, lines }: { requisitionId: number; lines: BomLinePayload[] }) =>
      api.post(`/bom/${requisitionId}/submit`, { lines }),
    onSuccess: (_data, { requisitionId }) => {
      qc.invalidateQueries({ queryKey: ["requisitions"] });
      qc.invalidateQueries({ queryKey: bomKeys.detail(requisitionId) });
    },
  });
}
