import { useState } from "react";
import { Modal, View, Text, TextInput, Pressable, FlatList, ActivityIndicator } from "react-native";
import { useCustomers, type CustomerLite } from "../../../api/customers";
import { theme } from "../../../theme";

interface Props {
  visible: boolean;
  onPick: (customer: CustomerLite) => void;
  onClose: () => void;
  onCreateNew: () => void;
}

export function CustomerPickerSheet({ visible, onPick, onClose, onCreateNew }: Props) {
  const [search, setSearch] = useState("");
  const { data, isLoading } = useCustomers(search);

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
          <Text style={{ fontWeight: "600", fontSize: 16 }}>Select customer</Text>
          <Pressable onPress={onCreateNew}>
            <Text style={{ color: theme.colors.primary, fontWeight: "600" }}>+ Create new</Text>
          </Pressable>
        </View>
        <TextInput
          value={search}
          onChangeText={setSearch}
          placeholder="Search by name…"
          style={{
            marginTop: 10, padding: 10, borderRadius: 8,
            backgroundColor: "#f1f5f9", fontSize: 14,
          }}
        />
        {isLoading ? <ActivityIndicator style={{ marginTop: 20 }} /> : (
          <FlatList
            data={data ?? []}
            keyExtractor={(c) => String(c.id)}
            keyboardShouldPersistTaps="handled"
            renderItem={({ item }) => (
              <Pressable
                onPress={() => onPick(item)}
                style={{ paddingVertical: 12, borderBottomWidth: 1, borderColor: "#f1f5f9" }}
              >
                <Text style={{ fontWeight: "500", color: "#0f172a" }}>{item.name}</Text>
                <Text style={{ fontSize: 12, color: "#64748b" }}>{item.code}</Text>
              </Pressable>
            )}
            ListEmptyComponent={<Text style={{ padding: 16, color: "#94a3b8" }}>No customers found.</Text>}
          />
        )}
      </View>
    </Modal>
  );
}
