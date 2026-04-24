import { useEffect } from "react";
import { Modal, View, Text, ScrollView, Platform, KeyboardAvoidingView } from "react-native";
import { MotiView } from "moti";
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

  const {
    control,
    handleSubmit,
    reset,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<CreateCustomerRequest>({
    // cast: schema input has optional fields; defaultValues guarantee all are strings at runtime
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

  return (
    <Modal visible={open} transparent animationType="none" onRequestClose={onClose}>
      <View className="flex-1 justify-end bg-black/40">
        <MotiView
          from={{ translateY: 400 }}
          animate={{ translateY: 0 }}
          transition={{ type: "timing", duration: 220 }}
          className="rounded-t-2xl bg-white"
        >
          <KeyboardAvoidingView behavior={Platform.OS === "ios" ? "padding" : undefined}>
            <ScrollView contentContainerClassName="p-5">
              <Text className="text-lg font-bold text-slate-900 mb-4">Add customer</Text>

              {(errors as { root?: { message?: string } }).root?.message ? (
                <ErrorBanner
                  message={(errors as { root?: { message?: string } }).root!.message!}
                />
              ) : null}

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

              <View className="flex-row gap-3 mt-4">
                <View className="flex-1">
                  <Button title="Cancel" variant="ghost" onPress={onClose} />
                </View>
                <View className="flex-1">
                  <Button
                    title="Save"
                    onPress={onSubmit}
                    loading={isSubmitting || createMut.isPending}
                  />
                </View>
              </View>
            </ScrollView>
          </KeyboardAvoidingView>
        </MotiView>
      </View>
    </Modal>
  );
}
