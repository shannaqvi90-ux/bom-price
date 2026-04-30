// bom-mobile/src/features/sales/detail/DetailHeader.tsx
import { View, Text } from "react-native";
import type { V3Requisition } from "../../../types/v3";
import { STATUS_COLOR, STATUS_LABEL } from "../utils/statusMap";

export function DetailHeader({ req }: { req: V3Requisition }) {
  return (
    <View style={{ padding: 16, backgroundColor: "white", borderBottomWidth: 1, borderColor: "#e5e7eb" }}>
      <View style={{ flexDirection: "row", alignItems: "center", justifyContent: "space-between" }}>
        <View style={{
          paddingHorizontal: 10, paddingVertical: 4, borderRadius: 6,
          backgroundColor: STATUS_COLOR[req.status],
        }}>
          <Text style={{ color: "white", fontSize: 12, fontWeight: "600" }}>
            {STATUS_LABEL[req.status]}
          </Text>
        </View>
        <Text style={{ fontWeight: "700", fontSize: 16, color: "#0f172a" }}>{req.refNo}</Text>
      </View>
      <Text style={{ marginTop: 10, fontSize: 18, color: "#0f172a", fontWeight: "600" }}>
        {req.customer.name}
      </Text>
      <Text style={{ fontSize: 12, color: "#64748b", marginTop: 2 }}>
        Updated {new Date(req.updatedAt).toLocaleString()}
      </Text>
    </View>
  );
}
