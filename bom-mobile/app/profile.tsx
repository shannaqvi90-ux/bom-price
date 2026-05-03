import { ScrollView, Text, View } from "react-native";
import { Stack, useRouter } from "expo-router";
import { Button } from "@/components/Button";
import { ScreenHeader } from "@/components/ScreenHeader";
import { useAuth } from "@/auth/AuthContext";
import { ProfileSignatureSection } from "@/features/profile/ProfileSignatureSection";

export default function Profile() {
  const { user, logout } = useAuth();
  const router = useRouter();

  const onLogout = async () => {
    await logout();
    router.replace("/login");
  };

  return (
    <View style={{ flex: 1, backgroundColor: "#f8fafc" }}>
      <Stack.Screen options={{ headerShown: false }} />
      <ScreenHeader title="Profile" back />
      <ScrollView contentContainerStyle={{ padding: 24 }}>
        <View className="bg-white rounded-md p-4 mb-4 border border-slate-200">
          <Text className="text-sm text-slate-500">Name</Text>
          <Text className="text-base text-slate-900 mb-2">{user?.name ?? "-"}</Text>
          <Text className="text-sm text-slate-500">Role</Text>
          <Text className="text-base text-slate-900 mb-2">{user?.role ?? "-"}</Text>
          <Text className="text-sm text-slate-500">Branch</Text>
          <Text className="text-base text-slate-900">
            {user?.branchId != null ? `#${user.branchId}` : "All branches"}
          </Text>
        </View>
        {user?.role === "ManagingDirector" && (
          <View className="bg-white rounded-md mb-4 border border-slate-200">
            <ProfileSignatureSection />
          </View>
        )}
        <Button title="Log out" variant="danger" onPress={onLogout} />
      </ScrollView>
    </View>
  );
}
