import { Pressable, Text, View } from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import * as Haptics from "expo-haptics";

interface Props {
  readyCount: number;
  totalCount: number;
  submitting: boolean;
  onSubmit: () => void;
}

export function SubmitAllFooter({ readyCount, totalCount, submitting, onSubmit }: Props) {
  const insets = useSafeAreaInsets();
  const enabled = !submitting && readyCount === totalCount && totalCount > 0;
  return (
    <View style={{
      borderTopWidth: 1, borderTopColor: "#e2e8f0",
      backgroundColor: "#ffffff",
      paddingHorizontal: 12, paddingTop: 10,
      paddingBottom: Math.max(insets.bottom, 12),
    }}>
      <Pressable
        onPress={() => { if (enabled) { Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium); onSubmit(); } }}
        disabled={!enabled}
        style={({ pressed }) => ({
          paddingVertical: 14, borderRadius: 12,
          backgroundColor: enabled ? "#1e40af" : "#cbd5e1",
          opacity: pressed && enabled ? 0.85 : 1,
          alignItems: "center",
        })}
      >
        <Text style={{ fontSize: 16, color: "#ffffff", fontWeight: "700" }}>
          {submitting ? "Submitting…" : enabled ? "Submit to MD" : `${readyCount} of ${totalCount} FGs ready`}
        </Text>
      </Pressable>
    </View>
  );
}
