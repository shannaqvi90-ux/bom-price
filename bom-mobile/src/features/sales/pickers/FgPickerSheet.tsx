// bom-mobile/src/features/sales/pickers/FgPickerSheet.tsx
import { useMemo, useState } from "react";
import { Modal, View, Text, TextInput, Pressable, FlatList, ActivityIndicator } from "react-native";
import { useItems } from "../../../api/lookups";
import { useAuth } from "../../../auth/AuthContext";
import { theme } from "../../../theme";
import type { Item } from "@/types/api";

interface Props {
  visible: boolean;
  onPick: (item: Item) => void;
  onClose: () => void;
  onCreateNew: () => void;
}

export function FgPickerSheet({ visible, onPick, onClose, onCreateNew }: Props) {
  const { user } = useAuth();
  const [search, setSearch] = useState("");
  const { data, isLoading } = useItems({ type: "FinishedGood", branchId: user?.branchId ?? undefined });

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    if (!q) return data ?? [];
    return (data ?? []).filter((it) =>
      it.description.toLowerCase().includes(q) || it.code.toLowerCase().includes(q)
    );
  }, [data, search]);

  return (
    <Modal visible={visible} transparent animationType="slide" onRequestClose={onClose}>
      <Pressable onPress={onClose} style={{ flex: 1, backgroundColor: "rgba(0,0,0,0.45)" }} />
      <View style={{
        backgroundColor: "white",
        borderTopLeftRadius: 18, borderTopRightRadius: 18,
        padding: 16, maxHeight: "70%",
      }}>
        <View style={{ alignSelf: "center", width: 40, height: 4, backgroundColor: "#cbd5e1", borderRadius: 2, marginBottom: 12 }} />
        <View style={{ flexDirection: "row", alignItems: "center", justifyContent: "space-between" }}>
          <Text style={{ fontWeight: "600", fontSize: 16 }}>Select finished good</Text>
          <Pressable onPress={onCreateNew}>
            <Text style={{ color: theme.colors.primary, fontWeight: "600" }}>+ Create new</Text>
          </Pressable>
        </View>
        <TextInput
          value={search}
          onChangeText={setSearch}
          placeholder="Search by code or description…"
          style={{
            marginTop: 10, padding: 10, borderRadius: 8,
            backgroundColor: "#f1f5f9", fontSize: 14,
          }}
        />
        {isLoading ? <ActivityIndicator style={{ marginTop: 20 }} /> : (
          <FlatList
            data={filtered}
            keyExtractor={(it) => String(it.id)}
            keyboardShouldPersistTaps="handled"
            renderItem={({ item }) => (
              <Pressable
                onPress={() => onPick(item)}
                style={{ paddingVertical: 12, borderBottomWidth: 1, borderColor: "#f1f5f9" }}
              >
                <Text style={{ fontWeight: "500", color: "#0f172a" }}>{item.code}</Text>
                <Text style={{ fontSize: 12, color: "#64748b" }}>{item.description}</Text>
              </Pressable>
            )}
            ListEmptyComponent={<Text style={{ padding: 16, color: "#94a3b8" }}>No finished goods found.</Text>}
          />
        )}
      </View>
    </Modal>
  );
}
