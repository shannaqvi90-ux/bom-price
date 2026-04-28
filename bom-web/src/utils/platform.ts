export const isIOSorIPadOS = (): boolean => {
  if (/iPad|iPhone|iPod/.test(navigator.userAgent)) return true;
  return /Macintosh/.test(navigator.userAgent) && navigator.maxTouchPoints > 1;
};

export const isSafari = (): boolean =>
  /Safari/.test(navigator.userAgent) && !/Chrome|CriOS|FxiOS|EdgiOS/.test(navigator.userAgent);

export const isStandalone = (): boolean =>
  window.matchMedia("(display-mode: standalone)").matches ||
  (navigator as Navigator & { standalone?: boolean }).standalone === true;

export const isAndroidChrome = (): boolean =>
  /Android/.test(navigator.userAgent) && /Chrome/.test(navigator.userAgent);
