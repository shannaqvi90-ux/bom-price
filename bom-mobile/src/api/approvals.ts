import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "./client";
import { requisitionKeys } from "./requisitions";
import type { MdReviewDetail } from "@/types/api";

export const approvalKeys = {
  review: (requisitionId: number) => ["approval", "review", requisitionId] as const,
};

export interface ApproveItemPayload {
  requisitionItemId: number;
  salesPricePerKgAed: number;
}

export interface ApprovePayload {
  items: ApproveItemPayload[];
  notes?: string;
}

export interface RejectPayload {
  notes: string;
}

export function useMdReview(requisitionId: number) {
  return useQuery({
    queryKey: approvalKeys.review(requisitionId),
    queryFn: async () => {
      const res = await api.get<MdReviewDetail>(`/api/approvals/${requisitionId}`);
      return res.data;
    },
    enabled: Number.isFinite(requisitionId) && requisitionId > 0,
    retry: false,
  });
}

export function useApproveRequisition() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({
      requisitionId,
      payload,
    }: {
      requisitionId: number;
      payload: ApprovePayload;
    }) => {
      await api.post(`/api/approvals/${requisitionId}/approve`, payload);
    },
    onSuccess: (_data, { requisitionId }) => {
      qc.invalidateQueries({ queryKey: requisitionKeys.list() });
      qc.invalidateQueries({ queryKey: requisitionKeys.detail(requisitionId) });
      qc.invalidateQueries({ queryKey: approvalKeys.review(requisitionId) });
    },
  });
}

export function useRejectRequisition() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({
      requisitionId,
      payload,
    }: {
      requisitionId: number;
      payload: RejectPayload;
    }) => {
      await api.post(`/api/approvals/${requisitionId}/reject`, payload);
    },
    onSuccess: (_data, { requisitionId }) => {
      qc.invalidateQueries({ queryKey: requisitionKeys.list() });
      qc.invalidateQueries({ queryKey: requisitionKeys.detail(requisitionId) });
      qc.invalidateQueries({ queryKey: approvalKeys.review(requisitionId) });
    },
  });
}
