import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/api/axios";

export interface Branch {
  id: number;
  name: string;
  isActive: boolean;
}

export const branchKeys = {
  all: ["branches"] as const,
  list: () => [...branchKeys.all, "list"] as const,
};

export function useBranches(options?: { enabled?: boolean }) {
  return useQuery({
    queryKey: branchKeys.list(),
    queryFn: async () => (await api.get<Branch[]>("/branches")).data,
    staleTime: 5 * 60_000,
    enabled: options?.enabled ?? true,
  });
}

export function useCreateBranch() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (req: { name: string }) =>
      (await api.post<Branch>("/branches", req)).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: branchKeys.list() }),
  });
}

export function useUpdateBranch() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, ...req }: { id: number; name: string; isActive: boolean }) =>
      api.put(`/branches/${id}`, req),
    onSuccess: () => qc.invalidateQueries({ queryKey: branchKeys.list() }),
  });
}

export function useDeleteBranch() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (id: number) => api.delete(`/branches/${id}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: branchKeys.list() }),
  });
}
