import { useLocalSearchParams } from "expo-router";
import { HistoricalRequisitionScreen } from "@/components/HistoricalRequisitionScreen";

export default function MdHistoricalDetail() {
  const params = useLocalSearchParams<{ id: string }>();
  return <HistoricalRequisitionScreen requisitionId={Number(params.id)} />;
}
