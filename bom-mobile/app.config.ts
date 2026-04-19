import type { ExpoConfig } from "expo/config";

export default (): ExpoConfig => ({
  name: "FPF Quotations",
  slug: "fpf-quotations",
  version: "0.1.0",
  orientation: "portrait",
  userInterfaceStyle: "light",
  ios: {
    supportsTablet: false,
    bundleIdentifier: "ae.fpf.quotations",
  },
  plugins: ["expo-router", "expo-secure-store"],
  scheme: "fpfquotations",
  extra: {
    apiBaseUrl: process.env.EXPO_PUBLIC_API_BASE_URL ?? "http://localhost:7300",
  },
  experiments: { typedRoutes: true },
});
