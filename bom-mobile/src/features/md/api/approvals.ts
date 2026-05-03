import { useMutation, useQueryClient } from "@tanstack/react-query";
import { api } from "@/api/client";
import { requisitionKeys } from "@/api/requisitions";

export interface SetMarginItem {
  requisitionItemId: number;
  marginPerKg: number;
}

export interface SetMarginPayload {
  items: SetMarginItem[];
  notes?: string;
}

export function useSetMargin(reqId: number) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (payload: SetMarginPayload) =>
      api.post(`/api/approvals/${reqId}/set-margin`, payload),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: requisitionKeys.detail(reqId) });
      qc.invalidateQueries({ queryKey: requisitionKeys.lists() });
    },
  });
}

export interface RejectPayload {
  reason: string;
}

export function useRejectRequisition(reqId: number) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (payload: RejectPayload) =>
      api.post(`/api/approvals/${reqId}/reject`, payload),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: requisitionKeys.detail(reqId) });
      qc.invalidateQueries({ queryKey: requisitionKeys.lists() });
    },
  });
}

export interface FinalSignPayload {
  confirmationToken: string;
  notes?: string;
}

export function useFinalSign(reqId: number) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (payload: FinalSignPayload) =>
      api.post(`/api/approvals/${reqId}/final-sign`, payload),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: requisitionKeys.detail(reqId) });
      qc.invalidateQueries({ queryKey: requisitionKeys.lists() });
    },
  });
}
