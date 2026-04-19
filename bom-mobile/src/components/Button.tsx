import { ActivityIndicator, Pressable, Text, type PressableProps } from "react-native";

type Variant = "primary" | "secondary" | "danger";

interface Props extends PressableProps {
  title: string;
  variant?: Variant;
  loading?: boolean;
}

const variantClasses: Record<Variant, string> = {
  primary: "bg-brand-600 active:bg-brand-700",
  secondary: "bg-white border border-slate-300 active:bg-slate-50",
  danger: "bg-rose-600 active:bg-rose-700",
};

const textClasses: Record<Variant, string> = {
  primary: "text-white",
  secondary: "text-slate-900",
  danger: "text-white",
};

export function Button({ title, variant = "primary", loading, disabled, ...rest }: Props) {
  const isDisabled = disabled || loading;
  return (
    <Pressable
      {...rest}
      disabled={isDisabled}
      className={`px-4 py-3 rounded-md items-center ${variantClasses[variant]} ${isDisabled ? "opacity-50" : ""}`}
    >
      {loading
        ? <ActivityIndicator color={variant === "secondary" ? "#0f172a" : "#ffffff"} />
        : <Text className={`text-base font-semibold ${textClasses[variant]}`}>{title}</Text>}
    </Pressable>
  );
}
