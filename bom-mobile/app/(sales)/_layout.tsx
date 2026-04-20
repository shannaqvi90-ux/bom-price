import { Pressable, Text } from "react-native";
import { Redirect, Stack, useRouter } from "expo-router";
import { useAuth, useRoleGuard } from "@/auth/AuthContext";
import { LoadingView } from "@/components/LoadingView";

function HeaderLogout() {
  const { logout } = useAuth();
  const router = useRouter();
  const onPress = async () => {
    await logout();
    router.replace("/login");
  };
  return (
    <Pressable onPress={onPress} className="pr-3">
      <Text className="text-brand-600 text-base font-semibold">Log out</Text>
    </Pressable>
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
        headerRight: () => <HeaderLogout />,
      }}
    />
  );
}
