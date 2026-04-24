import { useEffect } from "react";
import {
  KeyboardAvoidingView,
  Modal,
  Platform,
  Pressable,
  ScrollView,
  Text,
  useWindowDimensions,
  View,
} from "react-native";
import { MotiView } from "moti";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import * as Haptics from "expo-haptics";
import { useForm, Controller, type Resolver } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { Input } from "@/components/Input";
import { Button } from "@/components/Button";
import { ErrorBanner } from "@/components/ErrorBanner";
import { useCreateCustomer } from "@/api/customers";
import type { Customer, CreateCustomerRequest } from "@/types/api";
import { createCustomerSchema } from "@/utils/validation";

interface Props {
  open: boolean;
  onClose: () => void;
  onCreated: (customer: Customer) => void;
}

export function CustomerQuickCreateSheet({ open, onClose, onCreated }: Props) {
  const createMut = useCreateCustomer();
  const { height } = useWindowDimensions();
  const insets = useSafeAreaInsets();

  const {
    control,
    handleSubmit,
    reset,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<CreateCustomerRequest>({
    resolver: zodResolver(createCustomerSchema) as Resolver<CreateCustomerRequest>,
    defaultValues: { code: "", name: "", address: "", email: "", phoneNumber: "" },
  });

  useEffect(() => {
    if (!open) reset();
  }, [open, reset]);

  const onSubmit = handleSubmit(async (values) => {
    try {
      const created = await createMut.mutateAsync(values);
      await Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
      onCreated(created);
      onClose();
    } catch (e: unknown) {
      const status = (e as { response?: { status?: number } }).response?.status;
      if (status === 409) {
        setError("code", { type: "server", message: "Code already exists" });
      } else {
        const msg =
          (e as { response?: { data?: { message?: string } } }).response?.data?.message ??
          (e instanceof Error ? e.message : "Failed to add customer");
        setError("root", { type: "server", message: msg });
      }
    }
  });

  const rootError = (errors as { root?: { message?: string } }).root?.message;

  return (
    <Modal visible={open} transparent animationType="none" onRequestClose={onClose}>
      {/* Backdrop */}
      <Pressable
        style={{ flex: 1, backgroundColor: "rgba(0,0,0,0.45)" }}
        onPress={onClose}
      />

      {/* Sheet */}
      <MotiView
        from={{ translateY: height }}
        animate={{ translateY: 0 }}
        transition={{ type: "spring", damping: 20, stiffness: 220 }}
        style={{
          position: "absolute",
          bottom: 0,
          left: 0,
          right: 0,
          maxHeight: height * 0.88,
          backgroundColor: "#ffffff",
          borderTopLeftRadius: 24,
          borderTopRightRadius: 24,
          paddingBottom: insets.bottom,
        }}
      >
        {/* Drag handle */}
        <View style={{ alignItems: "center", paddingTop: 10, paddingBottom: 6 }}>
          <View
            style={{
              width: 42,
              height: 5,
              borderRadius: 999,
              backgroundColor: "#cbd5e1",
            }}
          />
        </View>

        {/* Header */}
        <View
          style={{
            flexDirection: "row",
            alignItems: "center",
            paddingHorizontal: 16,
            paddingTop: 4,
            paddingBottom: 12,
            borderBottomWidth: 1,
            borderBottomColor: "#e2e8f0",
          }}
        >
          <View style={{ flex: 1 }}>
            <Text style={{ fontSize: 22, fontWeight: "700", color: "#0f172a", letterSpacing: -0.3 }}>
              Add customer
            </Text>
            <Text style={{ fontSize: 13, color: "#64748b", marginTop: 2 }}>
              Fill the fields below, save to auto-select.
            </Text>
          </View>
          <Pressable
            onPress={onClose}
            hitSlop={8}
            style={{
              width: 34,
              height: 34,
              borderRadius: 17,
              backgroundColor: "#f1f5f9",
              alignItems: "center",
              justifyContent: "center",
            }}
          >
            <Text style={{ fontSize: 18, color: "#475569", fontWeight: "600" }}>✕</Text>
          </Pressable>
        </View>

        {/* Body */}
        <KeyboardAvoidingView
          behavior={Platform.OS === "ios" ? "padding" : undefined}
          keyboardVerticalOffset={10}
        >
          <ScrollView
            contentContainerStyle={{ paddingHorizontal: 16, paddingTop: 14, paddingBottom: 8 }}
            showsVerticalScrollIndicator={false}
            keyboardShouldPersistTaps="handled"
          >
            {rootError ? <ErrorBanner message={rootError} /> : null}

            <Controller
              control={control}
              name="code"
              render={({ field }) => (
                <Input
                  label="Code"
                  value={field.value}
                  onChangeText={field.onChange}
                  error={errors.code?.message}
                />
              )}
            />
            <Controller
              control={control}
              name="name"
              render={({ field }) => (
                <Input
                  label="Name"
                  value={field.value}
                  onChangeText={field.onChange}
                  error={errors.name?.message}
                />
              )}
            />
            <Controller
              control={control}
              name="address"
              render={({ field }) => (
                <Input
                  label="Address"
                  value={field.value}
                  onChangeText={field.onChange}
                  error={errors.address?.message}
                />
              )}
            />
            <Controller
              control={control}
              name="email"
              render={({ field }) => (
                <Input
                  label="Email"
                  keyboardType="email-address"
                  autoCapitalize="none"
                  value={field.value}
                  onChangeText={field.onChange}
                  error={errors.email?.message}
                />
              )}
            />
            <Controller
              control={control}
              name="phoneNumber"
              render={({ field }) => (
                <Input
                  label="Phone"
                  keyboardType="phone-pad"
                  value={field.value}
                  onChangeText={field.onChange}
                  error={errors.phoneNumber?.message}
                />
              )}
            />
          </ScrollView>
        </KeyboardAvoidingView>

        {/* Sticky action row */}
        <View
          style={{
            flexDirection: "row",
            gap: 10,
            paddingHorizontal: 16,
            paddingTop: 10,
            paddingBottom: 14,
            borderTopWidth: 1,
            borderTopColor: "#e2e8f0",
            backgroundColor: "#ffffff",
          }}
        >
          <View style={{ flex: 1 }}>
            <Button title="Cancel" variant="ghost" onPress={onClose} />
          </View>
          <View style={{ flex: 2 }}>
            <Button
              title="Save"
              onPress={onSubmit}
              loading={isSubmitting || createMut.isPending}
            />
          </View>
        </View>
      </MotiView>
    </Modal>
  );
}
