import { type ReactNode } from "react";
import { View } from "react-native";

export function ItemCardShell({ children }: { children: ReactNode }) {
  return (
    <View
      style={{
        backgroundColor: "#ffffff",
        borderWidth: 1,
        borderColor: "#e2e8f0",
        borderRadius: 14,
        padding: 14,
        marginBottom: 10,
      }}
    >
      {children}
    </View>
  );
}
