import {
  Alert,
  KeyboardAvoidingView,
  Modal,
  Platform,
  Pressable,
  ScrollView,
  Text,
  View,
} from "react-native";
import * as Haptics from "expo-haptics";
import type { V3Requisition } from "../../../types/v3";
import type { FgDraftState, RawMaterialCostState } from "../state/fgReadiness";
import { fgReadiness } from "../state/fgReadiness";
import { RmCostRow } from "./RmCostRow";
import { PrintingCostSection } from "./PrintingCostSection";
import { OtherCostsSection } from "./OtherCostsSection";
import { DrawerFooter } from "./DrawerFooter";

interface Props {
  visible: boolean;
  fgIdx: number;
  req: V3Requisition;
  draft: FgDraftState;
  saving: boolean;
  onClose: () => void;
  onSave: () => void;
  onChangeRm: (rmIdx: number, partial: Partial<RawMaterialCostState>) => void;
  onChangeFg: (partial: Partial<FgDraftState>) => void;
  dirtyDiffCount: number;
}

export function CostInputDrawer({
  visible,
  fgIdx,
  req,
  draft,
  saving,
  onClose,
  onSave,
  onChangeRm,
  onChangeFg,
  dirtyDiffCount,
}: Props) {
  const fg = req.finishedGoods[fgIdx];
  const readiness = fgReadiness(draft);

  const handleClose = () => {
    if (dirtyDiffCount >= 3) {
      Alert.alert(
        "Discard changes?",
        `You changed ${dirtyDiffCount} fields. Discard them?`,
        [
          { text: "Keep editing", style: "cancel" },
          {
            text: "Discard",
            style: "destructive",
            onPress: () => {
              Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
              onClose();
            },
          },
        ],
      );
    } else {
      onClose();
    }
  };

  if (!fg) return null;

  return (
    <Modal
      visible={visible}
      animationType="slide"
      presentationStyle="pageSheet"
      onRequestClose={handleClose}
    >
      <KeyboardAvoidingView
        behavior={Platform.OS === "ios" ? "padding" : "height"}
        style={{ flex: 1, backgroundColor: "#f8fafc" }}
      >
        {/* Header */}
        <View
          style={{
            paddingTop: Platform.OS === "ios" ? 16 : 24,
            paddingHorizontal: 16,
            paddingBottom: 12,
            backgroundColor: "#ffffff",
            borderBottomWidth: 1,
            borderBottomColor: "#e2e8f0",
          }}
        >
          <View
            style={{
              flexDirection: "row",
              alignItems: "center",
              justifyContent: "space-between",
            }}
          >
            <View style={{ flex: 1 }}>
              <Text
                style={{
                  fontSize: 11,
                  color: "#64748b",
                  letterSpacing: 0.5,
                  fontWeight: "600",
                }}
              >
                COSTING · FG {fgIdx + 1} OF {req.finishedGoods.length}
              </Text>
              <Text
                style={{
                  fontSize: 18,
                  fontWeight: "700",
                  color: "#0f172a",
                  marginTop: 2,
                }}
                numberOfLines={1}
              >
                {fg.item.description}
              </Text>
              <Text style={{ fontSize: 13, color: "#64748b", marginTop: 2 }}>
                {fg.item.code} · {fg.expectedQty.toLocaleString()} KG
              </Text>
            </View>
            <Pressable onPress={handleClose} style={{ paddingHorizontal: 12, paddingVertical: 6 }}>
              <Text style={{ fontSize: 24, color: "#94a3b8" }}>×</Text>
            </Pressable>
          </View>
          <ReadinessChip readiness={readiness} />
        </View>

        {/* Scrollable content */}
        <ScrollView style={{ flex: 1 }}>
          {/* Raw materials */}
          <View style={{ backgroundColor: "#ffffff", marginTop: 12 }}>
            <Text
              style={{
                paddingHorizontal: 12,
                paddingTop: 12,
                fontSize: 13,
                fontWeight: "700",
                color: "#0f172a",
              }}
            >
              Raw materials
            </Text>
            {(fg.bomLines ?? []).map((bl, blIdx) => {
              const cost = draft.rawMaterialCosts[blIdx];
              if (!cost) return null;
              return (
                <RmCostRow
                  key={bl.id}
                  bom={bl}
                  cost={cost}
                  onChange={(p) => onChangeRm(blIdx, p)}
                />
              );
            })}
          </View>

          {/* Printing (conditional) */}
          {draft.hasPrinting ? (
            <PrintingCostSection
              costPerKg={draft.printingCostPerKg}
              currency={draft.printingCostCurrency}
              onChange={(p) =>
                onChangeFg({
                  printingCostPerKg: p.costPerKg ?? draft.printingCostPerKg,
                  printingCostCurrency: p.currency ?? draft.printingCostCurrency,
                })
              }
            />
          ) : null}

          {/* FOH + Transport + Commission */}
          <OtherCostsSection
            fohPerKg={draft.fohPerKg}
            transportPerKg={draft.transportPerKg}
            commissionPerKg={draft.commissionPerKg}
            currencyCode={req.currencyCode}
            onChange={(p) => onChangeFg(p)}
          />
        </ScrollView>

        <DrawerFooter onCancel={handleClose} onSave={onSave} saving={saving} />
      </KeyboardAvoidingView>
    </Modal>
  );
}

function ReadinessChip({ readiness }: { readiness: ReturnType<typeof fgReadiness> }) {
  const color =
    readiness === "ready" ? "#10b981" : readiness === "in_progress" ? "#f59e0b" : "#94a3b8";
  const label =
    readiness === "ready"
      ? "🟢 Ready"
      : readiness === "in_progress"
        ? "🟡 In progress"
        : "⚪ Not started";

  return (
    <View
      style={{
        alignSelf: "flex-start",
        marginTop: 8,
        paddingHorizontal: 10,
        paddingVertical: 4,
        borderRadius: 999,
        backgroundColor: `${color}22`,
      }}
    >
      <Text style={{ fontSize: 12, fontWeight: "700", color }}>{label}</Text>
    </View>
  );
}
