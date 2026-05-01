import { Pressable, Text, View } from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";

interface Props {
  onCancel: () => void;
  onSave: () => void;
  saving: boolean;
}

export function DrawerFooter({ onCancel, onSave, saving }: Props) {
  const insets = useSafeAreaInsets();

  return (
    <View
      style={{
        flexDirection: "row",
        gap: 8,
        padding: 12,
        paddingBottom: Math.max(insets.bottom, 12),
        borderTopWidth: 1,
        borderTopColor: "#e2e8f0",
        backgroundColor: "#ffffff",
      }}
    >
      <View style={{ flex: 1 }}>
        <Pressable
          onPress={onCancel}
          style={({ pressed }) => ({
            paddingVertical: 12,
            borderRadius: 10,
            borderWidth: 1,
            borderColor: "#cbd5e1",
            opacity: pressed ? 0.7 : 1,
            alignItems: "center",
          })}
        >
          <Text style={{ fontSize: 15, color: "#475569", fontWeight: "600" }}>Cancel</Text>
        </Pressable>
      </View>

      <View style={{ flex: 2 }}>
        <Pressable
          onPress={onSave}
          disabled={saving}
          style={({ pressed }) => ({
            paddingVertical: 12,
            borderRadius: 10,
            backgroundColor: saving ? "#93c5fd" : "#1e40af",
            opacity: pressed && !saving ? 0.85 : 1,
            alignItems: "center",
          })}
        >
          <Text style={{ fontSize: 15, color: "#ffffff", fontWeight: "700" }}>
            {saving ? "Saving…" : "Save & Close"}
          </Text>
        </Pressable>
      </View>
    </View>
  );
}
