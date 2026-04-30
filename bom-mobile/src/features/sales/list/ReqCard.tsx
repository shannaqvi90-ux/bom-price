import { Pressable, Text, View } from "react-native";
import * as Haptics from "expo-haptics";
import type { V3RequisitionListItem } from "../../../types/v3";
import { STATUS_COLOR, STATUS_LABEL } from "../utils/statusMap";
import { OwnedByBadge } from "../../../components/OwnedByBadge";
import { useAuth } from "../../../auth/AuthContext";

interface Props {
  req: V3RequisitionListItem;
  onPress: (id: number) => void;
}

export function ReqCard({ req, onPress }: Props) {
  const { user } = useAuth();
  const userId = user?.userId ?? null;
  const isPeer = userId !== null && req.salesPersonId !== userId;

  return (
    <Pressable
      onPress={() => {
        Haptics.selectionAsync();
        onPress(req.id);
      }}
      style={{
        backgroundColor: "white", marginHorizontal: 12, marginVertical: 4,
        padding: 14, borderRadius: 12, borderWidth: 1, borderColor: "#e5e7eb",
      }}
    >
      <View style={{ flexDirection: "row", justifyContent: "space-between", alignItems: "center" }}>
        <View style={{
          paddingHorizontal: 8, paddingVertical: 3, borderRadius: 6,
          backgroundColor: STATUS_COLOR[req.status],
        }}>
          <Text style={{ color: "white", fontSize: 11, fontWeight: "600" }}>
            {STATUS_LABEL[req.status]}
          </Text>
        </View>
        <Text style={{ fontWeight: "600", color: "#0f172a" }}>{req.refNo}</Text>
      </View>
      <Text style={{ marginTop: 8, fontSize: 15, color: "#0f172a" }} numberOfLines={1}>
        {req.customerName}
      </Text>
      <View style={{ flexDirection: "row", justifyContent: "space-between", marginTop: 6 }}>
        <Text style={{ fontSize: 12, color: "#64748b" }}>
          {req.fgCount} FG · {new Date(req.createdAt).toLocaleDateString()}
        </Text>
        <Text style={{ fontSize: 12, color: "#64748b" }}>{req.currencyCode}</Text>
      </View>
      {isPeer && <OwnedByBadge ownerName={req.salesPersonName} />}
    </Pressable>
  );
}
