import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/api/axios";
import type { RequisitionStatus } from "@/types/api";

export type AdminActionType =
  | "DeleteRequisition"
  | "RollbackStatus"
  | "ReassignSp"
  | "UnlockBom"
  | "UnlockCosting"
  | "ResetPassword";

export interface AuditLogItem {
  id: number;
  adminUserId: number;
  adminUserName: string;
  actionType: AdminActionType;
  entityType: "Requisition" | "User";
  entityId: number;
  reason: string;
  beforeJson: string;
  afterJson?: string | null;
  createdAt: string;
}

export interface AuditLogPagedResponse {
  items: AuditLogItem[];
  total: number;
  page: number;
  pageSize: number;
}

export interface AuditLogFilters {
  page?: number;
  pageSize?: number;
  actionType?: AdminActionType;
  adminUserId?: number;
  entityType?: "Requisition" | "User";
  entityId?: number;
  from?: string;
  to?: string;
}

export function useAuditLog(filters: AuditLogFilters) {
  return useQuery({
    queryKey: ["admin-audit-log", filters],
    queryFn: async () => {
      const { data } = await api.get<AuditLogPagedResponse>("/admin/audit-log", { params: filters });
      return data;
    },
  });
}

function invalidateReqAndAudit(qc: ReturnType<typeof useQueryClient>, reqId?: number) {
  qc.invalidateQueries({ queryKey: ["requisitions"] });
  if (reqId) qc.invalidateQueries({ queryKey: ["requisition", reqId] });
  qc.invalidateQueries({ queryKey: ["admin-audit-log"] });
}

export function useDeleteRequisition() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, reason }: { id: number; reason: string }) => {
      await api.delete(`/admin/requisitions/${id}`, { data: { reason } });
    },
    onSuccess: (_, vars) => invalidateReqAndAudit(qc, vars.id),
  });
}

export function useRollbackStatus() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, targetStatus, reason }: { id: number; targetStatus: RequisitionStatus; reason: string }) => {
      const { data } = await api.post(`/admin/requisitions/${id}/rollback-status`, { targetStatus, reason });
      return data;
    },
    onSuccess: (_, vars) => invalidateReqAndAudit(qc, vars.id),
  });
}

export function useReassignSp() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, newSalesPersonId, reason }: { id: number; newSalesPersonId: number; reason: string }) => {
      const { data } = await api.post(`/admin/requisitions/${id}/reassign-sp`, { newSalesPersonId, reason });
      return data;
    },
    onSuccess: (_, vars) => invalidateReqAndAudit(qc, vars.id),
  });
}

export function useUnlockBom() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, reason }: { id: number; reason: string }) => {
      const { data } = await api.post(`/admin/requisitions/${id}/unlock-bom`, { reason });
      return data;
    },
    onSuccess: (_, vars) => invalidateReqAndAudit(qc, vars.id),
  });
}

export function useUnlockCosting() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, reason }: { id: number; reason: string }) => {
      const { data } = await api.post(`/admin/requisitions/${id}/unlock-costing`, { reason });
      return data;
    },
    onSuccess: (_, vars) => invalidateReqAndAudit(qc, vars.id),
  });
}

export function useResetPassword() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, reason }: { id: number; reason: string }) => {
      const { data } = await api.post<{ tempPassword: string }>(`/admin/users/${id}/reset-password`, { reason });
      return data;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["users"] });
      qc.invalidateQueries({ queryKey: ["admin-audit-log"] });
    },
  });
}
