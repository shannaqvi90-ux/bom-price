import * as SecureStore from "expo-secure-store";

const ACCESS = "auth.access";
const REFRESH = "auth.refresh";
const USER = "auth.user";

export async function saveTokens(access: string, refresh: string) {
  await Promise.all([
    SecureStore.setItemAsync(ACCESS, access),
    SecureStore.setItemAsync(REFRESH, refresh),
  ]);
}

export async function getAccess() {
  return SecureStore.getItemAsync(ACCESS);
}

export async function getRefresh() {
  return SecureStore.getItemAsync(REFRESH);
}

export async function clearTokens() {
  await Promise.all([
    SecureStore.deleteItemAsync(ACCESS),
    SecureStore.deleteItemAsync(REFRESH),
    SecureStore.deleteItemAsync(USER),
  ]);
}

export async function saveUser<T>(user: T) {
  await SecureStore.setItemAsync(USER, JSON.stringify(user));
}

export async function getUser<T>(): Promise<T | null> {
  const raw = await SecureStore.getItemAsync(USER);
  return raw ? (JSON.parse(raw) as T) : null;
}
