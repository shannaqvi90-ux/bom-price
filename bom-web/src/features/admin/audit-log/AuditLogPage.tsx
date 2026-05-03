import React, { useMemo, useState } from "react";
import { Link } from "react-router-dom";
import { Button } from "@/components/ui/Button";
import { Card, CardContent } from "@/components/ui/Card";
import { useAuditLog, type AdminActionType, type AuditEntityType } from "@/api/admin";
import { useUsers } from "@/features/users/usersApi";
import { DiffPanel } from "./DiffPanel";

const ACTION_TYPE_LABELS: Record<AdminActionType, string> = {
  DeleteRequisition: "Delete Requisition",
  RollbackStatus: "Rollback Status",
  ReassignSp: "Reassign SP",
  UnlockBom: "Unlock BOM",
  UnlockCosting: "Unlock Costing",
  ResetPassword: "Reset Password",
  OverridePrices: "Override Prices",
  HardDeleteCustomer: "Delete Customer",
  // V3:
  RollbackToCosting: "Rollback to Costing",
  V3CutoverMigration: "V3 Cutover Migration",
  UpdateCompanySettings: "Update Company Settings",
};

const ACTION_TYPES: AdminActionType[] = [
  "DeleteRequisition",
  "RollbackStatus",
  "ReassignSp",
  "UnlockBom",
  "UnlockCosting",
  "ResetPassword",
  "OverridePrices",
  "HardDeleteCustomer",
  // V3:
  "RollbackToCosting",
  "V3CutoverMigration",
  "UpdateCompanySettings",
];

const ENTITY_TYPES: AuditEntityType[] = ["Requisition", "User", "Customer"];

function formatDate(iso: string) {
  return new Date(iso).toLocaleString();
}

export function AuditLogPage() {
  const [page, setPage] = useState(1);
  const [actionType, setActionType] = useState<AdminActionType | "">("");
  const [entityType, setEntityType] = useState<AuditEntityType | "">("");
  const [adminUserId, setAdminUserId] = useState<number | "">("");
  const [entityIdInput, setEntityIdInput] = useState("");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [expandedId, setExpandedId] = useState<number | null>(null);

  const pageSize = 20;

  const { data: users } = useUsers();
  const adminUsers = useMemo(
    () =>
      (users ?? [])
        .filter((u) => u.role === "Admin" && u.isActive)
        .sort((a, b) => a.name.localeCompare(b.name)),
    [users],
  );

  const parsedEntityId = entityIdInput.trim() === "" ? undefined : Number(entityIdInput);
  const entityIdFilter =
    parsedEntityId !== undefined && Number.isFinite(parsedEntityId) && parsedEntityId > 0
      ? parsedEntityId
      : undefined;

  const filters = {
    page,
    pageSize,
    ...(actionType ? { actionType } : {}),
    ...(entityType ? { entityType } : {}),
    ...(adminUserId !== "" ? { adminUserId } : {}),
    ...(entityIdFilter !== undefined ? { entityId: entityIdFilter } : {}),
    ...(from ? { from } : {}),
    ...(to ? { to } : {}),
  };

  const { data, isLoading } = useAuditLog(filters);

  const items = data?.items ?? [];
  const total = data?.total ?? 0;
  const totalPages = Math.max(1, Math.ceil(total / pageSize));

  function handleFilterChange() {
    setPage(1);
    setExpandedId(null);
  }

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold tracking-tight">Audit Log</h1>
      </div>

      {/* Filters */}
      <Card>
        <CardContent className="flex flex-wrap gap-3 py-3">
          <select
            className="rounded-md border border-input bg-background px-3 py-1.5 text-sm"
            value={actionType}
            onChange={(e) => {
              setActionType(e.target.value as AdminActionType | "");
              handleFilterChange();
            }}
            aria-label="Filter by action type"
          >
            <option value="">All actions</option>
            {ACTION_TYPES.map((a) => (
              <option key={a} value={a}>{ACTION_TYPE_LABELS[a]}</option>
            ))}
          </select>

          <select
            className="rounded-md border border-input bg-background px-3 py-1.5 text-sm"
            value={entityType}
            onChange={(e) => {
              setEntityType(e.target.value as AuditEntityType | "");
              handleFilterChange();
            }}
            aria-label="Filter by entity type"
          >
            <option value="">All entities</option>
            {ENTITY_TYPES.map((t) => (
              <option key={t} value={t}>{t}</option>
            ))}
          </select>

          <select
            className="rounded-md border border-input bg-background px-3 py-1.5 text-sm"
            value={adminUserId === "" ? "" : String(adminUserId)}
            onChange={(e) => {
              const v = e.target.value;
              setAdminUserId(v === "" ? "" : Number(v));
              handleFilterChange();
            }}
            aria-label="Filter by admin user"
          >
            <option value="">All admins</option>
            {adminUsers.map((u) => (
              <option key={u.id} value={u.id}>{u.name}</option>
            ))}
          </select>

          <div className="flex items-center gap-1.5">
            <label className="text-sm text-muted-foreground">Entity ID</label>
            <input
              type="number"
              min={1}
              inputMode="numeric"
              className="w-24 rounded-md border border-input bg-background px-2 py-1.5 text-sm"
              value={entityIdInput}
              onChange={(e) => { setEntityIdInput(e.target.value); handleFilterChange(); }}
              aria-label="Filter by entity ID"
              placeholder="e.g. 42"
            />
          </div>

          <div className="flex items-center gap-1.5">
            <label className="text-sm text-muted-foreground">From</label>
            <input
              type="date"
              className="rounded-md border border-input bg-background px-2 py-1.5 text-sm"
              value={from}
              onChange={(e) => { setFrom(e.target.value); handleFilterChange(); }}
            />
          </div>

          <div className="flex items-center gap-1.5">
            <label className="text-sm text-muted-foreground">To</label>
            <input
              type="date"
              className="rounded-md border border-input bg-background px-2 py-1.5 text-sm"
              value={to}
              onChange={(e) => { setTo(e.target.value); handleFilterChange(); }}
            />
          </div>
        </CardContent>
      </Card>

      {/* Table */}
      <Card>
        <CardContent className="p-0">
          {isLoading ? (
            <div className="p-6 text-sm text-muted-foreground">Loading…</div>
          ) : items.length === 0 ? (
            <div className="p-6 text-sm text-muted-foreground">No audit log entries found.</div>
          ) : (
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-border bg-muted/50">
                  <th className="px-4 py-2.5 text-left font-medium">Timestamp</th>
                  <th className="px-4 py-2.5 text-left font-medium">Performed By</th>
                  <th className="px-4 py-2.5 text-left font-medium">Action</th>
                  <th className="px-4 py-2.5 text-left font-medium">Entity</th>
                  <th className="px-4 py-2.5 text-left font-medium">Reason</th>
                  <th className="px-4 py-2.5 text-left font-medium"></th>
                </tr>
              </thead>
              <tbody>
                {items.map((item) => (
                  <React.Fragment key={item.id}>
                    <tr className="border-b border-border hover:bg-muted/30">
                      <td className="px-4 py-2.5 whitespace-nowrap text-muted-foreground">
                        {formatDate(item.createdAt)}
                      </td>
                      <td className="px-4 py-2.5">{item.adminUserName}</td>
                      <td className="px-4 py-2.5">
                        <span className="inline-flex items-center rounded-full bg-muted px-2 py-0.5 text-xs font-medium">
                          {item.actionType}
                        </span>
                      </td>
                      <td className="px-4 py-2.5">
                        {item.entityType === "Requisition" ? (
                          <Link
                            to={`/requisitions/${item.entityId}`}
                            className="text-primary hover:underline"
                          >
                            Requisition #{item.entityId}
                          </Link>
                        ) : (
                          <span>{item.entityType} #{item.entityId}</span>
                        )}
                      </td>
                      <td className="px-4 py-2.5 max-w-xs truncate" title={item.reason}>
                        {item.reason}
                      </td>
                      <td className="px-4 py-2.5">
                        <Button
                          variant="ghost"
                          size="sm"
                          aria-label="Show diff"
                          onClick={() =>
                            setExpandedId(expandedId === item.id ? null : item.id)
                          }
                        >
                          Diff
                        </Button>
                      </td>
                    </tr>
                    {expandedId === item.id && (
                      <tr className="border-b border-border bg-muted/20">
                        <td colSpan={6} className="px-4 py-3">
                          <DiffPanel before={item.beforeJson} after={item.afterJson} />
                        </td>
                      </tr>
                    )}
                  </React.Fragment>
                ))}
              </tbody>
            </table>
          )}
        </CardContent>
      </Card>

      {/* Pagination */}
      <div className="flex items-center justify-between text-sm">
        <span className="text-muted-foreground">
          {total} {total === 1 ? "entry" : "entries"}
        </span>
        <div className="flex items-center gap-2">
          <Button
            variant="ghost"
            size="sm"
            aria-label="Prev"
            disabled={page <= 1}
            onClick={() => setPage((p) => p - 1)}
          >
            Prev
          </Button>
          <span className="text-muted-foreground">Page {page} of {totalPages}</span>
          <Button
            variant="ghost"
            size="sm"
            aria-label="Next"
            disabled={page >= totalPages}
            onClick={() => setPage((p) => p + 1)}
          >
            Next
          </Button>
        </div>
      </div>
    </div>
  );
}
