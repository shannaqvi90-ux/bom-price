import { Modal, Text, View } from "react-native";
import { Button } from "./Button";

interface Props {
  visible: boolean;
  title: string;
  message: string;
  confirmLabel?: string;
  cancelLabel?: string;
  destructive?: boolean;
  loading?: boolean;
  onConfirm: () => void;
  onCancel: () => void;
}

export function ConfirmDialog({
  visible,
  title,
  message,
  confirmLabel = "Confirm",
  cancelLabel = "Cancel",
  destructive,
  loading,
  onConfirm,
  onCancel,
}: Props) {
  return (
    <Modal
      transparent
      visible={visible}
      animationType="fade"
      onRequestClose={onCancel}
    >
      <View className="flex-1 bg-black/50 items-center justify-center p-6">
        <View className="bg-white rounded-lg p-5 w-full max-w-md">
          <Text className="text-lg font-bold text-slate-900 mb-2">{title}</Text>
          <Text className="text-sm text-slate-700 mb-5">{message}</Text>
          <View className="flex-row justify-end">
            <View className="mr-2">
              <Button
                title={cancelLabel}
                variant="secondary"
                onPress={onCancel}
                disabled={loading}
              />
            </View>
            <Button
              title={confirmLabel}
              variant={destructive ? "danger" : "primary"}
              onPress={onConfirm}
              loading={loading}
            />
          </View>
        </View>
      </View>
    </Modal>
  );
}
