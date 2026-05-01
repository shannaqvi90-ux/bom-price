import { Pressable, Text, View } from "react-native";
import { MotiView } from "moti";
import { Skeleton } from "../../../components/Skeleton";

interface Props {
  label: string;
  value: number | undefined;
  loading: boolean;
  delay: number;
  onPress: () => void;
}

export function KpiRow({ label, value, loading, delay, onPress }: Props) {
  return (
    <MotiView
      from={{ opacity: 0, translateY: 14 }}
      animate={{ opacity: 1, translateY: 0 }}
      transition={{ type: "spring", damping: 14, stiffness: 140, delay }}
    >
      <Pressable
        onPress={onPress}
        style={({ pressed }) => ({ transform: [{ scale: pressed ? 0.98 : 1 }] })}
      >
        <View style={{
          backgroundColor: "#ffffff",
          borderWidth: 1,
          borderColor: "#e2e8f0",
          borderRadius: 14,
          padding: 14,
          marginBottom: 8,
          flexDirection: "row",
          alignItems: "center",
          justifyContent: "space-between",
        }}>
          <Text style={{ color: "#64748b", fontSize: 12, fontWeight: "700", letterSpacing: 0.5 }}>
            {label}
          </Text>
          {loading ? (
            <Skeleton width={36} height={26} />
          ) : (
            <Text style={{ color: "#0f172a", fontSize: 22, fontWeight: "800" }}>{value ?? 0}</Text>
          )}
        </View>
      </Pressable>
    </MotiView>
  );
}
