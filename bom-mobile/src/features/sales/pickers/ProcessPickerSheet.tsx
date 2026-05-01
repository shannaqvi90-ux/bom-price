// bom-mobile/src/features/sales/pickers/ProcessPickerSheet.tsx
import { Modal, View, Text, Pressable, FlatList, ActivityIndicator } from "react-native";
import { useProcesses, type ProcessLite } from "../../../api/lookups";

interface Props {
  visible: boolean;
  onPick: (process: ProcessLite) => void;
  onClose: () => void;
}

export function ProcessPickerSheet({ visible, onPick, onClose }: Props) {
  const { data, isLoading } = useProcesses();

  return (
    <Modal visible={visible} transparent animationType="slide" onRequestClose={onClose}>
      <Pressable onPress={onClose} style={{ flex: 1, backgroundColor: "rgba(0,0,0,0.45)" }} />
      <View style={{
        backgroundColor: "white",
        borderTopLeftRadius: 18, borderTopRightRadius: 18,
        padding: 16, maxHeight: "60%",
      }}>
        <View style={{ alignSelf: "center", width: 40, height: 4, backgroundColor: "#cbd5e1", borderRadius: 2, marginBottom: 12 }} />
        <Text style={{ fontWeight: "600", fontSize: 16 }}>Select process</Text>
        {isLoading ? <ActivityIndicator style={{ marginTop: 20 }} /> : (
          <FlatList
            data={data ?? []}
            keyExtractor={(p) => String(p.id)}
            renderItem={({ item }) => (
              <Pressable
                onPress={() => onPick(item)}
                style={{ paddingVertical: 12, borderBottomWidth: 1, borderColor: "#f1f5f9" }}
              >
                <Text style={{ fontWeight: "500", color: "#0f172a" }}>{item.name}</Text>
              </Pressable>
            )}
            ListEmptyComponent={<Text style={{ padding: 16, color: "#94a3b8" }}>No processes available.</Text>}
          />
        )}
      </View>
    </Modal>
  );
}
