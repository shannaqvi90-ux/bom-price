import { colors as baseColors, spacing, radii, typography } from "./tokens";

export { spacing, radii, typography };

export const colors = {
  ...baseColors,
  primary: "#1e40af",
};

export const theme = { colors, spacing, radii, typography };
