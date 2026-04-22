/** @type {import('tailwindcss').Config} */
module.exports = {
  content: [
    "./app/**/*.{ts,tsx}",
    "./src/**/*.{ts,tsx}",
  ],
  presets: [require("nativewind/preset")],
  theme: {
    extend: {
      colors: {
        primary: {
          50: "#eff6ff",
          100: "#dbeafe",
          500: "#3b82f6",
          600: "#2563eb",
          700: "#1d4ed8",
          800: "#1e40af",
          900: "#1e3a8a",
        },
        brand: {
          50: "#eff6ff",
          100: "#dbeafe",
          500: "#3b82f6",
          600: "#2563eb",
          700: "#1d4ed8",
        },
        surface: {
          DEFAULT: "#ffffff",
          alt: "#f8fafc",
        },
        border: {
          DEFAULT: "#e2e8f0",
        },
        status: {
          pending: "#f59e0b",
          progress: "#3b82f6",
          review: "#8b5cf6",
          approved: "#10b981",
          rejected: "#ef4444",
        },
      },
    },
  },
  plugins: [],
};
