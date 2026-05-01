import { View } from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { Button } from "../../../components/Button";

interface Props {
  readyCount: number;
  totalCount: number;
  submitting: boolean;
  onSubmit: () => void;
}

export function SubmitAllFooter({ readyCount, totalCount, submitting, onSubmit }: Props) {
  const insets = useSafeAreaInsets();
  const enabled = !submitting && readyCount === totalCount && totalCount > 0;
  const title = enabled
    ? "Submit to MD"
    : `${readyCount} of ${totalCount} FGs ready`;

  return (
    <View style={{
      borderTopWidth: 1, borderTopColor: "#e2e8f0",
      backgroundColor: "#ffffff",
      paddingHorizontal: 12, paddingTop: 10,
      paddingBottom: Math.max(insets.bottom, 12),
    }}>
      <Button
        title={title}
        variant="primary"
        onPress={onSubmit}
        disabled={!enabled}
        loading={submitting}
      />
    </View>
  );
}
