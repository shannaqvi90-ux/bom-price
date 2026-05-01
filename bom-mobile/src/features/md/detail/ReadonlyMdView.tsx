import { ScrollView, Text, View } from "react-native";
import type { V3Requisition } from "@/types/v3";
import { DetailHeader } from "@/features/sales/detail/DetailHeader";
import { FgReadCard } from "@/features/sales/detail/FgReadCard";
import { FinalPriceCard } from "@/features/sales/detail/FinalPriceCard";

interface Props {
  req: V3Requisition;
}

export function ReadonlyMdView({ req }: Props) {
  const banner = bannerFor(req);
  return (
    <ScrollView contentContainerStyle={{ paddingBottom: 24 }}>
      <DetailHeader req={req} />

      {banner ? (
        <View
          style={{
            backgroundColor: banner.bg,
            marginHorizontal: 12,
            marginVertical: 8,
            padding: 12,
            borderRadius: 10,
          }}
        >
          <Text style={{ color: banner.fg, fontSize: 14, fontWeight: "600" }}>{banner.text}</Text>
        </View>
      ) : null}

      {req.finishedGoods.map((fg, idx) => (
        <FgReadCard key={fg.id} fg={fg} index={idx} />
      ))}

      {req.status === "Signed" ? <FinalPriceCard req={req} /> : null}
    </ScrollView>
  );
}

function bannerFor(req: V3Requisition): { text: string; bg: string; fg: string } | null {
  if (req.status === "Costing" || req.status === "Draft")
    return { text: "Waiting on accountant costing", bg: "#eff6ff", fg: "#1e40af" };
  if (req.status === "CustomerConfirm")
    return { text: "Waiting on SP customer-confirm", bg: "#eff6ff", fg: "#1e40af" };
  if (req.status === "Rejected")
    return { text: `Rejected: ${req.cancelReason ?? "(no reason)"}`, bg: "#fee2e2", fg: "#b91c1c" };
  if (req.status === "Cancelled")
    return { text: `Cancelled: ${req.cancelReason ?? "(no reason)"}`, bg: "#f1f5f9", fg: "#475569" };
  return null;
}
