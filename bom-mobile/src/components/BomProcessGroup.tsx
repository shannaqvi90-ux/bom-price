import { Text, View } from "react-native";
import { BomLineRow } from "./BomLineRow";
import type { BomLine } from "@/types/api";

interface Props {
  processName: string;
  lines: BomLine[];
}

export function BomProcessGroup({ processName, lines }: Props) {
  return (
    <View style={{ marginTop: 12 }}>
      <Text
        style={{
          fontSize: 13,
          fontWeight: "700",
          color: "#64748b",
          letterSpacing: 0.5,
          marginBottom: 6,
        }}
      >
        {`BOM — ${processName.toUpperCase()} (${lines.length})`}
      </Text>
      {lines.map((l) => (
        <BomLineRow key={l.id} line={l} />
      ))}
    </View>
  );
}
