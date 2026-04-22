import { ActivityIndicator, View } from "react-native";
import { Skeleton } from "./Skeleton";

interface Props {
  variant?: "spinner" | "list";
}

export function LoadingView({ variant = "spinner" }: Props) {
  if (variant === "list") {
    return (
      <View style={{ padding: 16 }}>
        {[0, 1, 2, 3].map((i) => (
          <View
            key={i}
            style={{
              backgroundColor: "#ffffff",
              borderWidth: 1,
              borderColor: "#e2e8f0",
              borderRadius: 14,
              padding: 14,
              marginBottom: 10,
            }}
          >
            <View style={{ flexDirection: "row", justifyContent: "space-between", marginBottom: 10 }}>
              <Skeleton width={80} height={14} />
              <Skeleton width={90} height={18} radius={6} />
            </View>
            <Skeleton width={"70%"} height={12} style={{ marginBottom: 8 }} />
            <View style={{ flexDirection: "row", justifyContent: "space-between" }}>
              <Skeleton width={100} height={10} />
              <Skeleton width={60} height={10} />
            </View>
          </View>
        ))}
      </View>
    );
  }
  return (
    <View style={{ flex: 1, alignItems: "center", justifyContent: "center" }}>
      <ActivityIndicator size="large" color="#1e40af" />
    </View>
  );
}
