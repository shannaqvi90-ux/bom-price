import { View } from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { Button } from "../../../components/Button";

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
        <Button title="Cancel" variant="secondary" onPress={onCancel} />
      </View>
      <View style={{ flex: 2 }}>
        <Button title="Save & Close" variant="primary" loading={saving} onPress={onSave} />
      </View>
    </View>
  );
}
