import { create } from "zustand";
import { persist, createJSONStorage } from "zustand/middleware";

type Theme = "dark" | "light";

interface ThemeState {
  theme: Theme;
  toggle: () => void;
  apply: () => void;
}

function applyThemeClass(theme: Theme) {
  const root = document.documentElement;
  if (theme === "dark") root.classList.add("dark");
  else root.classList.remove("dark");
}

function systemPrefersDark(): boolean {
  if (typeof window === "undefined" || !window.matchMedia) return false;
  return window.matchMedia("(prefers-color-scheme: dark)").matches;
}

export const useThemeStore = create<ThemeState>()(
  persist(
    (set, get) => ({
      // First-visit default = OS preference. Previously hardcoded "dark"
      // which dumped fresh users into a half-broken dark UI even when
      // their system was light.
      theme: systemPrefersDark() ? "dark" : "light",
      toggle: () => {
        const next: Theme = get().theme === "dark" ? "light" : "dark";
        applyThemeClass(next);
        set({ theme: next });
      },
      apply: () => applyThemeClass(get().theme),
    }),
    {
      name: "bom-theme",
      storage: createJSONStorage(() => localStorage),
    },
  ),
);
