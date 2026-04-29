import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/api/axios";
import type {
  MdReviewDetail,
  V3Approval,
  V3FinalSignPayload,
  V3SetMarginPayload,
} from "@/types/api";

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

// ---------------------------------------------------------------------------
// V3 hooks — added alongside V2.3 hooks during the transition.
// V3 has a 4-stage approval (Costing → MdPricing → CustomerConfirm → MdFinalSign
// → Signed). Sales acts on the customer's behalf for the CustomerConfirm step.
// ---------------------------------------------------------------------------

export const v3ApprovalKeys = {
  current: (requisitionId: number) =>
    ["approval", "current", requisitionId] as const,
};

// MD Stage 1: set margin + transition Costing → MdPricing → CustomerConfirm.
export function useSetMargin() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({
      requisitionId,
      payload,
    }: {
      requisitionId: number;
      payload: V3SetMarginPayload;
    }) =>
      api
        .post<{ id: number; status: string; approvalId: number }>(
          `/approvals/${requisitionId}/set-margin`,
          payload,
        )
        .then((r) => r.data),
    onSuccess: (_d, { requisitionId }) => {
      qc.invalidateQueries({ queryKey: ["requisitions"] });
      qc.invalidateQueries({ queryKey: ["requisition", requisitionId] });
      qc.invalidateQueries({ queryKey: v3ApprovalKeys.current(requisitionId) });
    },
  });
}

// Sales (on the customer's behalf): customer accepts the price → CustomerConfirm
// → MdFinalSign.
export function useAcceptCustomer() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({
      requisitionId,
      customerFeedback,
    }: {
      requisitionId: number;
      customerFeedback?: string;
    }) =>
      api
        .post<{ id: number; status: string }>(
          `/approvals/${requisitionId}/accept-customer`,
          { customerFeedback },
        )
        .then((r) => r.data),
    onSuccess: (_d, { requisitionId }) => {
      qc.invalidateQueries({ queryKey: ["requisitions"] });
      qc.invalidateQueries({ queryKey: ["requisition", requisitionId] });
    },
  });
}

// Sales (on the customer's behalf): customer rejects the price → CustomerConfirm
// → MdPricing (re-margin).
export function useRejectCustomer() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({
      requisitionId,
      reason,
    }: {
      requisitionId: number;
      reason: string;
    }) =>
      api
        .post<{ id: number; status: string }>(
          `/approvals/${requisitionId}/reject-customer`,
          { reason },
        )
        .then((r) => r.data),
    onSuccess: (_d, { requisitionId }) => {
      qc.invalidateQueries({ queryKey: ["requisitions"] });
      qc.invalidateQueries({ queryKey: ["requisition", requisitionId] });
    },
  });
}

// MD Stage 2: type-to-confirm SIGN → MdFinalSign → Signed (locks the quotation
// + generates PDF).
export function useFinalSign() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({
      requisitionId,
      payload,
    }: {
      requisitionId: number;
      payload: V3FinalSignPayload;
    }) =>
      api
        .post<{
          id: number;
          status: string;
          approvalId: number;
          pdfDownloadUrl: string;
        }>(`/approvals/${requisitionId}/final-sign`, payload)
        .then((r) => r.data),
    onSuccess: (_d, { requisitionId }) => {
      qc.invalidateQueries({ queryKey: ["requisitions"] });
      qc.invalidateQueries({ queryKey: ["requisition", requisitionId] });
      qc.invalidateQueries({ queryKey: v3ApprovalKeys.current(requisitionId) });
    },
  });
}

// V3 — read current (non-superseded) approval for pricing pages.
// Backend: GET /api/approvals/{requisitionId}/current — returns V3Approval shape.
// Note: a same-named legacy admin hook exists at @/api/admin (different endpoint,
// different shape). Disambiguate by import path.
export function useCurrentApproval(requisitionId: number) {
  return useQuery({
    queryKey: v3ApprovalKeys.current(requisitionId),
    queryFn: () =>
      api
        .get<V3Approval>(`/approvals/${requisitionId}/current`)
        .then((r) => r.data),
    enabled: Number.isFinite(requisitionId) && requisitionId > 0,
    retry: false,
  });
}
