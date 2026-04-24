import { useState } from "react";
import { Text, TextInput, View, type TextInputProps } from "react-native";
import { MotiView } from "moti";

interface Props extends TextInputProps {
  label: string;
  error?: string;
  marginBottom?: number;
}

export function Input({ label, error, onFocus, onBlur, marginBottom = 14, ...rest }: Props) {
  const [focused, setFocused] = useState(false);
  const borderColor = error
    ? "#dc2626"
    : focused
      ? "#1e40af"
      : "#e2e8f0";

  return (
    <View style={{ marginBottom }}>
      <Text style={{ fontSize: 14, fontWeight: "600", color: "#334155", marginBottom: 6 }}>
        {label}
      </Text>
      <MotiView
        animate={{ borderColor, scale: focused ? 1.005 : 1 }}
        transition={{ type: "timing", duration: 150 }}
        style={{
          borderWidth: 1,
          borderRadius: 10,
          backgroundColor: "#f8fafc",
        }}
      >
        <TextInput
          {...rest}
          onFocus={(e) => {
            setFocused(true);
            onFocus?.(e);
          }}
          onBlur={(e) => {
            setFocused(false);
            onBlur?.(e);
          }}
          placeholderTextColor="#94a3b8"
          style={{
            paddingHorizontal: 12,
            paddingVertical: 11,
            fontSize: 17,
            color: "#0f172a",
          }}
        />
      </MotiView>
      {error ? (
        <Text style={{ color: "#dc2626", fontSize: 13, marginTop: 4 }}>{error}</Text>
      ) : null}
    </View>
  );
}
