import { useState } from "react";
import { Pressable, ScrollView, Text, View } from "react-native";
import type { V3Requisition } from "../../../types/v3";
import { useSaveV3CostData, useSubmitV3Costing } from "../../../api/costing";
import { useCostingDraftState } from "../state/useCostingDraftState";
import type { FgDraftState } from "../state/fgReadiness";
import { CustomerSwapSheet } from "../../../components/CustomerSwapSheet";
import { CustomerChangeHistorySheet } from "../../../components/CustomerChangeHistorySheet";
import { useCustomerChangeHistory } from "../../../api/requisitions";
import { FgCostingCard } from "./FgCostingCard";
import { SubmitAllFooter } from "./SubmitAllFooter";
import { CostInputDrawer } from "../drawer/CostInputDrawer";

interface Props {
  req: V3Requisition;
}

function buildSavePayload(drafts: FgDraftState[]) {
  return {
    finishedGoods: drafts.map((d) => ({
      requisitionItemId: d.requisitionItemId,
      rawMaterialCosts: d.rawMaterialCosts.map((rc) => ({
        bomLineId: rc.bomLineId,
        costPerKg: parseFloat(rc.costPerKg) || 0,
        currencyCode: rc.currencyCode || "AED",
      })),
      printingCostPerKg: d.hasPrinting ? (parseFloat(d.printingCostPerKg) || 0) : null,
      printingCostCurrency: d.hasPrinting ? (d.printingCostCurrency || "AED") : null,
      fohPerKg: parseFloat(d.fohPerKg) || 0,
      transportPerKg: parseFloat(d.transportPerKg) || 0,
      commissionPerKg: parseFloat(d.commissionPerKg) || 0,
    })),
  };
}

export function ActiveCostingView({ req }: Props) {
  const draftState = useCostingDraftState(req);
  const [openFgIdx, setOpenFgIdx] = useState<number | null>(null);
  const [drawerBaseline, setDrawerBaseline] = useState<FgDraftState | null>(null);
  const [swapOpen, setSwapOpen] = useState(false);
  const [historyOpen, setHistoryOpen] = useState(false);

  const saveMut = useSaveV3CostData(req.id);
  const submitMut = useSubmitV3Costing(req.id);

  const historyQ = useCustomerChangeHistory(req.id, true);
  const historyCount = historyQ.data?.length ?? 0;

  const openFor = (idx: number) => {
    setDrawerBaseline({ ...draftState.drafts[idx] });
    setOpenFgIdx(idx);
  };

  const closeDrawer = () => {
    setOpenFgIdx(null);
    setDrawerBaseline(null);
  };

  const saveDrawer = async () => {
    try {
      await saveMut.mutateAsync(buildSavePayload(draftState.drafts));
      closeDrawer();
    } catch (err) {
      const e = err as { response?: { data?: { error?: string } } };
      console.warn("Save failed:", e?.response?.data?.error ?? "Unknown error");
    }
  };

  const submitAll = async () => {
    try {
      await saveMut.mutateAsync(buildSavePayload(draftState.drafts));
      await submitMut.mutateAsync();
    } catch (err) {
      const e = err as { response?: { data?: { error?: string; missingRequisitionItemIds?: number[] } } };
      console.warn("Submit failed:", e?.response?.data?.error ?? "Unknown error");
    }
  };

  const dirtyDiff = openFgIdx != null && drawerBaseline != null
    ? draftState.isDirtyVsBaseline(openFgIdx, drawerBaseline) : 0;

  return (
    <View style={{ flex: 1 }}>
      <ScrollView style={{ flex: 1 }} contentContainerStyle={{ paddingTop: 8, paddingBottom: 8 }}>
        <View style={{ paddingHorizontal: 12, marginBottom: 8 }}>
          <Text style={{ fontSize: 13, color: "#64748b", fontWeight: "600", letterSpacing: 0.5 }}>CUSTOMER</Text>
          <Text style={{ fontSize: 16, fontWeight: "700", color: "#0f172a", marginTop: 4 }}>{req.customer.name}</Text>
          <View style={{ flexDirection: "row", gap: 8, marginTop: 8 }}>
            <Pressable
              onPress={() => setSwapOpen(true)}
              style={{
                paddingHorizontal: 12, paddingVertical: 8,
                borderRadius: 8, borderWidth: 1, borderColor: "#1e40af",
                backgroundColor: "#eff6ff",
              }}
            >
              <Text style={{ color: "#1e40af", fontWeight: "600", fontSize: 13 }}>Change customer</Text>
            </Pressable>
            {historyCount > 0 ? (
              <Pressable
                onPress={() => setHistoryOpen(true)}
                style={{
                  paddingHorizontal: 10, paddingVertical: 8,
                  borderRadius: 999, backgroundColor: "#fef3c7",
                }}
              >
                <Text style={{ color: "#92400e", fontSize: 12, fontWeight: "600" }}>
                  Customer changed ({historyCount})
                </Text>
              </Pressable>
            ) : null}
          </View>
        </View>

        {req.finishedGoods.map((fg, idx) => (
          <FgCostingCard
            key={fg.id}
            fgIdx={idx}
            fg={fg}
            readiness={draftState.readiness[idx]}
            onPress={() => openFor(idx)}
          />
        ))}
      </ScrollView>

      <SubmitAllFooter
        readyCount={draftState.readiness.filter((r) => r === "ready").length}
        totalCount={draftState.readiness.length}
        submitting={submitMut.isPending || saveMut.isPending}
        onSubmit={submitAll}
      />

      {openFgIdx != null && drawerBaseline != null ? (
        <CostInputDrawer
          visible={true}
          fgIdx={openFgIdx}
          req={req}
          draft={draftState.drafts[openFgIdx]}
          saving={saveMut.isPending}
          dirtyDiffCount={dirtyDiff}
          onClose={closeDrawer}
          onSave={saveDrawer}
          onChangeRm={(rmIdx, partial) => draftState.setRmCost(openFgIdx, rmIdx, partial)}
          onChangeFg={(partial) => draftState.setFg(openFgIdx, partial)}
        />
      ) : null}

      <CustomerSwapSheet
        requisitionId={req.id}
        currentCustomerId={req.customer.id}
        currentCustomerName={req.customer.name}
        open={swapOpen}
        onClose={() => setSwapOpen(false)}
      />
      <CustomerChangeHistorySheet
        requisitionId={req.id}
        open={historyOpen}
        onClose={() => setHistoryOpen(false)}
      />
    </View>
  );
}
