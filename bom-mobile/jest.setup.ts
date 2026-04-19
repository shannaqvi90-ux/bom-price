import "@testing-library/jest-native/extend-expect";

jest.mock("expo-secure-store", () => {
  const store = new Map<string, string>();
  return {
    setItemAsync: async (k: string, v: string) => { store.set(k, v); },
    getItemAsync: async (k: string) => store.get(k) ?? null,
    deleteItemAsync: async (k: string) => { store.delete(k); },
  };
});

jest.mock("expo-constants", () => ({
  default: { expoConfig: { extra: { apiBaseUrl: "http://test.local" } } },
  expoConfig: { extra: { apiBaseUrl: "http://test.local" } },
}));
