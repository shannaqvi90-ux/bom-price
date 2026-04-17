import type { NavigateFunction } from "react-router-dom";

let appNavigate: NavigateFunction | null = null;

export function setAppNavigate(n: NavigateFunction): void {
  appNavigate = n;
}

export function getAppNavigate(): NavigateFunction | null {
  return appNavigate;
}
