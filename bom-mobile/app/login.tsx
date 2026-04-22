import { useState } from "react";
import {
  Alert,
  Image,
  KeyboardAvoidingView,
  Platform,
  ScrollView,
  Text,
  View,
} from "react-native";
import { useRouter } from "expo-router";
import { useForm, Controller } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { MotiView } from "moti";
import * as Haptics from "expo-haptics";
import { Button } from "@/components/Button";
import { Input } from "@/components/Input";
import { loginSchema, type LoginInput } from "@/utils/validation";
import { useAuth } from "@/auth/AuthContext";

const ALLOWED_ROLES = ["SalesPerson", "ManagingDirector"] as const;

export default function Login() {
  const { login } = useAuth();
  const router = useRouter();
  const [submitting, setSubmitting] = useState(false);
  const [shakeKey, setShakeKey] = useState(0);
  const { control, handleSubmit, formState: { errors } } = useForm<LoginInput>({
    resolver: zodResolver(loginSchema),
    defaultValues: { email: "", password: "" },
  });

  const onSubmit = handleSubmit(async (values) => {
    setSubmitting(true);
    try {
      const u = await login(values.email, values.password);
      if (!ALLOWED_ROLES.includes(u.role as typeof ALLOWED_ROLES[number])) {
        Haptics.notificationAsync(Haptics.NotificationFeedbackType.Warning);
        Alert.alert("Not allowed", "This app is for Sales and Management only.");
        return;
      }
      Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
      router.replace("/");
    } catch (e: unknown) {
      Haptics.notificationAsync(Haptics.NotificationFeedbackType.Error);
      setShakeKey((k) => k + 1);
      const msg = e instanceof Error ? e.message : "Login failed";
      Alert.alert("Login failed", msg);
    } finally {
      setSubmitting(false);
    }
  });

  return (
    <KeyboardAvoidingView
      behavior={Platform.OS === "ios" ? "padding" : undefined}
      style={{ flex: 1, backgroundColor: "#ffffff" }}
    >
      <ScrollView
        contentContainerStyle={{ flexGrow: 1, justifyContent: "center", padding: 24 }}
        keyboardShouldPersistTaps="handled"
      >
        <MotiView
          from={{ opacity: 0, translateY: 14 }}
          animate={{ opacity: 1, translateY: 0 }}
          transition={{ type: "spring", damping: 14, stiffness: 140 }}
          style={{ marginBottom: 28, alignSelf: "flex-start" }}
        >
          <Image
            source={require("../assets/fpf-logo.png")}
            style={{ width: 220, height: 108 }}
            resizeMode="contain"
          />
        </MotiView>

        <MotiView
          from={{ opacity: 0, translateY: -6 }}
          animate={{ opacity: 1, translateY: 0 }}
          transition={{ type: "timing", duration: 400, delay: 150 }}
        >
          <Text
            style={{
              fontSize: 24,
              fontWeight: "700",
              color: "#0f172a",
              letterSpacing: -0.5,
            }}
          >
            Welcome back
          </Text>
          <Text style={{ fontSize: 13, color: "#64748b", marginTop: 2, marginBottom: 28 }}>
            Sign in to FPF Quotations
          </Text>
        </MotiView>

        <MotiView
          key={shakeKey}
          from={{ translateX: 0 }}
          animate={
            shakeKey > 0
              ? { translateX: [0, -8, 8, -8, 0] }
              : { translateX: 0 }
          }
          transition={{ type: "timing", duration: 400 }}
        >
          <MotiView
            from={{ opacity: 0, translateY: 12 }}
            animate={{ opacity: 1, translateY: 0 }}
            transition={{ type: "timing", duration: 400, delay: 250 }}
          >
            <Controller
              control={control}
              name="email"
              render={({ field }) => (
                <Input
                  label="Email"
                  keyboardType="email-address"
                  autoCapitalize="none"
                  autoComplete="email"
                  value={field.value}
                  onChangeText={field.onChange}
                  error={errors.email?.message}
                />
              )}
            />
          </MotiView>

          <MotiView
            from={{ opacity: 0, translateY: 12 }}
            animate={{ opacity: 1, translateY: 0 }}
            transition={{ type: "timing", duration: 400, delay: 330 }}
          >
            <Controller
              control={control}
              name="password"
              render={({ field }) => (
                <Input
                  label="Password"
                  secureTextEntry
                  value={field.value}
                  onChangeText={field.onChange}
                  error={errors.password?.message}
                />
              )}
            />
          </MotiView>
        </MotiView>

        <MotiView
          from={{ opacity: 0, translateY: 12 }}
          animate={{ opacity: 1, translateY: 0 }}
          transition={{ type: "spring", damping: 14, stiffness: 140, delay: 410 }}
          style={{ marginTop: 6 }}
        >
          <Button title="Sign in" onPress={onSubmit} loading={submitting} />
        </MotiView>
      </ScrollView>
    </KeyboardAvoidingView>
  );
}
