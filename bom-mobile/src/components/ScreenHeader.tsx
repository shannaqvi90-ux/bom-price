import { type ReactNode } from "react";
import { Text, View } from "react-native";
import { MotiView } from "moti";
import { useSafeAreaInsets } from "react-native-safe-area-context";

interface Props {
  label?: string;
  title: string;
  count?: number;
  right?: ReactNode;
}

export function ScreenHeader({ label, title, count, right }: Props) {
  const insets = useSafeAreaInsets();
  return (
    <MotiView
      from={{ opacity: 0, translateY: -10 }}
      animate={{ opacity: 1, translateY: 0 }}
      transition={{ type: "spring", damping: 14, stiffness: 140 }}
      style={{
        paddingHorizontal: 16,
        paddingTop: insets.top + 12,
        paddingBottom: 14,
        flexDirection: "row",
        alignItems: "flex-end",
        justifyContent: "space-between",
      }}
    >
      <View style={{ flex: 1, flexShrink: 1 }}>
        {label ? (
          <Text style={{ fontSize: 11, fontWeight: "600", color: "#64748b" }}>
            {label}
          </Text>
        ) : null}
        <View style={{ flexDirection: "row", alignItems: "center", marginTop: 3 }}>
          <Text
            style={{
              fontSize: 22,
              fontWeight: "700",
              color: "#0f172a",
              letterSpacing: -0.5,
            }}
            numberOfLines={1}
          >
            {title}
          </Text>
          {typeof count === "number" && count > 0 ? (
            <MotiView
              from={{ scale: 0 }}
              animate={{ scale: 1 }}
              transition={{
                type: "spring",
                damping: 10,
                stiffness: 220,
                delay: 300,
              }}
              style={{
                marginLeft: 10,
                backgroundColor: "#1e40af",
                paddingHorizontal: 10,
                paddingVertical: 3,
                borderRadius: 999,
                shadowColor: "#1e40af",
                shadowOffset: { width: 0, height: 2 },
                shadowOpacity: 0.3,
                shadowRadius: 6,
                elevation: 3,
              }}
            >
              <Text style={{ color: "white", fontSize: 12, fontWeight: "700" }}>
                {count}
              </Text>
            </MotiView>
          ) : null}
        </View>
      </View>
      {right ? (
        <View style={{ flexDirection: "row", alignItems: "center", gap: 6 }}>
          {right}
        </View>
      ) : null}
    </MotiView>
  );
}
