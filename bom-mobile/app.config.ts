import type { ExpoConfig } from "expo/config";

const androidVersionCode = Number(process.env.ANDROID_VERSION_CODE ?? "1");

export default (): ExpoConfig => ({
  name: "FPF Quotations",
  slug: "fpf-quotations",
  version: "0.1.0",
  orientation: "portrait",
  userInterfaceStyle: "light",
  icon: "./assets/icon.png",
  splash: {
    image: "./assets/splash-icon.png",
    resizeMode: "contain",
    backgroundColor: "#ffffff",
  },
  ios: {
    supportsTablet: false,
    bundleIdentifier: "ae.fpf.quotations",
  },
  android: {
    package: "ae.fpf.quotations",
    versionCode: androidVersionCode,
    adaptiveIcon: {
      foregroundImage: "./assets/adaptive-icon.png",
      backgroundColor: "#ffffff",
    },
  },
  plugins: ["expo-router", "expo-secure-store"],
  scheme: "fpfquotations",
  extra: {
    apiBaseUrl: process.env.EXPO_PUBLIC_API_BASE_URL ?? "http://localhost:7300",
    eas: {
      projectId: process.env.EAS_PROJECT_ID ?? "",
    },
  },
  experiments: { typedRoutes: false },
});
