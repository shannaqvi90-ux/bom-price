import { z } from "zod";

export const loginSchema = z.object({
  email: z.string().email(),
  password: z.string().min(1, "Password is required"),
});

export type LoginInput = z.infer<typeof loginSchema>;

export const createRequisitionSchema = z.object({
  customerId: z.number().int().positive("Customer is required"),
  currencyCode: z.string().min(1, "Currency is required"),
  items: z
    .array(
      z.object({
        itemId: z.number().int().positive("Item is required"),
        expectedQty: z.number().positive("Quantity must be greater than zero"),
      })
    )
    .min(1, "At least one item is required"),
});

export type CreateRequisitionInput = z.infer<typeof createRequisitionSchema>;
