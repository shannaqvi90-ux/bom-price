import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/api/axios";
import type {
  AddRequisitionItemRequest,
  ChangeCustomerRequest,
  CreateRequisitionRequest,
  CustomerChangeHistoryEntry,
  RequisitionDetail,
  RequisitionListItem,
} from "@/types/api";

export const requisitionKeys = {
  all: ["requisitions"] as const,
  list: () => [...requisitionKeys.all, "list"] as const,
  detail: (id: number) => [...requisitionKeys.all, "detail", id] as const,
  customerHistory: (id: number) =>
    [...requisitionKeys.all, "customer-history", id] as const,
};

export function useRequisitions() {
  return useQuery({
    queryKey: requisitionKeys.list(),
    queryFn: () =>
      api.get<RequisitionListItem[]>("/requisitions").then((r) => r.data),
  });
}

export function useRequisition(id: number) {
  return useQuery({
    queryKey: requisitionKeys.detail(id),
    queryFn: () =>
      api.get<RequisitionDetail>(`/requisitions/${id}`).then((r) => r.data),
    enabled: Number.isFinite(id) && id > 0,
  });
}

interface CreateResponse {
  id: number;
  refNo: string;
}

export function useCreateRequisition() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateRequisitionRequest) =>
      api.post<CreateResponse>("/requisitions", body).then((r) => r.data),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: requisitionKeys.all });
    },
  });
}

export function useAddRequisitionItem() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({
      requisitionId,
      body,
    }: {
      requisitionId: number;
      body: AddRequisitionItemRequest;
    }) =>
      api
        .post<{ id: number }>(`/requisitions/${requisitionId}/items`, body)
        .then((r) => r.data),
    onSuccess: (_d, { requisitionId }) => {
      qc.invalidateQueries({ queryKey: requisitionKeys.detail(requisitionId) });
    },
  });
}

export function useRemoveRequisitionItem() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({
      requisitionId,
      requisitionItemId,
    }: {
      requisitionId: number;
      requisitionItemId: number;
    }) => api.delete(`/requisitions/${requisitionId}/items/${requisitionItemId}`),
    onSuccess: (_d, { requisitionId }) => {
      qc.invalidateQueries({ queryKey: requisitionKeys.detail(requisitionId) });
    },
  });
}

export interface ResubmitRequisitionRequest {
  items: { itemId: number; expectedQty: number }[];
}

export function useResubmitRequisition(id: number) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: ResubmitRequisitionRequest) =>
      api
        .post<{ id: number; refNo: string; status: string }>(
          `/requisitions/${id}/resubmit`,
          body,
        )
        .then((r) => r.data),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: requisitionKeys.detail(id) });
      qc.invalidateQueries({ queryKey: requisitionKeys.list() });
    },
  });
}

export function useChangeRequisitionCustomer(id: number) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: ChangeCustomerRequest) =>
      api.patch<void>(`/requisitions/${id}/customer`, body).then((r) => r.data),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: requisitionKeys.detail(id) });
      qc.invalidateQueries({ queryKey: requisitionKeys.list() });
      qc.invalidateQueries({ queryKey: requisitionKeys.customerHistory(id) });
    },
  });
}

export function useCustomerChangeHistory(id: number, enabled = true) {
  return useQuery({
    queryKey: requisitionKeys.customerHistory(id),
    queryFn: () =>
      api
        .get<CustomerChangeHistoryEntry[]>(`/requisitions/${id}/customer-history`)
        .then((r) => r.data),
    enabled: enabled && Number.isFinite(id) && id > 0,
  });
}
