import { type ReactNode } from "react";
import { Text, View } from "react-native";
import { MotiView } from "moti";

interface Props {
  title: string;
  hint?: string;
  icon?: ReactNode;
}

export function EmptyState({ title, hint, icon }: Props) {
  return (
    <View
      style={{
        flex: 1,
        alignItems: "center",
        justifyContent: "center",
        paddingHorizontal: 32,
        paddingVertical: 64,
      }}
    >
      {icon ? (
        <MotiView
          from={{ translateY: 0 }}
          animate={{ translateY: -4 }}
          transition={{ type: "timing", duration: 1800, loop: true, repeatReverse: true }}
          style={{
            width: 72,
            height: 72,
            borderRadius: 36,
            backgroundColor: "#dbeafe",
            alignItems: "center",
            justifyContent: "center",
            marginBottom: 16,
          }}
        >
          {icon}
        </MotiView>
      ) : null}
      <Text style={{ fontSize: 17, fontWeight: "700", color: "#0f172a" }}>{title}</Text>
      {hint ? (
        <Text style={{ fontSize: 13, color: "#64748b", marginTop: 6, textAlign: "center" }}>
          {hint}
        </Text>
      ) : null}
    </View>
  );
}
