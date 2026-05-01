import { Pressable, Text, View } from "react-native";
import { MotiView } from "moti";
import { Skeleton } from "../../../components/Skeleton";

interface Props {
  count: number | undefined;
  loading: boolean;
  onPress: () => void;
}

export function KpiHeroCard({ count, loading, onPress }: Props) {
  return (
    <MotiView
      from={{ opacity: 0, translateY: 14 }}
      animate={{ opacity: 1, translateY: 0 }}
      transition={{ type: "spring", damping: 14, stiffness: 140, delay: 100 }}
    >
      <Pressable
        onPress={onPress}
        style={({ pressed }) => ({ transform: [{ scale: pressed ? 0.98 : 1 }] })}
      >
        <View style={{
          backgroundColor: "#1e40af",
          borderRadius: 16,
          padding: 20,
          marginBottom: 12,
          shadowColor: "#1e40af",
          shadowOffset: { width: 0, height: 6 },
          shadowOpacity: 0.3,
          shadowRadius: 12,
          elevation: 6,
        }}>
          <Text style={{ color: "#dbeafe", fontSize: 13, fontWeight: "600", letterSpacing: 0.5 }}>
            COSTING TO COMPLETE
          </Text>
          <View style={{ flexDirection: "row", alignItems: "flex-end", marginTop: 10 }}>
            {loading ? (
              <Skeleton width={80} height={44} radius={8} style={{ backgroundColor: "rgba(255,255,255,0.25)" }} />
            ) : (
              <Text style={{ color: "#ffffff", fontSize: 44, fontWeight: "800", letterSpacing: -1 }}>
                {count ?? 0}
              </Text>
            )}
            <Text style={{ color: "#dbeafe", fontSize: 15, marginLeft: 10, marginBottom: 8 }}>
              to review
            </Text>
          </View>
          <Text style={{ color: "#dbeafe", fontSize: 14, marginTop: 14 }}>
            Tap to open the queue →
          </Text>
        </View>
      </Pressable>
    </MotiView>
  );
}
