import { type ViewStyle, type StyleProp } from "react-native";
import { MotiView } from "moti";

interface Props {
  width?: number | `${number}%`;
  height?: number;
  radius?: number;
  style?: StyleProp<ViewStyle>;
}

export function Skeleton({ width = "100%", height = 14, radius = 6, style }: Props) {
  return (
    <MotiView
      from={{ opacity: 0.4 }}
      animate={{ opacity: 1 }}
      transition={{ type: "timing", duration: 900, loop: true, repeatReverse: true }}
      style={[
        {
          width,
          height,
          borderRadius: radius,
          backgroundColor: "#e2e8f0",
        },
        style,
      ]}
    />
  );
}
