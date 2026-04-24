import { useState } from "react";
import {
  KeyboardAvoidingView,
  Platform,
  Pressable,
  ScrollView,
  Text,
  View,
} from "react-native";
import { useRouter } from "expo-router";
import { useForm, Controller, useFieldArray } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import * as Haptics from "expo-haptics";
import { Button } from "@/components/Button";
import { Input } from "@/components/Input";
import { SearchablePicker } from "@/components/SearchablePicker";
import { ErrorBanner } from "@/components/ErrorBanner";
import { CustomerQuickCreateSheet } from "@/components/CustomerQuickCreateSheet";
import { ScreenHeader } from "@/components/ScreenHeader";
import { SalesHeaderRight } from "@/components/SalesHeaderRight";
import { SectionCard } from "@/components/SectionCard";
import { ItemCardShell } from "@/components/ItemCardShell";
import { useCustomers, useExchangeRates, useItems } from "@/api/lookups";
import { useCreateRequisition } from "@/api/requisitions";
import {
  createRequisitionSchema,
  type CreateRequisitionInput,
} from "@/utils/validation";

export default function NewRequisition() {
  const router = useRouter();
  const customersQ = useCustomers();
  const itemsQ = useItems();
  const ratesQ = useExchangeRates();
  const createMut = useCreateRequisition();
  const [topError, setTopError] = useState<string | null>(null);
  const [addSheetOpen, setAddSheetOpen] = useState(false);

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

  const { fields, append, remove } = useFieldArray({ control, name: "items" });

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
      style={{ flex: 1, backgroundColor: "#f8fafc" }}
    >
      <ScreenHeader title="New requisition" right={<SalesHeaderRight />} />

      <ScrollView
        contentContainerStyle={{ padding: 14, paddingBottom: 32 }}
        keyboardShouldPersistTaps="handled"
      >
        {topError ? (
          <ErrorBanner message={topError} onRetry={() => setTopError(null)} />
        ) : null}

        <SectionCard title="Customer">
          <Controller
            control={control}
            name="customerId"
            render={({ field }) => (
              <View>
                <SearchablePicker
                  label=""
                  placeholder="Select customer..."
                  value={field.value || null}
                  onChange={field.onChange}
                  loading={customersQ.isPending}
                  options={(customersQ.data ?? []).map((c) => ({
                    id: c.id,
                    label: c.name,
                    sublabel: c.code,
                  }))}
                  error={errors.customerId?.message}
                />
                <Pressable
                  onPress={async () => {
                    await Haptics.selectionAsync();
                    setAddSheetOpen(true);
                  }}
                  style={{ alignSelf: "flex-start", marginTop: 4 }}
                >
                  <Text style={{ color: "#1e40af", fontSize: 14, fontWeight: "600" }}>
                    + New customer
                  </Text>
                </Pressable>
                <CustomerQuickCreateSheet
                  open={addSheetOpen}
                  onClose={() => setAddSheetOpen(false)}
                  onCreated={(c) => {
                    field.onChange(c.id);
                  }}
                />
              </View>
            )}
          />
        </SectionCard>

        <SectionCard title="Currency">
          <Controller
            control={control}
            name="currencyCode"
            render={({ field }) => (
              <View>
                <View style={{ flexDirection: "row", flexWrap: "wrap", marginRight: -8 }}>
                  {currencyOptions.map((opt) => {
                    const selected = field.value === opt.code;
                    return (
                      <Pressable
                        key={opt.code}
                        onPress={async () => {
                          await Haptics.selectionAsync();
                          field.onChange(opt.code);
                        }}
                        style={{
                          paddingHorizontal: 14,
                          paddingVertical: 8,
                          marginRight: 8,
                          marginBottom: 8,
                          borderRadius: 8,
                          borderWidth: 1,
                          backgroundColor: selected ? "#1e40af" : "#ffffff",
                          borderColor: selected ? "#1e40af" : "#cbd5e1",
                        }}
                      >
                        <Text
                          style={{
                            color: selected ? "#ffffff" : "#334155",
                            fontSize: 14,
                            fontWeight: "600",
                          }}
                        >
                          {opt.code}
                        </Text>
                      </Pressable>
                    );
                  })}
                </View>
                {errors.currencyCode ? (
                  <Text style={{ color: "#be123c", fontSize: 12, marginTop: 4 }}>
                    {errors.currencyCode.message}
                  </Text>
                ) : null}
              </View>
            )}
          />
        </SectionCard>

        <Text
          style={{
            fontSize: 13,
            fontWeight: "700",
            color: "#64748b",
            marginBottom: 8,
            marginTop: 4,
            letterSpacing: 0.3,
          }}
        >
          {`ITEMS (${fields.length})`}
        </Text>

        {fields.map((f, idx) => (
          <ItemCardShell key={f.id}>
            <View
              style={{
                flexDirection: "row",
                alignItems: "center",
                justifyContent: "space-between",
                marginBottom: 8,
              }}
            >
              <Text style={{ fontSize: 14, fontWeight: "600", color: "#334155" }}>
                Item {idx + 1}
              </Text>
              {fields.length > 1 ? (
                <Pressable
                  onPress={async () => {
                    await Haptics.selectionAsync();
                    remove(idx);
                  }}
                  hitSlop={6}
                >
                  <Text style={{ color: "#be123c", fontSize: 14, fontWeight: "600" }}>
                    Remove
                  </Text>
                </Pressable>
              ) : null}
            </View>

            <Controller
              control={control}
              name={`items.${idx}.itemId` as const}
              render={({ field }) => (
                <SearchablePicker
                  label="Item"
                  placeholder="Select item..."
                  value={field.value || null}
                  onChange={field.onChange}
                  loading={itemsQ.isPending}
                  options={(itemsQ.data ?? []).map((it) => ({
                    id: it.id,
                    label: it.description,
                    sublabel: it.code,
                  }))}
                  error={errors.items?.[idx]?.itemId?.message}
                />
              )}
            />

            <Controller
              control={control}
              name={`items.${idx}.expectedQty` as const}
              render={({ field }) => (
                <Input
                  label="Expected Qty"
                  keyboardType="decimal-pad"
                  value={field.value ? String(field.value) : ""}
                  onChangeText={(t) => {
                    const n = Number(t);
                    field.onChange(Number.isFinite(n) ? n : 0);
                  }}
                  error={errors.items?.[idx]?.expectedQty?.message}
                />
              )}
            />
          </ItemCardShell>
        ))}

        <Pressable
          onPress={async () => {
            await Haptics.selectionAsync();
            append({ itemId: 0, expectedQty: 0 });
          }}
          style={{ alignSelf: "flex-start", marginBottom: 4 }}
        >
          <Text style={{ color: "#1e40af", fontSize: 14, fontWeight: "600" }}>
            + Add another item
          </Text>
        </Pressable>

        {errors.items?.root ? (
          <Text style={{ color: "#be123c", fontSize: 12, marginBottom: 8 }}>
            {errors.items.root.message}
          </Text>
        ) : null}

        <View style={{ marginTop: 20 }}>
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
