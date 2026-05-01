// bom-mobile/src/features/sales/create/SubmitFooter.tsx
import { View, Pressable, Text, ActivityIndicator } from "react-native";
import { theme } from "../../../theme";
import type { ValidationResult } from "./validate";

interface Props {
  validation: ValidationResult;
  onSubmit: () => void;
  loading: boolean;
  label: string;
}

export function SubmitFooter({ validation, onSubmit, loading, label }: Props) {
  return (
    <View style={{
      padding: 16, paddingBottom: 32, backgroundColor: "white",
      borderTopWidth: 1, borderColor: "#e5e7eb",
    }}>
      {!validation.ok && (
        <Text style={{ color: "#dc2626", fontSize: 11, marginBottom: 8 }}>
          {validation.errors[0]}
          {validation.errors.length > 1 ? ` (+ ${validation.errors.length - 1} more)` : ""}
        </Text>
      )}
      <Pressable
        onPress={onSubmit}
        disabled={!validation.ok || loading}
        style={{
          backgroundColor: theme.colors.primary, padding: 14, borderRadius: 10,
          alignItems: "center", opacity: !validation.ok || loading ? 0.5 : 1,
        }}
      >
        {loading ? <ActivityIndicator color="white" /> : (
          <Text style={{ color: "white", fontWeight: "600", fontSize: 15 }}>{label}</Text>
        )}
      </Pressable>
    </View>
  );
}
