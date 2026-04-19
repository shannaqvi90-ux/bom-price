import { Text, View } from "react-native";
import { useRouter } from "expo-router";
import { Button } from "@/components/Button";
import { useAuth } from "@/auth/AuthContext";

export default function Profile() {
  const { user, logout } = useAuth();
  const router = useRouter();

  const onLogout = async () => {
    await logout();
    router.replace("/login");
  };

  return (
    <View className="flex-1 p-6 bg-slate-50">
      <Text className="text-2xl font-bold text-slate-900 mb-4">Profile</Text>
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
      <Button title="Log out" variant="danger" onPress={onLogout} />
    </View>
  );
}
