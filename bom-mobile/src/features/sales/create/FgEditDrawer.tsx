// bom-mobile/src/features/sales/create/FgEditDrawer.tsx
import { useState } from "react";
import { Modal, View, Text, TextInput, Pressable, ScrollView, KeyboardAvoidingView, Platform } from "react-native";
import type { V3FinishedGood, V3BomLine } from "../../../types/v3";
import { BomLineRow } from "./BomLineRow";
import { RmPickerSheet } from "../pickers/RmPickerSheet";
import { ProcessPickerSheet } from "../pickers/ProcessPickerSheet";
import { theme } from "../../../theme";

interface Props {
  fg: V3FinishedGood;
  visible: boolean;
  onSave: (fg: V3FinishedGood) => void;
  onClose: () => void;
  onRemove: () => void;
}

export function FgEditDrawer({ fg, visible, onSave, onClose, onRemove }: Props) {
  const [draft, setDraft] = useState<V3FinishedGood>(fg);
  const [rmPickerForIdx, setRmPickerForIdx] = useState<number | null>(null);
  const [processPickerForIdx, setProcessPickerForIdx] = useState<number | null>(null);

  const updateLine = (idx: number, line: V3BomLine) => {
    const lines = [...draft.bomLines];
    lines[idx] = line;
    setDraft({ ...draft, bomLines: lines });
  };
  const removeLine = (idx: number) => {
    setDraft({ ...draft, bomLines: draft.bomLines.filter((_, i) => i !== idx) });
  };
  const addLine = () => {
    setDraft({ ...draft, bomLines: [...draft.bomLines, { processId: 0, rawMaterialItemId: 0, qtyPerKg: 0, wastagePct: 0 }] });
  };

  return (
    <Modal visible={visible} transparent animationType="slide" onRequestClose={onClose}>
      <Pressable onPress={onClose} style={{ flex: 1, backgroundColor: "rgba(0,0,0,0.45)" }} />
      <KeyboardAvoidingView behavior={Platform.OS === "ios" ? "padding" : undefined}>
        <View style={{
          backgroundColor: "white", borderTopLeftRadius: 18, borderTopRightRadius: 18,
          padding: 16, maxHeight: "85%",
        }}>
          <View style={{ alignSelf: "center", width: 40, height: 4, backgroundColor: "#cbd5e1", borderRadius: 2, marginBottom: 12 }} />
          <View style={{ flexDirection: "row", justifyContent: "space-between", alignItems: "center" }}>
            <Text style={{ fontWeight: "600", fontSize: 16 }}>{draft.code ?? "Edit FG"}</Text>
            <Pressable onPress={() => { onSave(draft); }}>
              <Text style={{ color: theme.colors.primary, fontWeight: "600" }}>Done</Text>
            </Pressable>
          </View>
          <ScrollView contentContainerStyle={{ paddingVertical: 12 }} keyboardShouldPersistTaps="handled">
            <Text style={{ fontSize: 11, color: "#64748b", textTransform: "uppercase", fontWeight: "600" }}>Expected Qty (kg)</Text>
            <TextInput
              value={String(draft.expectedQty)}
              keyboardType="decimal-pad"
              onChangeText={(t) => setDraft({ ...draft, expectedQty: parseFloat(t) || 0 })}
              style={{ padding: 10, backgroundColor: "#f1f5f9", borderRadius: 8, marginTop: 4 }}
            />
            <Text style={{ marginTop: 16, fontSize: 13, fontWeight: "600", color: "#0f172a" }}>BOM Lines ({draft.bomLines.length})</Text>
            {draft.bomLines.map((line, i) => (
              <BomLineRow
                key={i} line={line} idx={i}
                onChange={(l) => updateLine(i, l)}
                onPickRm={() => setRmPickerForIdx(i)}
                onPickProcess={() => setProcessPickerForIdx(i)}
                onRemove={() => removeLine(i)}
              />
            ))}
            <Pressable onPress={addLine}
              style={{ marginTop: 10, padding: 10, backgroundColor: "#eff6ff", borderRadius: 8, alignItems: "center" }}>
              <Text style={{ color: theme.colors.primary, fontWeight: "600" }}>+ Add line</Text>
            </Pressable>
            <Pressable onPress={onRemove}
              style={{ marginTop: 24, padding: 12, alignItems: "center" }}>
              <Text style={{ color: "#dc2626", fontWeight: "600" }}>Remove this FG</Text>
            </Pressable>
          </ScrollView>
        </View>
      </KeyboardAvoidingView>
      {rmPickerForIdx !== null && (
        <RmPickerSheet
          visible
          onPick={(rm) => {
            updateLine(rmPickerForIdx, { ...draft.bomLines[rmPickerForIdx], rawMaterialItemId: rm.id, rawMaterialDescription: rm.description });
            setRmPickerForIdx(null);
          }}
          onClose={() => setRmPickerForIdx(null)}
          onCreateNew={() => { /* RmCreateModal — drawer-within-drawer chaining deferred per spec OQ#2 (Android testing required); SP can create RM from main create screen instead */ }}
        />
      )}
      {processPickerForIdx !== null && (
        <ProcessPickerSheet
          visible
          onPick={(p) => {
            updateLine(processPickerForIdx, { ...draft.bomLines[processPickerForIdx], processId: p.id, processName: p.name });
            setProcessPickerForIdx(null);
          }}
          onClose={() => setProcessPickerForIdx(null)}
        />
      )}
    </Modal>
  );
}
