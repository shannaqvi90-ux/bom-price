import { useState } from "react";
import {
  KeyboardAvoidingView,
  Platform,
  ScrollView,
  Text,
  View,
} from "react-native";
import { useRouter } from "expo-router";
import { useForm, Controller } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { Button } from "@/components/Button";
import { SearchablePicker } from "@/components/SearchablePicker";
import { ErrorBanner } from "@/components/ErrorBanner";
import { useCustomers, useExchangeRates } from "@/api/lookups";
import { useCreateRequisition } from "@/api/requisitions";
import {
  createRequisitionSchema,
  type CreateRequisitionInput,
} from "@/utils/validation";

export default function NewRequisition() {
  const router = useRouter();
  const customersQ = useCustomers();
  const ratesQ = useExchangeRates();
  const createMut = useCreateRequisition();
  const [topError, setTopError] = useState<string | null>(null);

  const {
    control,
    handleSubmit,
    formState: { errors },
  } = useForm<CreateRequisitionInput>({
    resolver: zodResolver(createRequisitionSchema),
    defaultValues: {
      customerId: 0,
      currencyCode: "AED",
      items: [{ itemId: 0, expectedQty: 0 }],
    },
  });

  const currencyOptions = [
    { code: "AED", label: "AED — UAE Dirham" },
    ...(ratesQ.data ?? []).map((r) => ({
      code: r.currencyCode,
      label: `${r.currencyCode} — ${r.currencyName}`,
    })),
  ];

  const onSubmit = handleSubmit(async (values) => {
    setTopError(null);
    try {
      const created = await createMut.mutateAsync(values);
      router.replace(`/(sales)/${created.id}`);
    } catch (e: unknown) {
      const msg =
        (e as { response?: { data?: { message?: string } } }).response?.data?.message ??
        (e instanceof Error ? e.message : "Failed to create requisition");
      setTopError(msg);
    }
  });

  return (
    <KeyboardAvoidingView
      behavior={Platform.OS === "ios" ? "padding" : undefined}
      className="flex-1 bg-slate-50"
    >
      <ScrollView contentContainerClassName="p-4">
        <Text className="text-xl font-bold text-slate-900 mb-4">New requisition</Text>

        {topError ? (
          <ErrorBanner message={topError} onRetry={() => setTopError(null)} />
        ) : null}

        <Controller
          control={control}
          name="customerId"
          render={({ field }) => (
            <SearchablePicker
              label="Customer"
              placeholder="Select customer..."
              value={field.value || null}
              onChange={field.onChange}
              loading={customersQ.isPending}
              options={
                (customersQ.data ?? []).map((c) => ({
                  id: c.id,
                  label: c.name,
                  sublabel: c.code,
                }))
              }
              error={errors.customerId?.message}
            />
          )}
        />

        <Controller
          control={control}
          name="currencyCode"
          render={({ field }) => (
            <View className="mb-3">
              <Text className="text-sm text-slate-700 mb-1">Currency</Text>
              <View className="flex-row flex-wrap -mr-2">
                {currencyOptions.map((opt) => {
                  const selected = field.value === opt.code;
                  return (
                    <Text
                      key={opt.code}
                      onPress={() => field.onChange(opt.code)}
                      className={`px-3 py-2 mr-2 mb-2 rounded-md border ${
                        selected
                          ? "bg-brand-600 border-brand-600 text-white"
                          : "bg-white border-slate-300 text-slate-700"
                      }`}
                    >
                      {opt.code}
                    </Text>
                  );
                })}
              </View>
              {errors.currencyCode ? (
                <Text className="text-xs text-rose-600 mt-1">
                  {errors.currencyCode.message}
                </Text>
              ) : null}
            </View>
          )}
        />

        {/* Items — populated in Task 9 */}
        <Text className="text-base font-semibold text-slate-900 mt-4 mb-2">Items</Text>
        <Text className="text-slate-500 text-sm">Items editor added in next task.</Text>

        <View className="mt-6">
          <Button
            title="Create requisition"
            onPress={onSubmit}
            loading={createMut.isPending}
          />
        </View>
      </ScrollView>
    </KeyboardAvoidingView>
  );
}
