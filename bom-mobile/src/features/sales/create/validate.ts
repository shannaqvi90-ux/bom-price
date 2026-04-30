// bom-mobile/src/features/sales/create/validate.ts
import type { V3FinishedGood } from "../../../types/v3";
import type { CustomerLite } from "../../../api/customers";

export interface ValidationResult { ok: boolean; errors: string[]; }

export function validateRequisition(
  customer: CustomerLite | null,
  currency: string,
  fgs: V3FinishedGood[],
): ValidationResult {
  const errors: string[] = [];
  if (!customer) errors.push("Customer is required");
  if (!currency) errors.push("Currency is required");
  if (fgs.length === 0) errors.push("At least 1 FG required");
  fgs.forEach((fg, i) => {
    const tag = `FG #${i + 1}`;
    if (!(fg.expectedQty > 0)) errors.push(`${tag}: ExpectedQty must be > 0`);
    if (fg.bomLines.length === 0) errors.push(`${tag}: at least 1 BOM line required`);
    fg.bomLines.forEach((l, j) => {
      const lt = `${tag} line ${j + 1}`;
      if (!l.processId) errors.push(`${lt}: process required`);
      if (!l.rawMaterialItemId) errors.push(`${lt}: raw material required`);
      if (!(l.qtyPerKg > 0)) errors.push(`${lt}: qty/kg must be > 0`);
      if (l.wastagePct < 0) errors.push(`${lt}: wastage cannot be negative`);
    });
  });
  return { ok: errors.length === 0, errors };
}
