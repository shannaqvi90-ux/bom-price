import { type ReactNode } from "react";
import { Text, View } from "react-native";

interface Props {
  title: string;
  children: ReactNode;
}

export function SectionCard({ title, children }: Props) {
  return (
    <View
      style={{
        backgroundColor: "#ffffff",
        borderWidth: 1,
        borderColor: "#e2e8f0",
        borderRadius: 14,
        padding: 14,
        marginBottom: 12,
      }}
    >
      <Text
        style={{
          fontSize: 13,
          fontWeight: "700",
          color: "#64748b",
          marginBottom: 10,
          letterSpacing: 0.3,
        }}
      >
        {title.toUpperCase()}
      </Text>
      {children}
    </View>
  );
}
