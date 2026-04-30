import { useState } from "react";
import { View, Text, Pressable, TextInput } from "react-native";
import { CustomerPickerSheet } from "../pickers/CustomerPickerSheet";
import { CustomerCreateModal } from "../pickers/CustomerCreateModal";
import type { CustomerLite } from "../../../api/customers";
import { theme } from "../../../theme";

interface Props {
  customer: CustomerLite | null;
  setCustomer: (c: CustomerLite) => void;
  currency: string;
  setCurrency: (c: string) => void;
  reference: string;
  setReference: (s: string) => void;
  notes: string;
  setNotes: (s: string) => void;
}

const CURRENCIES = ["AED", "USD", "EUR", "GBP", "SAR"];

export function HeaderSection({
  customer,
  setCustomer,
  currency,
  setCurrency,
  reference,
  setReference,
  notes,
  setNotes,
}: Props) {
  const [pickerOpen, setPickerOpen] = useState(false);
  const [createOpen, setCreateOpen] = useState(false);

  return (
    <View
      style={{
        padding: 14,
        backgroundColor: "white",
        borderRadius: 12,
        margin: 12,
        borderWidth: 1,
        borderColor: "#e5e7eb",
      }}
    >
      {/* Customer picker */}
      <Pressable onPress={() => setPickerOpen(true)}>
        <Text
          style={{
            fontSize: 11,
            color: "#64748b",
            textTransform: "uppercase",
            fontWeight: "600",
          }}
        >
          Customer
        </Text>
        <Text
          style={{
            marginTop: 4,
            fontSize: 16,
            fontWeight: customer ? "600" : "400",
            color: customer ? "#0f172a" : "#94a3b8",
          }}
        >
          {customer ? customer.name : "Tap to pick…"}
        </Text>
      </Pressable>

      {/* Currency chips */}
      <View style={{ flexDirection: "row", gap: 8, marginTop: 12 }}>
        {CURRENCIES.map((c) => (
          <Pressable
            key={c}
            onPress={() => setCurrency(c)}
            style={{
              paddingHorizontal: 10,
              paddingVertical: 6,
              borderRadius: 6,
              backgroundColor:
                currency === c ? theme.colors.primary : "#f1f5f9",
            }}
          >
            <Text
              style={{
                color: currency === c ? "white" : "#0f172a",
                fontWeight: "500",
                fontSize: 12,
              }}
            >
              {c}
            </Text>
          </Pressable>
        ))}
      </View>

      {/* Reference field */}
      <View style={{ marginTop: 12 }}>
        <Text
          style={{
            fontSize: 11,
            color: "#64748b",
            textTransform: "uppercase",
            fontWeight: "600",
          }}
        >
          Reference (optional)
        </Text>
        <TextInput
          value={reference}
          onChangeText={setReference}
          placeholder="e.g., PO-2026-001"
          style={{
            marginTop: 4,
            padding: 8,
            backgroundColor: "#f8fafc",
            borderRadius: 6,
            fontSize: 14,
            color: "#0f172a",
          }}
        />
      </View>

      {/* Notes field */}
      <View style={{ marginTop: 12 }}>
        <Text
          style={{
            fontSize: 11,
            color: "#64748b",
            textTransform: "uppercase",
            fontWeight: "600",
          }}
        >
          Notes
        </Text>
        <TextInput
          value={notes}
          onChangeText={setNotes}
          multiline
          placeholder="Special instructions or remarks…"
          style={{
            marginTop: 4,
            padding: 8,
            backgroundColor: "#f8fafc",
            borderRadius: 6,
            fontSize: 14,
            minHeight: 60,
            textAlignVertical: "top",
            color: "#0f172a",
          }}
        />
      </View>

      {/* Customer picker sheet */}
      <CustomerPickerSheet
        visible={pickerOpen}
        onPick={(c) => {
          setCustomer(c);
          setPickerOpen(false);
        }}
        onClose={() => setPickerOpen(false)}
        onCreateNew={() => {
          setPickerOpen(false);
          setCreateOpen(true);
        }}
      />

      {/* Customer create modal */}
      <CustomerCreateModal
        visible={createOpen}
        onCreated={(c) => {
          setCustomer(c);
          setCreateOpen(false);
        }}
        onClose={() => setCreateOpen(false)}
      />
    </View>
  );
}
