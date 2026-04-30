import { useMutation, useQueryClient } from "@tanstack/react-query";
import { api } from "./client";
import { requisitionKeys } from "./requisitions";

export function useAcceptCustomer() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (requisitionId: number) =>
      api.post(`/api/approvals/${requisitionId}/accept-customer`),
    onSuccess: (_d, id) => {
      qc.invalidateQueries({ queryKey: requisitionKeys.detail(id) });
      qc.invalidateQueries({ queryKey: requisitionKeys.lists() });
    },
  });
}

export function useRejectCustomer() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ requisitionId, reason }: { requisitionId: number; reason: string }) =>
      api.post(`/api/approvals/${requisitionId}/reject-customer`, { reason }),
    onSuccess: (_d, vars) => {
      qc.invalidateQueries({ queryKey: requisitionKeys.detail(vars.requisitionId) });
      qc.invalidateQueries({ queryKey: requisitionKeys.lists() });
    },
  });
}
