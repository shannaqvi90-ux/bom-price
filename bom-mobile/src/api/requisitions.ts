import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { api } from "./client";
import type { V3Requisition, V3RequisitionListItem } from "../types/v3";

export const requisitionKeys = {
  all: ["requisitions"] as const,
  lists: () => [...requisitionKeys.all, "list"] as const,
  list: (params?: Record<string, unknown>) => [...requisitionKeys.lists(), params] as const,
  details: () => [...requisitionKeys.all, "detail"] as const,
  detail: (id: number) => [...requisitionKeys.details(), id] as const,
};

export interface CreateReqPayload {
  customerId: number;
  quotationCurrency: string;
  referenceNumber?: string;
  notes?: string;
  finishedGoods: {
    itemId: number;
    expectedQtyKg: number;
    printing: boolean;
    bomLines: { processId: number; itemId: number; qtyPerKg: number; micron?: string }[];
  }[];
}

export function useRequisitions(statuses?: string[]) {
  const params = statuses?.length ? { status: statuses.join(",") } : undefined;
  return useQuery({
    queryKey: requisitionKeys.list(params),
    queryFn: () =>
      api.get<V3RequisitionListItem[]>("/api/requisitions", { params }).then((r) => r.data),
  });
}

export function useRequisition(id: number) {
  return useQuery({
    queryKey: requisitionKeys.detail(id),
    queryFn: () => api.get<V3Requisition>(`/api/requisitions/${id}`).then((r) => r.data),
    enabled: Number.isFinite(id) && id > 0,
  });
}

export function useCreateRequisition() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (payload: CreateReqPayload) =>
      api.post<{ id: number }>("/api/requisitions", payload).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: requisitionKeys.lists() }),
  });
}

export function useUpdateRequisition() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, payload }: { id: number; payload: CreateReqPayload }) =>
      api.put(`/api/requisitions/${id}`, payload),
    onSuccess: (_d, vars) => {
      qc.invalidateQueries({ queryKey: requisitionKeys.detail(vars.id) });
      qc.invalidateQueries({ queryKey: requisitionKeys.lists() });
    },
  });
}

export function useSubmitToCosting() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => api.post(`/api/requisitions/${id}/submit`),
    onSuccess: (_d, id) => {
      qc.invalidateQueries({ queryKey: requisitionKeys.detail(id) });
      qc.invalidateQueries({ queryKey: requisitionKeys.lists() });
    },
  });
}
