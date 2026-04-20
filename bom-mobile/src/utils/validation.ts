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

export const approveSchema = z.object({
  items: z
    .array(
      z.object({
        requisitionItemId: z.number().int().positive(),
        salesPricePerKgAed: z.number().positive("Price must be greater than zero"),
      })
    )
    .min(1, "At least one item is required"),
  notes: z.string().max(2000, "Notes must be 2000 characters or fewer").optional(),
});

export type ApproveInput = z.infer<typeof approveSchema>;
