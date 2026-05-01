// bom-mobile/src/features/sales/pickers/RmCreateModal.tsx
import { useState } from "react";
import { Modal, View, Text, TextInput, Pressable, KeyboardAvoidingView, Platform, ActivityIndicator } from "react-native";
import { useCreateItem } from "../../../api/items";
import { theme } from "../../../theme";
import type { Item } from "@/types/api";

interface Props {
  visible: boolean;
  onCreated: (item: Item) => void;
  onClose: () => void;
}

export function RmCreateModal({ visible, onCreated, onClose }: Props) {
  const [description, setDescription] = useState("");
  const [lastPurchasePrice, setLastPurchasePrice] = useState("");
  const create = useCreateItem();

  const reset = () => { setDescription(""); setLastPurchasePrice(""); };
  const submit = async () => {
    if (!description.trim()) return;
    const priceNum = lastPurchasePrice.trim() ? Number(lastPurchasePrice) : null;
    if (priceNum !== null && Number.isNaN(priceNum)) return;
    const created = await create.mutateAsync({
      description: description.trim(),
      type: "RawMaterial",
      lastPurchasePrice: priceNum,
    });
    onCreated(created);
    reset();
  };

  return (
    <Modal visible={visible} animationType="slide" onRequestClose={onClose}>
      <KeyboardAvoidingView style={{ flex: 1 }} behavior={Platform.OS === "ios" ? "padding" : undefined}>
        <View style={{ flexDirection: "row", padding: 16, borderBottomWidth: 1, borderColor: "#e5e7eb", justifyContent: "space-between", alignItems: "center" }}>
          <Pressable onPress={onClose}><Text style={{ color: theme.colors.primary }}>Cancel</Text></Pressable>
          <Text style={{ fontWeight: "600", fontSize: 16 }}>New raw material</Text>
          <Pressable onPress={submit} disabled={!description.trim() || create.isPending}>
            {create.isPending ? <ActivityIndicator /> : <Text style={{ color: theme.colors.primary, fontWeight: "600", opacity: description.trim() ? 1 : 0.4 }}>Save</Text>}
          </Pressable>
        </View>
        <View style={{ padding: 16 }}>
          <FormField label="Description *" value={description} onChange={setDescription} multiline />
          <FormField label="Last purchase price (AED)" value={lastPurchasePrice} onChange={setLastPurchasePrice} keyboardType="decimal-pad" />
        </View>
      </KeyboardAvoidingView>
    </Modal>
  );
}

function FormField({ label, value, onChange, keyboardType, multiline }: {
  label: string; value: string; onChange: (s: string) => void;
  keyboardType?: "decimal-pad"; multiline?: boolean;
}) {
  return (
    <View style={{ marginTop: 12 }}>
      <Text style={{ fontSize: 12, color: "#64748b", textTransform: "uppercase", fontWeight: "600", letterSpacing: 0.5 }}>{label}</Text>
      <TextInput
        value={value}
        onChangeText={onChange}
        keyboardType={keyboardType}
        multiline={multiline}
        style={{
          marginTop: 4, padding: 10, borderRadius: 8,
          backgroundColor: "#f1f5f9", fontSize: 14,
          minHeight: multiline ? 80 : undefined,
          textAlignVertical: multiline ? "top" : "center",
        }}
      />
    </View>
  );
}
