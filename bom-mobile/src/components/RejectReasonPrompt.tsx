import { useState } from "react";
import { KeyboardAvoidingView, Modal, Platform, Text, TextInput, View } from "react-native";
import { Button } from "./Button";

interface Props {
  visible: boolean;
  loading?: boolean;
  onConfirm: (notes: string) => void;
  onCancel: () => void;
}

export function RejectReasonPrompt({ visible, loading, onConfirm, onCancel }: Props) {
  const [notes, setNotes] = useState("");
  const [error, setError] = useState<string | null>(null);

  const submit = () => {
    const trimmed = notes.trim();
    if (trimmed.length < 1) {
      setError("Rejection reason is required");
      return;
    }
    if (trimmed.length > 2000) {
      setError("Must be 2000 characters or fewer");
      return;
    }
    setError(null);
    onConfirm(trimmed);
  };

  const cancel = () => {
    setNotes("");
    setError(null);
    onCancel();
  };

  return (
    <Modal transparent visible={visible} animationType="fade" onRequestClose={cancel}>
      <KeyboardAvoidingView
        behavior={Platform.OS === "ios" ? "padding" : undefined}
        className="flex-1 bg-black/50 items-center justify-center p-6"
      >
        <View className="bg-white rounded-lg p-5 w-full max-w-md">
          <Text className="text-lg font-bold text-slate-900 mb-1">Reject requisition</Text>
          <Text className="text-sm text-slate-600 mb-3">
            Please explain why. The sales person will see this.
          </Text>
          <TextInput
            value={notes}
            onChangeText={setNotes}
            placeholder="Reason..."
            multiline
            numberOfLines={4}
            placeholderTextColor="#94a3b8"
            className={`border rounded-md px-3 py-2 text-base text-slate-900 bg-white min-h-[100px] ${
              error ? "border-rose-500" : "border-slate-300"
            }`}
            textAlignVertical="top"
          />
          {error ? (
            <Text className="text-xs text-rose-600 mt-1">{error}</Text>
          ) : null}
          <View className="flex-row justify-end mt-4">
            <View className="mr-2">
              <Button title="Cancel" variant="secondary" onPress={cancel} disabled={loading} />
            </View>
            <Button
              title="Reject"
              variant="danger"
              onPress={submit}
              loading={loading}
            />
          </View>
        </View>
      </KeyboardAvoidingView>
    </Modal>
  );
}
