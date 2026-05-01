import { useEffect, useState } from "react";
import { Image, View } from "react-native";
import Constants from "expo-constants";
import { getAccess } from "@/auth/secureStore";

const baseURL =
  (Constants.expoConfig?.extra?.apiBaseUrl as string) ?? "http://localhost:7300";

interface Props {
  width?: number;
  height?: number;
}

// Renders the current user's signature PNG via authenticated <Image> request.
// Mounting is the cache-bust trigger — re-uploads call invalidateQueries
// (which triggers a remount of the parent), and the timestamp changes the URL
// so RN's image cache fetches fresh bytes.
export function SignaturePreview({ width = 200, height = 80 }: Props) {
  const [token, setToken] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      const t = await getAccess();
      if (!cancelled) setToken(t);
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  if (!token) return null;

  return (
    <View>
      <Image
        source={{
          uri: `${baseURL}/api/profile/signature?_=${Date.now()}`,
          headers: { Authorization: `Bearer ${token}` },
        }}
        style={{ width, height, resizeMode: "contain" }}
      />
    </View>
  );
}
