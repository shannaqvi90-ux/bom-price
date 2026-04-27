import { Pressable, ScrollView, Text, View } from "react-native";
import { useRouter } from "expo-router";
import { MotiView } from "moti";
import * as Haptics from "expo-haptics";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { useAuth } from "@/auth/AuthContext";

export default function AdminSplash() {
  const router = useRouter();
  const { user, logout } = useAuth();
  const insets = useSafeAreaInsets();

  const onLogout = async () => {
    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
    await logout();
    router.replace("/login");
  };

  return (
    <ScrollView
      contentContainerStyle={{
        flexGrow: 1,
        paddingTop: insets.top + 24,
        paddingBottom: insets.bottom + 32,
        paddingHorizontal: 20,
        backgroundColor: "#f8fafc",
      }}
    >
      <MotiView
        from={{ opacity: 0, translateY: 8 }}
        animate={{ opacity: 1, translateY: 0 }}
        transition={{ type: "timing", duration: 300 }}
      >
        <Text style={{ fontSize: 12, color: "#64748b", fontWeight: "600", letterSpacing: 1 }}>
          ADMIN
        </Text>
        <Text style={{ fontSize: 28, fontWeight: "700", color: "#0f172a", marginTop: 4 }}>
          Hello, {user?.name ?? "Admin"}
        </Text>
      </MotiView>

      <MotiView
        from={{ opacity: 0, translateY: 12 }}
        animate={{ opacity: 1, translateY: 0 }}
        transition={{ type: "timing", duration: 350, delay: 100 }}
        style={{
          marginTop: 28,
          padding: 20,
          borderRadius: 16,
          backgroundColor: "#ffffff",
          borderWidth: 1,
          borderColor: "#e2e8f0",
        }}
      >
        <Text style={{ fontSize: 18, fontWeight: "700", color: "#0f172a" }}>
          Admin tasks live on the web
        </Text>
        <Text style={{ marginTop: 10, fontSize: 14, lineHeight: 20, color: "#475569" }}>
          The mobile app is built for SalesPersons, Accountants, and the Managing Director — the
          roles that need quotation workflow on the go.
        </Text>
        <Text style={{ marginTop: 10, fontSize: 14, lineHeight: 20, color: "#475569" }}>
          For Admin operations — overrides, hard-deletes, password resets, audit log, user / branch /
          group management — please open the web app from your desktop.
        </Text>

        <View
          style={{
            marginTop: 16,
            padding: 12,
            borderRadius: 10,
            backgroundColor: "#eff6ff",
            borderWidth: 1,
            borderColor: "#dbeafe",
          }}
        >
          <Text style={{ fontSize: 12, fontWeight: "600", color: "#1e40af", marginBottom: 4 }}>
            WEB APP
          </Text>
          <Text style={{ fontSize: 14, color: "#1e3a8a" }} selectable>
            http://localhost:5300 (dev) • production URL TBD
          </Text>
        </View>
      </MotiView>

      <MotiView
        from={{ opacity: 0, translateY: 12 }}
        animate={{ opacity: 1, translateY: 0 }}
        transition={{ type: "timing", duration: 350, delay: 200 }}
        style={{ marginTop: 24 }}
      >
        <Pressable
          onPress={onLogout}
          style={({ pressed }) => ({
            paddingVertical: 14,
            paddingHorizontal: 18,
            borderRadius: 12,
            backgroundColor: pressed ? "#dc2626" : "#ef4444",
            alignItems: "center",
          })}
          accessibilityRole="button"
          accessibilityLabel="Log out"
        >
          <Text style={{ color: "#ffffff", fontSize: 16, fontWeight: "600" }}>Log out</Text>
        </Pressable>
      </MotiView>
    </ScrollView>
  );
}
