import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { QueryClientProvider } from "@tanstack/react-query";
import App from "./App";
import "./index.css";
import { queryClient } from "@/api/queryClient";
import { useThemeStore } from "@/store/themeStore";
import { useAuthStore } from "@/store/authStore";

useThemeStore.getState().apply();
useAuthStore.getState().initAuth();

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <App />
    </QueryClientProvider>
  </StrictMode>,
);
