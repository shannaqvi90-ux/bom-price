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
        brand: {
          50: "#eef2ff",
          100: "#e0e7ff",
          500: "#6366f1",
          600: "#4f46e5",
          700: "#4338ca",
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
