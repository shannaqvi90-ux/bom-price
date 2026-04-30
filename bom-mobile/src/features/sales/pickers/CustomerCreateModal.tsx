import { useState } from "react";
import { Modal, View, Text, TextInput, Pressable, KeyboardAvoidingView, Platform, ActivityIndicator } from "react-native";
import { useCreateCustomer, type CustomerLite } from "../../../api/customers";
import { theme } from "../../../theme";

interface Props {
  visible: boolean;
  onCreated: (customer: CustomerLite) => void;
  onClose: () => void;
}

export function CustomerCreateModal({ visible, onCreated, onClose }: Props) {
  const [name, setName] = useState("");
  const [email, setEmail] = useState("");
  const [phoneNumber, setPhoneNumber] = useState("");
  const [address, setAddress] = useState("");
  const create = useCreateCustomer();

  const reset = () => { setName(""); setEmail(""); setPhoneNumber(""); setAddress(""); };
  const submit = async () => {
    if (!name.trim()) return;
    const created = await create.mutateAsync({
      name: name.trim(),
      email: email || undefined,
      phoneNumber: phoneNumber || undefined,
      address: address || undefined,
    });
    onCreated(created);
    reset();
  };

  return (
    <Modal visible={visible} animationType="slide" onRequestClose={onClose}>
      <KeyboardAvoidingView style={{ flex: 1 }} behavior={Platform.OS === "ios" ? "padding" : undefined}>
        <View style={{ flexDirection: "row", padding: 16, borderBottomWidth: 1, borderColor: "#e5e7eb", justifyContent: "space-between", alignItems: "center" }}>
          <Pressable onPress={onClose}><Text style={{ color: theme.colors.primary }}>Cancel</Text></Pressable>
          <Text style={{ fontWeight: "600", fontSize: 16 }}>New customer</Text>
          <Pressable onPress={submit} disabled={!name.trim() || create.isPending}>
            {create.isPending ? <ActivityIndicator /> : <Text style={{ color: theme.colors.primary, fontWeight: "600", opacity: name.trim() ? 1 : 0.4 }}>Save</Text>}
          </Pressable>
        </View>
        <View style={{ padding: 16 }}>
          <FormField label="Name *" value={name} onChange={setName} />
          <FormField label="Email" value={email} onChange={setEmail} keyboardType="email-address" />
          <FormField label="Phone" value={phoneNumber} onChange={setPhoneNumber} keyboardType="phone-pad" />
          <FormField label="Address" value={address} onChange={setAddress} multiline />
        </View>
      </KeyboardAvoidingView>
    </Modal>
  );
}

function FormField({ label, value, onChange, keyboardType, multiline }: {
  label: string; value: string; onChange: (s: string) => void;
  keyboardType?: "email-address" | "phone-pad"; multiline?: boolean;
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
