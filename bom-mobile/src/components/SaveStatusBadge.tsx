import { Pressable, Text, View } from "react-native";

export type SaveStatus = "idle" | "saving" | "saved" | "error";

interface Props {
  status: SaveStatus;
  onRetry?: () => void;
}

const COLORS: Record<SaveStatus, { bg: string; fg: string; label: string }> = {
  idle:   { bg: "transparent", fg: "transparent", label: "" },
  saving: { bg: "#eff6ff",     fg: "#1e40af",     label: "Saving…" },
  saved:  { bg: "#ecfdf5",     fg: "#047857",     label: "Saved" },
  error:  { bg: "#fef2f2",     fg: "#b91c1c",     label: "Save failed — tap to retry" },
};

export function SaveStatusBadge({ status, onRetry }: Props) {
  if (status === "idle") return null;
  const { bg, fg, label } = COLORS[status];
  if (status === "error" && onRetry) {
    return (
      <Pressable
        onPress={onRetry}
        style={{
          backgroundColor: bg,
          paddingVertical: 4,
          paddingHorizontal: 10,
          borderRadius: 12,
        }}
      >
        <Text style={{ color: fg, fontSize: 12, fontWeight: "600" }}>{label}</Text>
      </Pressable>
    );
  }
  return (
    <View
      style={{
        backgroundColor: bg,
        paddingVertical: 4,
        paddingHorizontal: 10,
        borderRadius: 12,
      }}
    >
      <Text style={{ color: fg, fontSize: 12, fontWeight: "600" }}>{label}</Text>
    </View>
  );
}
