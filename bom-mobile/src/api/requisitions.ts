import { useQuery, useMutation, useQueryClient, type UseQueryOptions } from "@tanstack/react-query";
import { api } from "./client";
import type {
  ChangeCustomerRequest,
  CreateRequisitionRequest,
  CustomerChangeHistoryEntry,
  RequisitionDetail,
  RequisitionListItem,
} from "@/types/api";

const keys = {
  all: ["requisitions"] as const,
  list: () => [...keys.all, "list"] as const,
  detail: (id: number) => [...keys.all, "detail", id] as const,
};

async function fetchList(): Promise<RequisitionListItem[]> {
  const res = await api.get<RequisitionListItem[]>("/api/requisitions");
  return res.data;
}

async function fetchDetail(id: number): Promise<RequisitionDetail> {
  const res = await api.get<RequisitionDetail>(`/api/requisitions/${id}`);
  return res.data;
}

export function useRequisitionsList(options?: Partial<UseQueryOptions<RequisitionListItem[]>>) {
  return useQuery({
    queryKey: keys.list(),
    queryFn: fetchList,
    ...options,
  });
}

export function useRequisitionDetail(id: number, options?: Partial<UseQueryOptions<RequisitionDetail>>) {
  return useQuery({
    queryKey: keys.detail(id),
    queryFn: () => fetchDetail(id),
    enabled: Number.isFinite(id) && id > 0,
    ...options,
  });
}

export function useCreateRequisition() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (input: CreateRequisitionRequest): Promise<RequisitionDetail> => {
      const res = await api.post<RequisitionDetail>("/api/requisitions", input);
      return res.data;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: keys.list() });
    },
  });
}

export const requisitionKeys = keys;

export function useChangeCustomer(requisitionId: number) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: ChangeCustomerRequest) =>
      api.patch(`/api/requisitions/${requisitionId}/customer`, body).then((r) => r.data),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: keys.detail(requisitionId) });
      qc.invalidateQueries({ queryKey: ["requisition", requisitionId, "customerHistory"] });
      qc.invalidateQueries({ queryKey: keys.list() });
    },
  });
}

export function useCustomerChangeHistory(requisitionId: number, enabled = true) {
  return useQuery({
    queryKey: ["requisition", requisitionId, "customerHistory"],
    queryFn: async () => {
      const res = await api.get<CustomerChangeHistoryEntry[]>(
        `/api/requisitions/${requisitionId}/customer-history`,
      );
      return res.data;
    },
    enabled: enabled && requisitionId > 0,
    staleTime: 30_000,
  });
}

export interface ChangeBranchPayload {
  branchId: number;
  reason?: string;
}

export interface BranchChangeHistoryEntry {
  id: number;
  oldBranchId: number;
  oldBranchName: string;
  newBranchId: number;
  newBranchName: string;
  changedByUserId: number;
  changedByUserName: string;
  changedAt: string;
  reason: string | null;
}

export function useChangeBranch(requisitionId: number) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (payload: ChangeBranchPayload) => {
      await api.patch(`/api/requisitions/${requisitionId}/branch`, payload);
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: keys.detail(requisitionId) });
      qc.invalidateQueries({ queryKey: keys.list() });
      qc.invalidateQueries({ queryKey: ["requisition", requisitionId, "branchHistory"] });
    },
  });
}

export function useBranchChangeHistory(requisitionId: number, enabled = true) {
  return useQuery({
    queryKey: ["requisition", requisitionId, "branchHistory"],
    queryFn: async () => {
      const res = await api.get<BranchChangeHistoryEntry[]>(
        `/api/requisitions/${requisitionId}/branch-history`,
      );
      return res.data;
    },
    enabled: enabled && requisitionId > 0,
    staleTime: 30_000,
  });
}
