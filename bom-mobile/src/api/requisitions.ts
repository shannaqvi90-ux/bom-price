import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { useMemo } from "react";
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

export function useRequisitions(opts?: { statuses?: string[]; from?: string }) {
  const params = useMemo(() => {
    const p = new URLSearchParams();
    (opts?.statuses ?? []).forEach((s) => p.append("status", s));
    if (opts?.from) p.append("from", opts.from);
    return p.toString();
  }, [opts?.statuses, opts?.from]);
  return useQuery({
    queryKey: requisitionKeys.list({ statuses: opts?.statuses, from: opts?.from }),
    queryFn: () =>
      api.get<V3RequisitionListItem[]>(`/api/requisitions${params ? `?${params}` : ""}`)
        .then((r) => r.data),
    staleTime: 10_000,
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

export function useChangeCustomer(requisitionId: number) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (payload: { customerId: number; reason?: string }) => {
      await api.patch(`/api/requisitions/${requisitionId}/customer`, payload);
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: requisitionKeys.detail(requisitionId) });
      qc.invalidateQueries({ queryKey: requisitionKeys.lists() });
    },
  });
}

export function useCustomerChangeHistory(requisitionId: number, enabled = true) {
  return useQuery({
    queryKey: [...requisitionKeys.detail(requisitionId), "customer-history"],
    queryFn: async () => {
      const res = await api.get<Array<{
        id: number;
        oldCustomerId: number;
        oldCustomerName: string;
        newCustomerId: number;
        newCustomerName: string;
        changedByUserId: number;
        changedByUserName: string;
        changedAt: string;
        reason?: string | null;
      }>>(`/api/requisitions/${requisitionId}/customer-history`);
      return res.data;
    },
    enabled: enabled && Number.isFinite(requisitionId) && requisitionId > 0,
    staleTime: 30_000,
  });
}
