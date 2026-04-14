import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/api/axios";
import type { MdReviewDetail } from "@/types/api";

export const approvalKeys = {
  review: (requisitionId: number) => ["approval", "review", requisitionId] as const,
};

export interface ApprovePayload {
  salesPricePerKgAed: number;
  notes?: string;
}

export interface RejectPayload {
  notes: string;
}

export function useMdReview(requisitionId: number) {
  return useQuery({
    queryKey: approvalKeys.review(requisitionId),
    queryFn: () =>
      api.get<MdReviewDetail>(`/approvals/${requisitionId}`).then((r) => r.data),
    enabled: Number.isFinite(requisitionId) && requisitionId > 0,
    retry: false,
  });
}

export function useApproveRequisition() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({
      requisitionId,
      payload,
    }: {
      requisitionId: number;
      payload: ApprovePayload;
    }) =>
      api
        .post<{ message: string; refNo: string }>(
          `/approvals/${requisitionId}/approve`,
          payload,
        )
        .then((r) => r.data),
    onSuccess: (_d, { requisitionId }) => {
      qc.invalidateQueries({ queryKey: ["requisitions"] });
      qc.invalidateQueries({ queryKey: approvalKeys.review(requisitionId) });
    },
  });
}

export function useRejectRequisition() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({
      requisitionId,
      payload,
    }: {
      requisitionId: number;
      payload: RejectPayload;
    }) =>
      api
        .post<{ message: string }>(`/approvals/${requisitionId}/reject`, payload)
        .then((r) => r.data),
    onSuccess: (_d, { requisitionId }) => {
      qc.invalidateQueries({ queryKey: ["requisitions"] });
      qc.invalidateQueries({ queryKey: approvalKeys.review(requisitionId) });
    },
  });
}
