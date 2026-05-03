import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/api/axios";
import type { RequisitionStatus } from "@/types/api";

export type AdminActionType =
  | "DeleteRequisition"
  | "RollbackStatus"
  | "ReassignSp"
  | "UnlockBom"
  | "UnlockCosting"
  | "ResetPassword"
  | "OverridePrices"
  | "HardDeleteCustomer"
  // V3 additions:
  | "RollbackToCosting"
  | "V3CutoverMigration"
  | "UpdateCompanySettings";

export type AuditEntityType = "Requisition" | "User" | "Customer";

export interface AuditLogItem {
  id: number;
  adminUserId: number;
  adminUserName: string;
  actionType: AdminActionType;
  entityType: AuditEntityType;
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
  entityType?: AuditEntityType;
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

// ---------- V2.3-C P2 ----------

export interface CurrentApprovalItem {
  requisitionItemId: number;
  itemDescription: string;
  expectedQty: number;
  salesPricePerKgAed: number;
  salesPricePerKgForeign: number | null;
  profitMarginPct: number;
  materialCostPct: number;
  otherCostPct: number;
}

export interface CurrentApproval {
  id: number;
  quotationRequestId: number;
  refNo: string;
  currencyCode: string;
  rateSnapshot: number | null;
  approvedAt: string;
  approvedByUserId: number;
  notes: string | null;
  items: CurrentApprovalItem[];
}

export function useCurrentApproval(reqId: number, enabled = true) {
  return useQuery({
    queryKey: ["admin-current-approval", reqId],
    queryFn: async () => {
      const { data } = await api.get<CurrentApproval>(
        `/admin/requisitions/${reqId}/current-approval`,
      );
      return data;
    },
    enabled: enabled && Number.isFinite(reqId) && reqId > 0,
  });
}

export interface OverridePricesItemPayload {
  requisitionItemId: number;
  salesPricePerKgAed: number;
  salesPricePerKgForeign?: number | null;
  profitMarginPct: number;
  materialCostPct: number;
  otherCostPct: number;
}

export interface OverridePricesPayload {
  reason: string;
  items: OverridePricesItemPayload[];
}

export interface OverridePricesResponse {
  newApprovalId: number;
  supersededApprovalId: number;
  emailSentToSpUserId?: number | null;
}

export function useOverridePrices(reqId: number) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (payload: OverridePricesPayload) => {
      const { data } = await api.post<OverridePricesResponse>(
        `/admin/requisitions/${reqId}/override-prices`,
        payload,
      );
      return data;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["requisitions"] });
      qc.invalidateQueries({ queryKey: ["requisition", reqId] });
      qc.invalidateQueries({ queryKey: ["admin-audit-log"] });
    },
  });
}

export interface HardDeleteCustomerBlocked {
  error: string;
  blockingRequisitions: number[];
}

export function useHardDeleteCustomer() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, reason }: { id: number; reason: string }) => {
      await api.delete(`/admin/customers/${id}`, { data: { reason } });
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["customers"] });
      qc.invalidateQueries({ queryKey: ["admin-audit-log"] });
    },
  });
}
