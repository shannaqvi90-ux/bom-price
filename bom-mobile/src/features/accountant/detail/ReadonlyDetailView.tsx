// bom-mobile/src/features/accountant/detail/ReadonlyDetailView.tsx
import { ScrollView, Text, View } from "react-native";
import type { V3Requisition } from "../../../types/v3";
import { DetailHeader } from "../../sales/detail/DetailHeader";
import { FgReadCard } from "../../sales/detail/FgReadCard";
import { FinalPriceCard } from "../../sales/detail/FinalPriceCard";

interface Props {
  req: V3Requisition;
}

export function ReadonlyDetailView({ req }: Props) {
  return (
    <ScrollView contentContainerStyle={{ paddingBottom: 24 }}>
      <DetailHeader req={req} />

      {req.status === "MdPricing" ? (
        <FooterText text="Waiting on MD margin pricing" tone="info" />
      ) : null}
      {req.status === "CustomerConfirm" ? (
        <FooterText text="Waiting on SP customer-confirm" tone="info" />
      ) : null}
      {req.status === "MdFinalSign" ? (
        <FooterText text="Waiting on MD final sign" tone="info" />
      ) : null}
      {req.status === "Rejected" ? (
        <FooterText
          text={`Rejected: ${req.cancelReason ?? "(no reason recorded)"}`}
          tone="danger"
        />
      ) : null}
      {req.status === "Cancelled" ? (
        <FooterText
          text={`Cancelled${req.cancelledAt ? ` on ${new Date(req.cancelledAt).toLocaleDateString()}` : ""}: ${req.cancelReason ?? "(no reason)"}`}
          tone="muted"
        />
      ) : null}

      {req.finishedGoods.map((fg, idx) => (
        <FgReadCard key={fg.id} fg={fg} index={idx} />
      ))}

      {req.status === "Signed" ? <FinalPriceCard req={req} /> : null}
    </ScrollView>
  );
}

function FooterText({ text, tone }: { text: string; tone: "info" | "danger" | "muted" }) {
  const bg = tone === "info" ? "#eff6ff" : tone === "danger" ? "#fee2e2" : "#f1f5f9";
  const fg = tone === "info" ? "#1e40af" : tone === "danger" ? "#b91c1c" : "#475569";
  return (
    <View
      style={{
        backgroundColor: bg,
        marginHorizontal: 12,
        marginVertical: 8,
        padding: 12,
        borderRadius: 10,
      }}
    >
      <Text style={{ color: fg, fontSize: 14, fontWeight: "600" }}>{text}</Text>
    </View>
  );
}
