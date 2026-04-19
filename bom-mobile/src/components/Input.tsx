import { Text, TextInput, View, type TextInputProps } from "react-native";

interface Props extends TextInputProps {
  label: string;
  error?: string;
}

export function Input({ label, error, ...rest }: Props) {
  return (
    <View className="mb-3">
      <Text className="text-sm text-slate-700 mb-1">{label}</Text>
      <TextInput
        {...rest}
        className={`border rounded-md px-3 py-2 text-base text-slate-900 bg-white ${error ? "border-rose-500" : "border-slate-300"}`}
        placeholderTextColor="#94a3b8"
      />
      {error ? <Text className="text-xs text-rose-600 mt-1">{error}</Text> : null}
    </View>
  );
}
