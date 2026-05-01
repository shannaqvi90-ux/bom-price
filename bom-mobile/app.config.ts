import type { ExpoConfig } from "expo/config";

const androidVersionCode = Number(process.env.ANDROID_VERSION_CODE ?? "1");
const allowCleartextTraffic = process.env.EAS_BUILD_PROFILE !== "production";

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
  plugins: [
    "expo-router",
    "expo-secure-store",
    [
      "expo-build-properties",
      {
        android: {
          usesCleartextTraffic: allowCleartextTraffic,
        },
      },
    ],
    [
      "expo-image-picker",
      {
        photosPermission: "Allow access to photo library to upload signature.",
        cameraPermission: "Allow camera to capture signature.",
      },
    ],
  ],
  scheme: "fpfquotations",
  updates: {
    url: "https://u.expo.dev/4d550ebf-6917-4811-8d0c-db0aa90e559f",
  },
  runtimeVersion: {
    policy: "appVersion",
  },
  extra: {
    // API base URL.
    //   Dev: defaults to http://localhost:7300 (use `adb reverse` to reach
    //        host's API from emulator, or set EXPO_PUBLIC_API_BASE_URL=http://192.168.x.x:7300
    //        in `.env.local` when testing on a physical device on LAN).
    //   Prod: set EXPO_PUBLIC_API_BASE_URL on EAS build profile env vars
    //        (eas.json) — e.g. "https://bom-fpf-api.fly.dev".
    apiBaseUrl: process.env.EXPO_PUBLIC_API_BASE_URL ?? "http://localhost:7300",
    eas: {
      projectId: "4d550ebf-6917-4811-8d0c-db0aa90e559f",
    },
  },
  experiments: { typedRoutes: false },
});
