import { Pressable, Text } from "react-native";
import { useRouter } from "expo-router";
import { useAuth } from "@/auth/AuthContext";
import { NotificationBell } from "@/components/NotificationBell";

export function SalesHeaderRight() {
  const { logout } = useAuth();
  const router = useRouter();

  const onLogout = async () => {
    await logout();
    router.replace("/login");
  };

  // Fragment so ScreenHeader's outer `right` View with gap:6 governs spacing.
  return (
    <>
      <NotificationBell />
      <Pressable onPress={onLogout} hitSlop={8} style={{ paddingHorizontal: 6, paddingVertical: 2 }}>
        <Text style={{ color: "#1e40af", fontSize: 15, fontWeight: "600" }}>Log out</Text>
      </Pressable>
    </>
  );
}
