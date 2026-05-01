// bom-mobile/src/features/sales/create/FgListMain.tsx
import { useState } from "react";
import { View, Text, Pressable } from "react-native";
import type { V3FinishedGoodDraft } from "../../../types/v3";
import { FgEditDrawer } from "./FgEditDrawer";
import { FgPickerSheet } from "../pickers/FgPickerSheet";
import { FgCreateModal } from "../pickers/FgCreateModal";
import { theme } from "../../../theme";

interface Props {
  fgs: V3FinishedGoodDraft[];
  setFgs: (fgs: V3FinishedGoodDraft[]) => void;
}

export function FgListMain({ fgs, setFgs }: Props) {
  const [pickerOpen, setPickerOpen] = useState(false);
  const [createOpen, setCreateOpen] = useState(false);
  const [drawerForIdx, setDrawerForIdx] = useState<number | null>(null);

  const addFromPicker = (item: { id: number; code: string; description: string }) => {
    setFgs([...fgs, { itemId: item.id, code: item.code, description: item.description, expectedQty: 0, bomLines: [] }]);
    setPickerOpen(false);
    setDrawerForIdx(fgs.length); // index of just-added FG (current length before append)
  };

  return (
    <View style={{ marginHorizontal: 12, marginTop: 4 }}>
      <Text style={{ fontWeight: "600", fontSize: 14, marginVertical: 8, color: "#0f172a" }}>
        Finished Goods ({fgs.length})
      </Text>
      {fgs.map((fg, i) => (
        <Pressable
          key={i}
          onPress={() => setDrawerForIdx(i)}
          style={{ padding: 12, backgroundColor: "white", borderRadius: 10, marginBottom: 8, borderWidth: 1, borderColor: "#e5e7eb" }}
        >
          <View style={{ flexDirection: "row", justifyContent: "space-between" }}>
            <Text style={{ fontWeight: "600", color: "#0f172a" }}>{fg.code ?? `Item ${fg.itemId}`}</Text>
            <Text style={{
              color: fg.bomLines.length > 0 ? theme.colors.primary : "#92400e",
              fontWeight: "600", fontSize: 12,
            }}>
              {fg.bomLines.length > 0 ? "Edit ›" : "+ Lines"}
            </Text>
          </View>
          <Text style={{ fontSize: 12, color: "#64748b", marginTop: 4 }}>
            {fg.description} · {fg.expectedQty || "-"} kg · {fg.bomLines.length} lines
          </Text>
        </Pressable>
      ))}
      <Pressable
        onPress={() => setPickerOpen(true)}
        style={{ padding: 12, backgroundColor: "#eff6ff", borderRadius: 10, alignItems: "center" }}
      >
        <Text style={{ color: theme.colors.primary, fontWeight: "600" }}>+ Add FG</Text>
      </Pressable>

      <FgPickerSheet
        visible={pickerOpen}
        onPick={addFromPicker}
        onClose={() => setPickerOpen(false)}
        onCreateNew={() => { setPickerOpen(false); setCreateOpen(true); }}
      />
      <FgCreateModal
        visible={createOpen}
        onCreated={(newFg) => {
          addFromPicker(newFg);
          setCreateOpen(false);
        }}
        onClose={() => setCreateOpen(false)}
      />
      {drawerForIdx !== null && (
        <FgEditDrawer
          fg={fgs[drawerForIdx]}
          visible
          onSave={(updated) => {
            const next = [...fgs];
            next[drawerForIdx] = updated;
            setFgs(next);
            setDrawerForIdx(null);
          }}
          onRemove={() => {
            setFgs(fgs.filter((_, i) => i !== drawerForIdx));
            setDrawerForIdx(null);
          }}
          onClose={() => setDrawerForIdx(null)}
        />
      )}
    </View>
  );
}
