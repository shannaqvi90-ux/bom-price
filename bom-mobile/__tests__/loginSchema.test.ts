import { loginSchema } from "@/utils/validation";

test("accepts valid email + non-empty password", () => {
  const r = loginSchema.safeParse({ email: "a@b.com", password: "x" });
  expect(r.success).toBe(true);
});

test("rejects invalid email", () => {
  const r = loginSchema.safeParse({ email: "not-email", password: "x" });
  expect(r.success).toBe(false);
});

test("rejects empty password", () => {
  const r = loginSchema.safeParse({ email: "a@b.com", password: "" });
  expect(r.success).toBe(false);
});
