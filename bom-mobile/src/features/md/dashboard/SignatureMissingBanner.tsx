import { Pressable, Text } from "react-native";
import { useRouter } from "expo-router";
import * as Haptics from "expo-haptics";

export function SignatureMissingBanner() {
  const router = useRouter();
  return (
    <Pressable
      onPress={() => {
        Haptics.selectionAsync();
        router.push("/profile");
      }}
      style={{
        marginHorizontal: 12,
        marginVertical: 8,
        padding: 14,
        borderRadius: 12,
        backgroundColor: "#fef3c7",
        borderWidth: 1,
        borderColor: "#fde68a",
      }}
    >
      <Text style={{ color: "#92400e", fontSize: 14, fontWeight: "600" }}>
        ⚠️ No signature uploaded
      </Text>
      <Text style={{ color: "#92400e", fontSize: 13, marginTop: 4 }}>
        Tap to upload your signature in Profile (required to sign quotations).
      </Text>
    </Pressable>
  );
}
