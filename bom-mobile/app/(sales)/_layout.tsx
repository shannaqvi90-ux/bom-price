import { Pressable, Text, View } from "react-native";
import { Redirect, Stack, useRouter } from "expo-router";
import { useAuth, useRoleGuard } from "@/auth/AuthContext";
import { LoadingView } from "@/components/LoadingView";
import { NotificationBell } from "@/components/NotificationBell";

function HeaderRight() {
  const { logout } = useAuth();
  const router = useRouter();
  const onLogout = async () => {
    await logout();
    router.replace("/login");
  };
  return (
    <View className="flex-row items-center">
      <NotificationBell />
      <Pressable onPress={onLogout} className="pr-3">
        <Text className="text-brand-600 text-base font-semibold">Log out</Text>
      </Pressable>
    </View>
  );
}

export default function SalesLayout() {
  const { status } = useRoleGuard(["SalesPerson"]);
  if (status === "loading") return <LoadingView />;
  if (status !== "allowed") return <Redirect href="/login" />;
  return (
    <Stack
      screenOptions={{
        headerShown: true,
        headerRight: () => <HeaderRight />,
      }}
    />
  );
}
