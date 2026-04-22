import { useState } from "react";
import { ActivityIndicator, Pressable, Text, type PressableProps } from "react-native";
import { MotiView } from "moti";
import * as Haptics from "expo-haptics";

type Variant = "primary" | "secondary" | "danger" | "ghost";

interface Props extends Omit<PressableProps, "style"> {
  title: string;
  variant?: Variant;
  loading?: boolean;
}

const bg: Record<Variant, string> = {
  primary: "#1e40af",
  secondary: "#ffffff",
  danger: "#dc2626",
  ghost: "transparent",
};

const fg: Record<Variant, string> = {
  primary: "#ffffff",
  secondary: "#0f172a",
  danger: "#ffffff",
  ghost: "#1e40af",
};

export function Button({
  title,
  variant = "primary",
  loading,
  disabled,
  onPress,
  ...rest
}: Props) {
  const [pressed, setPressed] = useState(false);
  const isDisabled = disabled || loading;

  return (
    <Pressable
      {...rest}
      disabled={isDisabled}
      onPressIn={() => {
        setPressed(true);
        if (!isDisabled) Haptics.selectionAsync();
      }}
      onPressOut={() => setPressed(false)}
      onPress={onPress}
    >
      <MotiView
        animate={{ scale: pressed ? 0.97 : 1 }}
        transition={{ type: "spring", damping: 15, stiffness: 300 }}
        style={{
          backgroundColor: bg[variant],
          borderRadius: 10,
          paddingVertical: 13,
          paddingHorizontal: 16,
          alignItems: "center",
          justifyContent: "center",
          opacity: isDisabled ? 0.5 : 1,
          borderWidth: variant === "secondary" ? 1 : 0,
          borderColor: "#e2e8f0",
          shadowColor: variant === "primary" ? "#1e40af" : "#000",
          shadowOffset: { width: 0, height: 2 },
          shadowOpacity: variant === "primary" ? 0.25 : 0,
          shadowRadius: 4,
          elevation: variant === "primary" ? 3 : 0,
        }}
      >
        {loading ? (
          <ActivityIndicator color={fg[variant]} />
        ) : (
          <Text style={{ color: fg[variant], fontSize: 15, fontWeight: "600" }}>
            {title}
          </Text>
        )}
      </MotiView>
    </Pressable>
  );
}
