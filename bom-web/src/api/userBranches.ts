import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/api/axios";

export function useUserBranches(userId: number, enabled = true) {
  return useQuery({
    queryKey: ["users", userId, "branches"],
    queryFn: async () => (await api.get<number[]>(`/users/${userId}/branches`)).data,
    enabled: enabled && userId > 0,
  });
}

export function useSetUserBranches(userId: number) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (branchIds: number[]) =>
      api.put(`/users/${userId}/branches`, { branchIds }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["users", userId, "branches"] });
      qc.invalidateQueries({ queryKey: ["users"] });
    },
  });
}
