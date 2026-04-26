import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/api/axios";

export interface SalesGroup {
  id: number;
  name: string;
  isActive: boolean;
}

export const groupKeys = {
  all: ["groups"] as const,
  list: () => [...groupKeys.all, "list"] as const,
};

export function useGroups() {
  return useQuery({
    queryKey: groupKeys.list(),
    queryFn: async () => (await api.get<SalesGroup[]>("/groups")).data,
    staleTime: 5 * 60_000,
  });
}

export function useCreateGroup() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (req: { name: string }) =>
      (await api.post<SalesGroup>("/groups", req)).data,
    onSuccess: () => qc.invalidateQueries({ queryKey: groupKeys.list() }),
  });
}

export function useUpdateGroup() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, ...req }: { id: number; name: string; isActive: boolean }) =>
      api.put(`/groups/${id}`, req),
    onSuccess: () => qc.invalidateQueries({ queryKey: groupKeys.list() }),
  });
}

export function useDeleteGroup() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (id: number) => api.delete(`/groups/${id}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: groupKeys.list() }),
  });
}
