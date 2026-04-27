import { useState } from "react";
import { useAuthStore } from "@/store/authStore";
import { Button } from "@/components/ui/Button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/Card";
import {
  canRollback,
  canUnlockBom,
  canUnlockCosting,
  canDelete,
  canReassignSp,
  canOverridePrices,
} from "./adminOverrideAuthorization";
import { DeleteRequisitionModal } from "./modals/DeleteRequisitionModal";
import { RollbackStatusModal } from "./modals/RollbackStatusModal";
import { ReassignSpModal } from "./modals/ReassignSpModal";
import { UnlockBomModal } from "./modals/UnlockBomModal";
import { UnlockCostingModal } from "./modals/UnlockCostingModal";
import { OverridePricesModal } from "./modals/OverridePricesModal";
import type { RequisitionStatus } from "@/types/api";

interface Requisition {
  id: number;
  refNo: string;
  status: RequisitionStatus;
}

interface Props {
  requisition: Requisition;
}

type ModalKey =
  | "delete"
  | "rollback"
  | "reassignSp"
  | "unlockBom"
  | "unlockCosting"
  | "overridePrices"
  | null;

export function AdminActionsCard({ requisition }: Props) {
  const role = useAuthStore((s) => s.user?.role);
  const [expanded, setExpanded] = useState(false);
  const [activeModal, setActiveModal] = useState<ModalKey>(null);

  if (role !== "Admin") return null;

  const { status } = requisition;

  return (
    <>
      <Card className="border-amber-200 bg-amber-50">
        <CardHeader className="pb-2">
          <CardTitle className="flex items-center justify-between text-base text-amber-800">
            <button
              type="button"
              onClick={() => setExpanded((v) => !v)}
              className="flex items-center gap-2 font-semibold hover:opacity-80"
              aria-label="Admin actions"
            >
              Admin actions
              <span className="text-xs font-normal text-amber-600">
                {expanded ? "▲" : "▼"}
              </span>
            </button>
          </CardTitle>
        </CardHeader>

        {expanded && (
          <CardContent className="flex flex-wrap gap-2 pt-0">
            {canDelete() && (
              <Button
                variant="destructive"
                size="sm"
                onClick={() => setActiveModal("delete")}
              >
                Delete Requisition
              </Button>
            )}

            {canRollback(status) && (
              <Button
                variant="outline"
                size="sm"
                onClick={() => setActiveModal("rollback")}
              >
                Rollback Status
              </Button>
            )}

            {canReassignSp() && (
              <Button
                variant="outline"
                size="sm"
                onClick={() => setActiveModal("reassignSp")}
              >
                Reassign SP
              </Button>
            )}

            {canUnlockBom(status) && (
              <Button
                variant="outline"
                size="sm"
                onClick={() => setActiveModal("unlockBom")}
              >
                Unlock BOM
              </Button>
            )}

            {canUnlockCosting(status) && (
              <Button
                variant="outline"
                size="sm"
                onClick={() => setActiveModal("unlockCosting")}
              >
                Unlock Costing
              </Button>
            )}

            {canOverridePrices(status) && (
              <Button
                variant="outline"
                size="sm"
                onClick={() => setActiveModal("overridePrices")}
              >
                Override Prices
              </Button>
            )}
          </CardContent>
        )}
      </Card>

      {activeModal === "delete" && (
        <DeleteRequisitionModal
          requisition={requisition}
          onClose={() => setActiveModal(null)}
        />
      )}
      {activeModal === "rollback" && (
        <RollbackStatusModal
          requisition={requisition}
          onClose={() => setActiveModal(null)}
        />
      )}
      {activeModal === "reassignSp" && (
        <ReassignSpModal
          requisition={requisition}
          onClose={() => setActiveModal(null)}
        />
      )}
      {activeModal === "unlockBom" && (
        <UnlockBomModal
          requisition={requisition}
          onClose={() => setActiveModal(null)}
        />
      )}
      {activeModal === "unlockCosting" && (
        <UnlockCostingModal
          requisition={requisition}
          onClose={() => setActiveModal(null)}
        />
      )}
      {activeModal === "overridePrices" && (
        <OverridePricesModal
          requisition={requisition}
          onClose={() => setActiveModal(null)}
        />
      )}
    </>
  );
}
