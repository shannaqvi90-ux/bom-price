import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/api/axios";

export interface UserGroupResponse {
  groupId: number | null;
  groupName: string | null;
}

export function useUserGroup(userId: number, enabled = true) {
  return useQuery({
    queryKey: ["users", userId, "group"],
    queryFn: async () => (await api.get<UserGroupResponse>(`/users/${userId}/group`)).data,
    enabled: enabled && userId > 0,
  });
}

export function useSetUserGroup(userId: number) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (groupId: number | null) => {
      await api.put(`/users/${userId}/group`, { groupId });
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["users", userId, "group"] });
      qc.invalidateQueries({ queryKey: ["users"] });
    },
  });
}
