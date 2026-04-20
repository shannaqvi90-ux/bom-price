import { useQuery, useMutation, useQueryClient, type UseQueryOptions } from "@tanstack/react-query";
import { api } from "./client";
import type {
  CreateRequisitionRequest,
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
