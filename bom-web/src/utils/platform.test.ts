import { describe, it, expect } from "vitest";
import { isIOSorIPadOS, isSafari, isStandalone, isAndroidChrome } from "./platform";

const setUA = (ua: string, maxTouch = 0) => {
  Object.defineProperty(navigator, "userAgent", { value: ua, configurable: true });
  Object.defineProperty(navigator, "maxTouchPoints", { value: maxTouch, configurable: true });
};

const setStandalone = (matches: boolean, navigatorStandalone = false) => {
  Object.defineProperty(window, "matchMedia", {
    value: () => ({ matches, addEventListener: () => {}, removeEventListener: () => {} }),
    configurable: true,
  });
  Object.defineProperty(navigator, "standalone", { value: navigatorStandalone, configurable: true });
};

describe("isIOSorIPadOS", () => {
  it("detects iPhone", () => {
    setUA("Mozilla/5.0 (iPhone; CPU iPhone OS 17_4 like Mac OS X) Safari");
    expect(isIOSorIPadOS()).toBe(true);
  });

  it("detects pre-iPadOS-13 iPad (legacy UA)", () => {
    setUA("Mozilla/5.0 (iPad; CPU OS 12_4 like Mac OS X) Safari");
    expect(isIOSorIPadOS()).toBe(true);
  });

  it("detects modern iPadOS (Mac-disguised UA + touch)", () => {
    setUA("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) Safari", 5);
    expect(isIOSorIPadOS()).toBe(true);
  });

  it("rejects desktop Mac (no touch)", () => {
    setUA("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) Safari", 0);
    expect(isIOSorIPadOS()).toBe(false);
  });

  it("rejects Android", () => {
    setUA("Mozilla/5.0 (Linux; Android 14; Pixel 8) Chrome", 5);
    expect(isIOSorIPadOS()).toBe(false);
  });

  it("rejects Windows desktop", () => {
    setUA("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome", 0);
    expect(isIOSorIPadOS()).toBe(false);
  });
});

describe("isSafari", () => {
  it("detects Safari iPhone", () => {
    setUA("Mozilla/5.0 (iPhone; CPU iPhone OS 17_4 like Mac OS X) Version/17.4 Safari");
    expect(isSafari()).toBe(true);
  });

  it("rejects Chrome iOS (CriOS)", () => {
    setUA("Mozilla/5.0 (iPhone; CPU iPhone OS 17_4) CriOS/120 Safari");
    expect(isSafari()).toBe(false);
  });

  it("rejects desktop Chrome", () => {
    setUA("Mozilla/5.0 (Macintosh) Chrome/120 Safari");
    expect(isSafari()).toBe(false);
  });

  it("rejects Firefox iOS (FxiOS)", () => {
    setUA("Mozilla/5.0 (iPhone) FxiOS/130 Safari");
    expect(isSafari()).toBe(false);
  });
});

describe("isAndroidChrome", () => {
  it("detects Android Chrome", () => {
    setUA("Mozilla/5.0 (Linux; Android 14; Pixel 8) Chrome/120");
    expect(isAndroidChrome()).toBe(true);
  });

  it("rejects iOS Chrome (CriOS)", () => {
    setUA("Mozilla/5.0 (iPhone) CriOS/120");
    expect(isAndroidChrome()).toBe(false);
  });

  it("rejects desktop Chrome", () => {
    setUA("Mozilla/5.0 (Macintosh) Chrome/120");
    expect(isAndroidChrome()).toBe(false);
  });
});

describe("isStandalone", () => {
  it("returns true when matchMedia standalone matches", () => {
    setStandalone(true);
    expect(isStandalone()).toBe(true);
  });

  it("returns true when navigator.standalone is true (iOS Safari quirk)", () => {
    setStandalone(false, true);
    expect(isStandalone()).toBe(true);
  });

  it("returns false in regular browser tab", () => {
    setStandalone(false, false);
    expect(isStandalone()).toBe(false);
  });
});
