import { createCustomerSchema } from "@/utils/validation";

describe("createCustomerSchema", () => {
  it("requires code and name", () => {
    const r = createCustomerSchema.safeParse({});
    expect(r.success).toBe(false);
  });

  it("accepts minimal valid payload", () => {
    const r = createCustomerSchema.safeParse({ code: "C1", name: "N1" });
    expect(r.success).toBe(true);
  });

  it("rejects code longer than 20 chars", () => {
    const r = createCustomerSchema.safeParse({ code: "X".repeat(21), name: "N1" });
    expect(r.success).toBe(false);
  });

  it("rejects invalid email", () => {
    const r = createCustomerSchema.safeParse({ code: "C1", name: "N1", email: "not-an-email" });
    expect(r.success).toBe(false);
  });

  it("accepts empty email (literal '')", () => {
    const r = createCustomerSchema.safeParse({ code: "C1", name: "N1", email: "" });
    expect(r.success).toBe(true);
  });
});
