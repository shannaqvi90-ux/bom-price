export const colors = {
  brand: { 50: "#eef2ff", 500: "#6366f1", 600: "#4f46e5" },
  text: { primary: "#0f172a", muted: "#64748b", inverse: "#ffffff" },
  bg: { app: "#f8fafc", card: "#ffffff", border: "#e2e8f0" },
  status: {
    Draft: "#64748b",
    BomPending: "#f59e0b",
    BomInProgress: "#3b82f6",
    CostingPending: "#f59e0b",
    CostingInProgress: "#3b82f6",
    MdReview: "#8b5cf6",
    Approved: "#10b981",
    Rejected: "#ef4444",
  },
} as const;

export const spacing = { xs: 4, sm: 8, md: 12, lg: 16, xl: 24, xxl: 32 } as const;

export const radii = { sm: 4, md: 8, lg: 12, full: 9999 } as const;

export const typography = {
  h1: { fontSize: 24, fontWeight: "700" as const },
  h2: { fontSize: 20, fontWeight: "600" as const },
  body: { fontSize: 16, fontWeight: "400" as const },
  caption: { fontSize: 13, fontWeight: "400" as const },
} as const;
