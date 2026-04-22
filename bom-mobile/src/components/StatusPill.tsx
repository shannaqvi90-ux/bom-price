import { Text, View } from "react-native";

type Status =
  | "BomPending"
  | "BomInProgress"
  | "CostingPending"
  | "CostingInProgress"
  | "MdReview"
  | "Approved"
  | "Rejected";

const theme: Record<Status, { bg: string; fg: string; label: string }> = {
  BomPending: { bg: "#fef3c7", fg: "#92400e", label: "BOM PENDING" },
  BomInProgress: { bg: "#dbeafe", fg: "#1e40af", label: "BOM IN PROGRESS" },
  CostingPending: { bg: "#fef3c7", fg: "#92400e", label: "COSTING PENDING" },
  CostingInProgress: { bg: "#dbeafe", fg: "#1e40af", label: "COSTING IN PROGRESS" },
  MdReview: { bg: "#ede9fe", fg: "#6d28d9", label: "MD REVIEW" },
  Approved: { bg: "#d1fae5", fg: "#065f46", label: "APPROVED" },
  Rejected: { bg: "#fee2e2", fg: "#991b1b", label: "REJECTED" },
};

export function StatusPill({ status }: { status: Status }) {
  const t = theme[status] ?? { bg: "#e2e8f0", fg: "#334155", label: String(status) };
  return (
    <View
      style={{
        backgroundColor: t.bg,
        paddingHorizontal: 10,
        paddingVertical: 4,
        borderRadius: 6,
        alignSelf: "flex-start",
      }}
    >
      <Text style={{ color: t.fg, fontSize: 12, fontWeight: "700", letterSpacing: 0.3 }}>
        {t.label}
      </Text>
    </View>
  );
}
