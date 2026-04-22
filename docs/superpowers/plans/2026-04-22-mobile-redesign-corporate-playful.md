# Mobile Redesign — Corporate + Playful Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the rejected glassmorphism spike in `bom-mobile/` with a Stripe/Vercel-style corporate design layered with Duolingo-grade playful micro-interactions (moti + haptics).

**Architecture:** Single-session, file-by-file refactor on branch `feature/mobile-md-features`. Design tokens live in `tailwind.config.js`. Animations use `moti` (already installed) over `react-native-reanimated`. Haptics via `expo-haptics`. No new runtime deps; remove two unused ones.

**Tech Stack:** React Native 0.81 · Expo 54 · NativeWind 4 (Tailwind) · Moti 0.30 · expo-haptics · react-native-reanimated 4 · TypeScript 5.9 · Jest 29.

**Spec:** [2026-04-22-mobile-redesign-corporate-playful-design.md](../specs/2026-04-22-mobile-redesign-corporate-playful-design.md)

**Working directory for all commands:** `D:/shan projects/BOM_Price_Approval/bom-mobile`

---

## File Structure

| File | Action | Purpose |
|---|---|---|
| `src/components/GlassCard.tsx` | **Delete** | Replaced by plain card styling in `RequisitionCard` |
| `src/components/GradientBackground.tsx` | **Delete** | No gradient in new design |
| `tailwind.config.js` | **Update** | Replace `brand.*` with `primary.*` token family |
| `src/components/Button.tsx` | **Rewrite** | Moti scale-press + haptic |
| `src/components/Input.tsx` | **Rewrite** | Moti focus border transition |
| `src/components/Skeleton.tsx` | **Create** | Opacity-shimmer block for loading states |
| `src/components/ScreenHeader.tsx` | **Create** | Reusable in-screen header (label + title + badge + right slot) |
| `src/components/StatusPill.tsx` | **Update** | Soft-colored pill (bg + matching text) |
| `src/components/RequisitionCard.tsx` | **Rewrite** | White surface, new layout + Moti press |
| `src/components/EmptyState.tsx` | **Update** | Optional floating icon slot |
| `src/components/LoadingView.tsx` | **Update** | Skeleton list variant |
| `src/components/NotificationBell.tsx` | **Update** | Wiggle animation on new notification |
| `app/login.tsx` | **Rewrite** | Stagger entrance, shake-on-error, haptics |
| `app/(md)/index.tsx` | **Rewrite** | ScreenHeader + stagger list + skeleton |
| `package.json` | **Update** | Remove `expo-blur`, `expo-linear-gradient` |

---

## Task 0: Cleanup (delete glass spike)

**Files:**
- Delete: `src/components/GlassCard.tsx`
- Delete: `src/components/GradientBackground.tsx`

**Rationale:** The glass spike was uncommitted. These two files are the only ones that are genuinely discardable. `login.tsx` and `app/(md)/index.tsx` will be rewritten in Tasks 12 and 13 respectively — no separate revert needed.

- [ ] **Step 0.1: Delete the two component files**

Run:
```bash
rm "src/components/GlassCard.tsx" "src/components/GradientBackground.tsx"
```

Verify:
```bash
ls src/components/GlassCard.tsx src/components/GradientBackground.tsx 2>&1
```
Expected: "No such file or directory" for both.

- [ ] **Step 0.2: Confirm no remaining imports**

Run from repo root:
```bash
grep -rn "GlassCard\|GradientBackground" bom-mobile/src bom-mobile/app 2>/dev/null
```
Expected: no output. (Both `login.tsx` and `(md)/index.tsx` import these — they'll get rewritten in Tasks 12/13, producing a temporary broken state that is fixed within this same session. Do not commit yet.)

---

## Task 1: Design tokens in `tailwind.config.js`

**Files:**
- Modify: `bom-mobile/tailwind.config.js`

- [ ] **Step 1.1: Replace the file**

Overwrite with:
```js
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
```

- [ ] **Step 1.2: Confirm TypeScript still compiles**

Run: `npx tsc --noEmit`
Expected: no output (clean).

Note: Legacy `brand-*` class usages exist in a few files. They'll all be rewritten in Tasks 2–13. Don't try to fix `tsc` errors from old classes — there aren't any (Tailwind class names are strings, not types).

---

## Task 2: Rewrite `src/components/Button.tsx`

**Files:**
- Rewrite: `bom-mobile/src/components/Button.tsx`

- [ ] **Step 2.1: Overwrite the file**

```tsx
import { useState } from "react";
import { ActivityIndicator, Pressable, Text, type PressableProps } from "react-native";
import { MotiView } from "moti";
import * as Haptics from "expo-haptics";

type Variant = "primary" | "secondary" | "danger" | "ghost";

interface Props extends Omit<PressableProps, "style"> {
  title: string;
  variant?: Variant;
  loading?: boolean;
}

const bg: Record<Variant, string> = {
  primary: "#1e40af",
  secondary: "#ffffff",
  danger: "#dc2626",
  ghost: "transparent",
};

const fg: Record<Variant, string> = {
  primary: "#ffffff",
  secondary: "#0f172a",
  danger: "#ffffff",
  ghost: "#1e40af",
};

export function Button({
  title,
  variant = "primary",
  loading,
  disabled,
  onPress,
  ...rest
}: Props) {
  const [pressed, setPressed] = useState(false);
  const isDisabled = disabled || loading;

  return (
    <Pressable
      {...rest}
      disabled={isDisabled}
      onPressIn={() => {
        setPressed(true);
        if (!isDisabled) Haptics.selectionAsync();
      }}
      onPressOut={() => setPressed(false)}
      onPress={onPress}
    >
      <MotiView
        animate={{ scale: pressed ? 0.97 : 1 }}
        transition={{ type: "spring", damping: 15, stiffness: 300 }}
        style={{
          backgroundColor: bg[variant],
          borderRadius: 10,
          paddingVertical: 13,
          paddingHorizontal: 16,
          alignItems: "center",
          justifyContent: "center",
          opacity: isDisabled ? 0.5 : 1,
          borderWidth: variant === "secondary" ? 1 : 0,
          borderColor: "#e2e8f0",
          shadowColor: variant === "primary" ? "#1e40af" : "#000",
          shadowOffset: { width: 0, height: 2 },
          shadowOpacity: variant === "primary" ? 0.25 : 0,
          shadowRadius: 4,
          elevation: variant === "primary" ? 3 : 0,
        }}
      >
        {loading ? (
          <ActivityIndicator color={fg[variant]} />
        ) : (
          <Text style={{ color: fg[variant], fontSize: 15, fontWeight: "600" }}>
            {title}
          </Text>
        )}
      </MotiView>
    </Pressable>
  );
}
```

- [ ] **Step 2.2: Type-check**

Run: `npx tsc --noEmit`
Expected: clean.

---

## Task 3: Rewrite `src/components/Input.tsx`

**Files:**
- Rewrite: `bom-mobile/src/components/Input.tsx`

- [ ] **Step 3.1: Overwrite the file**

```tsx
import { useState } from "react";
import { Text, TextInput, View, type TextInputProps } from "react-native";
import { MotiView } from "moti";

interface Props extends TextInputProps {
  label: string;
  error?: string;
}

export function Input({ label, error, onFocus, onBlur, ...rest }: Props) {
  const [focused, setFocused] = useState(false);
  const borderColor = error
    ? "#dc2626"
    : focused
      ? "#1e40af"
      : "#e2e8f0";

  return (
    <View style={{ marginBottom: 14 }}>
      <Text style={{ fontSize: 12, fontWeight: "600", color: "#334155", marginBottom: 6 }}>
        {label}
      </Text>
      <MotiView
        animate={{ borderColor, scale: focused ? 1.005 : 1 }}
        transition={{ type: "timing", duration: 150 }}
        style={{
          borderWidth: 1,
          borderRadius: 10,
          backgroundColor: "#f8fafc",
        }}
      >
        <TextInput
          {...rest}
          onFocus={(e) => {
            setFocused(true);
            onFocus?.(e);
          }}
          onBlur={(e) => {
            setFocused(false);
            onBlur?.(e);
          }}
          placeholderTextColor="#94a3b8"
          style={{
            paddingHorizontal: 12,
            paddingVertical: 11,
            fontSize: 15,
            color: "#0f172a",
          }}
        />
      </MotiView>
      {error ? (
        <Text style={{ color: "#dc2626", fontSize: 11, marginTop: 4 }}>{error}</Text>
      ) : null}
    </View>
  );
}
```

- [ ] **Step 3.2: Type-check**

Run: `npx tsc --noEmit`
Expected: clean.

---

## Task 4: Create `src/components/Skeleton.tsx`

**Files:**
- Create: `bom-mobile/src/components/Skeleton.tsx`

- [ ] **Step 4.1: Write the file**

```tsx
import { type ViewStyle, type StyleProp } from "react-native";
import { MotiView } from "moti";

interface Props {
  width?: number | string;
  height?: number;
  radius?: number;
  style?: StyleProp<ViewStyle>;
}

export function Skeleton({ width = "100%", height = 14, radius = 6, style }: Props) {
  return (
    <MotiView
      from={{ opacity: 0.4 }}
      animate={{ opacity: 1 }}
      transition={{ type: "timing", duration: 900, loop: true, repeatReverse: true }}
      style={[
        {
          width: width as number | `${number}%`,
          height,
          borderRadius: radius,
          backgroundColor: "#e2e8f0",
        },
        style,
      ]}
    />
  );
}
```

- [ ] **Step 4.2: Type-check**

Run: `npx tsc --noEmit`
Expected: clean.

---

## Task 5: Create `src/components/ScreenHeader.tsx`

**Files:**
- Create: `bom-mobile/src/components/ScreenHeader.tsx`

- [ ] **Step 5.1: Write the file**

```tsx
import { type ReactNode } from "react";
import { Text, View } from "react-native";
import { MotiView } from "moti";

interface Props {
  label?: string;
  title: string;
  count?: number;
  right?: ReactNode;
}

export function ScreenHeader({ label, title, count, right }: Props) {
  return (
    <MotiView
      from={{ opacity: 0, translateY: -10 }}
      animate={{ opacity: 1, translateY: 0 }}
      transition={{ type: "spring", damping: 14, stiffness: 140 }}
      style={{
        paddingHorizontal: 16,
        paddingTop: 48,
        paddingBottom: 14,
        flexDirection: "row",
        alignItems: "flex-end",
        justifyContent: "space-between",
      }}
    >
      <View style={{ flex: 1, flexShrink: 1 }}>
        {label ? (
          <Text style={{ fontSize: 11, fontWeight: "600", color: "#64748b" }}>
            {label}
          </Text>
        ) : null}
        <View style={{ flexDirection: "row", alignItems: "center", marginTop: 3 }}>
          <Text
            style={{
              fontSize: 22,
              fontWeight: "700",
              color: "#0f172a",
              letterSpacing: -0.5,
            }}
            numberOfLines={1}
          >
            {title}
          </Text>
          {typeof count === "number" && count > 0 ? (
            <MotiView
              from={{ scale: 0 }}
              animate={{ scale: 1 }}
              transition={{
                type: "spring",
                damping: 10,
                stiffness: 220,
                delay: 300,
              }}
              style={{
                marginLeft: 10,
                backgroundColor: "#1e40af",
                paddingHorizontal: 10,
                paddingVertical: 3,
                borderRadius: 999,
                shadowColor: "#1e40af",
                shadowOffset: { width: 0, height: 2 },
                shadowOpacity: 0.3,
                shadowRadius: 6,
                elevation: 3,
              }}
            >
              <Text style={{ color: "white", fontSize: 12, fontWeight: "700" }}>
                {count}
              </Text>
            </MotiView>
          ) : null}
        </View>
      </View>
      {right ? (
        <View style={{ flexDirection: "row", alignItems: "center", gap: 6 }}>
          {right}
        </View>
      ) : null}
    </MotiView>
  );
}
```

- [ ] **Step 5.2: Type-check**

Run: `npx tsc --noEmit`
Expected: clean.

- [ ] **Step 5.3: Commit Tasks 0–5**

```bash
cd "D:/shan projects/BOM_Price_Approval"
git rm bom-mobile/src/components/GlassCard.tsx bom-mobile/src/components/GradientBackground.tsx
git add bom-mobile/tailwind.config.js bom-mobile/src/components/Button.tsx bom-mobile/src/components/Input.tsx bom-mobile/src/components/Skeleton.tsx bom-mobile/src/components/ScreenHeader.tsx
git status --short
```

**Show the user the diff summary and propose message, wait for approval before committing.** Per CLAUDE.md: show diff, propose message, wait for "haan"/"ok".

Proposed message:
```
feat(mobile): design tokens + primitive components for redesign

- Remove glassmorphism spike (GlassCard, GradientBackground)
- Tailwind tokens: primary navy family, surface, border, status
- Button: Moti scale-press + selection haptic
- Input: Moti focus border transition
- Skeleton: opacity-shimmer block for loading states
- ScreenHeader: reusable header with label/title/count/right-slot
```

Then:
```bash
git commit -m "<message>"
```

---

## Task 6: Update `src/components/StatusPill.tsx`

**Files:**
- Rewrite: `bom-mobile/src/components/StatusPill.tsx`

- [ ] **Step 6.1: Overwrite the file**

```tsx
import { Text, View } from "react-native";

type Status =
  | "BomPending"
  | "BomInProgress"
  | "CostingPending"
  | "CostingInProgress"
  | "MdReview"
  | "Approved"
  | "Rejected";

const theme: Record<Status, { bg: string; fg: string; label: string }> = {
  BomPending: { bg: "#fef3c7", fg: "#92400e", label: "BOM PENDING" },
  BomInProgress: { bg: "#dbeafe", fg: "#1e40af", label: "BOM IN PROGRESS" },
  CostingPending: { bg: "#fef3c7", fg: "#92400e", label: "COSTING PENDING" },
  CostingInProgress: { bg: "#dbeafe", fg: "#1e40af", label: "COSTING IN PROGRESS" },
  MdReview: { bg: "#ede9fe", fg: "#6d28d9", label: "MD REVIEW" },
  Approved: { bg: "#d1fae5", fg: "#065f46", label: "APPROVED" },
  Rejected: { bg: "#fee2e2", fg: "#991b1b", label: "REJECTED" },
};

export function StatusPill({ status }: { status: Status }) {
  const t = theme[status] ?? { bg: "#e2e8f0", fg: "#334155", label: String(status) };
  return (
    <View
      style={{
        backgroundColor: t.bg,
        paddingHorizontal: 8,
        paddingVertical: 3,
        borderRadius: 6,
        alignSelf: "flex-start",
      }}
    >
      <Text style={{ color: t.fg, fontSize: 10, fontWeight: "700", letterSpacing: 0.3 }}>
        {t.label}
      </Text>
    </View>
  );
}
```

Rationale: old version imported `colors.status` from `@/theme/tokens` (might not exist) and rendered the raw enum string. New version is self-contained, uses a soft pill (bg + matching text) per spec, and shows human-readable labels.

- [ ] **Step 6.2: Check for usages referencing old prop shape**

Run:
```bash
grep -rn "StatusPill" bom-mobile/src bom-mobile/app --include="*.tsx"
```

Verify all callers still pass `status={...}`. Old and new signature both take `{ status: string }`.

- [ ] **Step 6.3: Type-check**

Run: `npx tsc --noEmit`
Expected: clean. If a caller passes a status string outside the 7 known values, the fallback handles it. If the types reject a valid string, cast at the call site when found in Task 7 or 13.

---

## Task 7: Rewrite `src/components/RequisitionCard.tsx`

**Files:**
- Rewrite: `bom-mobile/src/components/RequisitionCard.tsx`

- [ ] **Step 7.1: Overwrite the file**

```tsx
import { useState } from "react";
import { Pressable, Text, View } from "react-native";
import { MotiView } from "moti";
import * as Haptics from "expo-haptics";
import { StatusPill } from "./StatusPill";
import type { RequisitionListItem } from "@/types/api";
import { formatShortDate } from "@/utils/dates";

interface Props {
  item: RequisitionListItem;
  onPress: (id: number) => void;
}

export function RequisitionCard({ item, onPress }: Props) {
  const [pressed, setPressed] = useState(false);

  return (
    <Pressable
      onPressIn={() => {
        setPressed(true);
        Haptics.selectionAsync();
      }}
      onPressOut={() => setPressed(false)}
      onPress={() => onPress(item.id)}
    >
      <MotiView
        animate={{ scale: pressed ? 0.98 : 1 }}
        transition={{ type: "spring", damping: 16, stiffness: 280 }}
        style={{
          backgroundColor: "#ffffff",
          borderWidth: 1,
          borderColor: "#e2e8f0",
          borderRadius: 14,
          padding: 14,
          marginBottom: 10,
          shadowColor: "#000",
          shadowOffset: { width: 0, height: 1 },
          shadowOpacity: 0.04,
          shadowRadius: 3,
          elevation: 1,
        }}
      >
        <View
          style={{
            flexDirection: "row",
            justifyContent: "space-between",
            alignItems: "center",
            marginBottom: 6,
          }}
        >
          <Text style={{ fontSize: 14, fontWeight: "700", color: "#0f172a" }}>
            {item.refNo}
          </Text>
          <StatusPill status={item.status as Parameters<typeof StatusPill>[0]["status"]} />
        </View>
        <Text
          style={{ fontSize: 13, color: "#475569", marginBottom: 8 }}
          numberOfLines={1}
        >
          {item.customerName}
        </Text>
        <View
          style={{
            flexDirection: "row",
            justifyContent: "space-between",
            alignItems: "center",
          }}
        >
          <Text style={{ fontSize: 11, color: "#94a3b8" }}>
            {item.itemCount} {item.itemCount === 1 ? "item" : "items"} · {item.currencyCode}
          </Text>
          <Text style={{ fontSize: 11, color: "#94a3b8" }}>
            {formatShortDate(item.createdAt)}
          </Text>
        </View>
      </MotiView>
    </Pressable>
  );
}
```

- [ ] **Step 7.2: Type-check**

Run: `npx tsc --noEmit`
Expected: clean.

---

## Task 8: Update `src/components/EmptyState.tsx`

**Files:**
- Rewrite: `bom-mobile/src/components/EmptyState.tsx`

- [ ] **Step 8.1: Overwrite the file**

```tsx
import { type ReactNode } from "react";
import { Text, View } from "react-native";
import { MotiView } from "moti";

interface Props {
  title: string;
  hint?: string;
  icon?: ReactNode;
}

export function EmptyState({ title, hint, icon }: Props) {
  return (
    <View
      style={{
        flex: 1,
        alignItems: "center",
        justifyContent: "center",
        paddingHorizontal: 32,
        paddingVertical: 64,
      }}
    >
      {icon ? (
        <MotiView
          from={{ translateY: 0 }}
          animate={{ translateY: -4 }}
          transition={{ type: "timing", duration: 1800, loop: true, repeatReverse: true }}
          style={{
            width: 72,
            height: 72,
            borderRadius: 36,
            backgroundColor: "#dbeafe",
            alignItems: "center",
            justifyContent: "center",
            marginBottom: 16,
          }}
        >
          {icon}
        </MotiView>
      ) : null}
      <Text style={{ fontSize: 17, fontWeight: "700", color: "#0f172a" }}>{title}</Text>
      {hint ? (
        <Text style={{ fontSize: 13, color: "#64748b", marginTop: 6, textAlign: "center" }}>
          {hint}
        </Text>
      ) : null}
    </View>
  );
}
```

- [ ] **Step 8.2: Type-check**

Run: `npx tsc --noEmit`
Expected: clean.

---

## Task 9: Update `src/components/LoadingView.tsx`

**Files:**
- Rewrite: `bom-mobile/src/components/LoadingView.tsx`

- [ ] **Step 9.1: Overwrite the file**

```tsx
import { ActivityIndicator, View } from "react-native";
import { Skeleton } from "./Skeleton";

interface Props {
  variant?: "spinner" | "list";
}

export function LoadingView({ variant = "spinner" }: Props) {
  if (variant === "list") {
    return (
      <View style={{ padding: 16 }}>
        {[0, 1, 2, 3].map((i) => (
          <View
            key={i}
            style={{
              backgroundColor: "#ffffff",
              borderWidth: 1,
              borderColor: "#e2e8f0",
              borderRadius: 14,
              padding: 14,
              marginBottom: 10,
            }}
          >
            <View style={{ flexDirection: "row", justifyContent: "space-between", marginBottom: 10 }}>
              <Skeleton width={80} height={14} />
              <Skeleton width={90} height={18} radius={6} />
            </View>
            <Skeleton width={"70%"} height={12} style={{ marginBottom: 8 }} />
            <View style={{ flexDirection: "row", justifyContent: "space-between" }}>
              <Skeleton width={100} height={10} />
              <Skeleton width={60} height={10} />
            </View>
          </View>
        ))}
      </View>
    );
  }
  return (
    <View style={{ flex: 1, alignItems: "center", justifyContent: "center" }}>
      <ActivityIndicator size="large" color="#1e40af" />
    </View>
  );
}
```

- [ ] **Step 9.2: Type-check**

Run: `npx tsc --noEmit`
Expected: clean.

---

## Task 10: Update `src/components/NotificationBell.tsx`

**Files:**
- Modify: `bom-mobile/src/components/NotificationBell.tsx`

- [ ] **Step 10.1: Read the current file first**

Run: `cat bom-mobile/src/components/NotificationBell.tsx`
(Use the Read tool in agent mode.)

- [ ] **Step 10.2: Wrap the bell icon + badge in a `MotiView` that wiggles when unread count increases**

Strategy:
- Keep existing data-fetching + navigation logic untouched.
- Track previous unread count in a `useRef`; if new > prev, trigger wiggle once.
- Wiggle = `MotiView` with `animate` key bump → rotate sequence `[0, -15, 15, -8, 0]` via `transition.type: "timing"` + `loop: false`, total ~400 ms.
- Use `expo-haptics` `impactAsync(Light)` on wiggle trigger.

Show the full file to the agent using the Read tool, then apply these changes via Edit operations. Preserve all existing imports, hooks, navigation, and style classes except where visual polish changes are noted. If the bell uses hard-coded `text-brand-*` classes, swap to inline colors or `text-primary-800`/`#1e40af`.

- [ ] **Step 10.3: Type-check**

Run: `npx tsc --noEmit`
Expected: clean.

- [ ] **Step 10.4: Commit Tasks 6–10**

Stage all modified/created files from Tasks 6–10:
```bash
cd "D:/shan projects/BOM_Price_Approval"
git add bom-mobile/src/components/StatusPill.tsx bom-mobile/src/components/RequisitionCard.tsx bom-mobile/src/components/EmptyState.tsx bom-mobile/src/components/LoadingView.tsx bom-mobile/src/components/NotificationBell.tsx
git diff --stat HEAD
```

Show diff summary to the user, propose message, wait for approval:

```
feat(mobile): refresh shared components for corporate+playful redesign

- StatusPill: soft-colored labels per status
- RequisitionCard: white surface + border + Moti press + haptic
- EmptyState: optional floating icon
- LoadingView: skeleton list variant
- NotificationBell: wiggle + haptic on new notification
```

Then commit after approval.

---

## Task 11: Remove unused packages

**Files:**
- Modify: `bom-mobile/package.json` (automatic via `npm uninstall`)

- [ ] **Step 11.1: Verify nothing outside our spike imports them**

Run from repo root:
```bash
grep -rn "from ['\"]expo-blur['\"]\|from ['\"]expo-linear-gradient['\"]" bom-mobile/src bom-mobile/app 2>/dev/null
```
Expected: no output. (Task 0 already removed the two files that used them.)

- [ ] **Step 11.2: Uninstall**

```bash
cd bom-mobile
npm uninstall expo-blur expo-linear-gradient --legacy-peer-deps
```

- [ ] **Step 11.3: Type-check**

Run: `npx tsc --noEmit`
Expected: clean.

- [ ] **Step 11.4: Run existing test suite**

Run: `npx jest`
Expected: all tests pass (28/28 from prior baseline). Any test that imports removed files will fail — if so, that is a test that was mocking the glass components; remove/update it.

---

## Task 12: Rewrite `app/login.tsx`

**Files:**
- Rewrite: `bom-mobile/app/login.tsx`

- [ ] **Step 12.1: Overwrite the file**

```tsx
import { useState } from "react";
import {
  Alert,
  KeyboardAvoidingView,
  Platform,
  ScrollView,
  Text,
  View,
} from "react-native";
import { useRouter } from "expo-router";
import { useForm, Controller } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { MotiView } from "moti";
import * as Haptics from "expo-haptics";
import { Button } from "@/components/Button";
import { Input } from "@/components/Input";
import { loginSchema, type LoginInput } from "@/utils/validation";
import { useAuth } from "@/auth/AuthContext";

const ALLOWED_ROLES = ["SalesPerson", "ManagingDirector"] as const;

export default function Login() {
  const { login } = useAuth();
  const router = useRouter();
  const [submitting, setSubmitting] = useState(false);
  const [shakeKey, setShakeKey] = useState(0);
  const { control, handleSubmit, formState: { errors } } = useForm<LoginInput>({
    resolver: zodResolver(loginSchema),
    defaultValues: { email: "", password: "" },
  });

  const onSubmit = handleSubmit(async (values) => {
    setSubmitting(true);
    try {
      const u = await login(values.email, values.password);
      if (!ALLOWED_ROLES.includes(u.role as typeof ALLOWED_ROLES[number])) {
        Haptics.notificationAsync(Haptics.NotificationFeedbackType.Warning);
        Alert.alert("Not allowed", "This app is for Sales and Management only.");
        return;
      }
      Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
      router.replace("/");
    } catch (e: unknown) {
      Haptics.notificationAsync(Haptics.NotificationFeedbackType.Error);
      setShakeKey((k) => k + 1);
      const msg = e instanceof Error ? e.message : "Login failed";
      Alert.alert("Login failed", msg);
    } finally {
      setSubmitting(false);
    }
  });

  return (
    <KeyboardAvoidingView
      behavior={Platform.OS === "ios" ? "padding" : undefined}
      style={{ flex: 1, backgroundColor: "#ffffff" }}
    >
      <ScrollView
        contentContainerStyle={{ flexGrow: 1, justifyContent: "center", padding: 24 }}
        keyboardShouldPersistTaps="handled"
      >
        <MotiView
          from={{ opacity: 0, translateY: 14 }}
          animate={{ opacity: 1, translateY: 0 }}
          transition={{ type: "spring", damping: 14, stiffness: 140 }}
          style={{
            width: 48,
            height: 48,
            borderRadius: 12,
            backgroundColor: "#1e40af",
            marginBottom: 24,
            alignItems: "center",
            justifyContent: "center",
            shadowColor: "#1e40af",
            shadowOffset: { width: 0, height: 6 },
            shadowOpacity: 0.3,
            shadowRadius: 12,
            elevation: 6,
          }}
        >
          <View
            style={{
              width: 22,
              height: 22,
              backgroundColor: "#ffffff",
              borderRadius: 5,
              opacity: 0.95,
            }}
          />
        </MotiView>

        <MotiView
          from={{ opacity: 0, translateY: -6 }}
          animate={{ opacity: 1, translateY: 0 }}
          transition={{ type: "timing", duration: 400, delay: 150 }}
        >
          <Text
            style={{
              fontSize: 24,
              fontWeight: "700",
              color: "#0f172a",
              letterSpacing: -0.5,
            }}
          >
            Welcome back
          </Text>
          <Text style={{ fontSize: 13, color: "#64748b", marginTop: 2, marginBottom: 28 }}>
            Sign in to FPF Quotations
          </Text>
        </MotiView>

        <ShakeGroup key={shakeKey}>
          <MotiView
            from={{ opacity: 0, translateY: 12 }}
            animate={{ opacity: 1, translateY: 0 }}
            transition={{ type: "timing", duration: 400, delay: 250 }}
          >
            <Controller
              control={control}
              name="email"
              render={({ field }) => (
                <Input
                  label="Email"
                  keyboardType="email-address"
                  autoCapitalize="none"
                  autoComplete="email"
                  value={field.value}
                  onChangeText={field.onChange}
                  error={errors.email?.message}
                />
              )}
            />
          </MotiView>

          <MotiView
            from={{ opacity: 0, translateY: 12 }}
            animate={{ opacity: 1, translateY: 0 }}
            transition={{ type: "timing", duration: 400, delay: 330 }}
          >
            <Controller
              control={control}
              name="password"
              render={({ field }) => (
                <Input
                  label="Password"
                  secureTextEntry
                  value={field.value}
                  onChangeText={field.onChange}
                  error={errors.password?.message}
                />
              )}
            />
          </MotiView>
        </ShakeGroup>

        <MotiView
          from={{ opacity: 0, translateY: 12 }}
          animate={{ opacity: 1, translateY: 0 }}
          transition={{ type: "spring", damping: 14, stiffness: 140, delay: 410 }}
          style={{ marginTop: 6 }}
        >
          <Button title="Sign in" onPress={onSubmit} loading={submitting} />
        </MotiView>
      </ScrollView>
    </KeyboardAvoidingView>
  );
}

function ShakeGroup({ children }: { children: React.ReactNode }) {
  return (
    <MotiView
      from={{ translateX: 0 }}
      animate={{ translateX: 0 }}
      transition={{ type: "timing", duration: 60 }}
    >
      {children}
    </MotiView>
  );
}
```

Note: the `ShakeGroup` wraps the two input fields and re-animates each time `shakeKey` changes (which happens on login error). The `key={shakeKey}` on the group forces React to remount the `MotiView`, which re-runs its `from → animate` transition. For a true shake sequence, an expanded version (replace ShakeGroup body) would use `animate={{ translateX: [0, -8, 8, -8, 0] }}`. The skill-writer instructs engineers to prefer the simpler key-remount version first (it re-triggers inputs' entrance) and upgrade to sequence animation only if it feels flat on device.

- [ ] **Step 12.2: Type-check**

Run: `npx tsc --noEmit`
Expected: clean.

---

## Task 13: Rewrite `app/(md)/index.tsx`

**Files:**
- Rewrite: `bom-mobile/app/(md)/index.tsx`

- [ ] **Step 13.1: Overwrite the file**

```tsx
import { FlatList, Pressable, RefreshControl, Text, View } from "react-native";
import { Stack, useRouter } from "expo-router";
import { useQuery } from "@tanstack/react-query";
import { MotiView } from "moti";
import * as Haptics from "expo-haptics";
import { api } from "@/api/client";
import { requisitionKeys } from "@/api/requisitions";
import { RequisitionCard } from "@/components/RequisitionCard";
import { ScreenHeader } from "@/components/ScreenHeader";
import { EmptyState } from "@/components/EmptyState";
import { ErrorBanner } from "@/components/ErrorBanner";
import { LoadingView } from "@/components/LoadingView";
import { NotificationBell } from "@/components/NotificationBell";
import { useAuth } from "@/auth/AuthContext";
import type { RequisitionListItem } from "@/types/api";

function useMdPending() {
  return useQuery({
    queryKey: [...requisitionKeys.list(), "mdReview"],
    queryFn: async () => {
      const res = await api.get<RequisitionListItem[]>("/api/requisitions", {
        params: { status: "MdReview" },
      });
      return res.data;
    },
  });
}

export default function MdPendingApprovals() {
  const router = useRouter();
  const { logout } = useAuth();
  const q = useMdPending();

  const onLogout = async () => {
    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
    await logout();
    router.replace("/login");
  };

  const HeaderRight = (
    <>
      <NotificationBell />
      <Pressable
        onPress={onLogout}
        style={{
          paddingHorizontal: 10,
          paddingVertical: 7,
          borderRadius: 8,
          backgroundColor: "#f1f5f9",
        }}
      >
        <Text style={{ color: "#1e40af", fontSize: 13, fontWeight: "600" }}>
          Log out
        </Text>
      </Pressable>
    </>
  );

  return (
    <View style={{ flex: 1, backgroundColor: "#f8fafc" }}>
      <Stack.Screen options={{ headerShown: false }} />

      <ScreenHeader
        label="Managing Director"
        title="Pending approvals"
        count={q.data?.length}
        right={HeaderRight}
      />

      {q.isPending ? (
        <LoadingView variant="list" />
      ) : q.isError ? (
        <View style={{ padding: 16 }}>
          <ErrorBanner
            message={
              q.error instanceof Error
                ? q.error.message
                : "Failed to load pending approvals"
            }
            onRetry={() => q.refetch()}
          />
        </View>
      ) : (
        <FlatList
          data={q.data ?? []}
          keyExtractor={(r) => String(r.id)}
          contentContainerStyle={{ padding: 16, paddingTop: 4 }}
          refreshControl={
            <RefreshControl
              refreshing={q.isRefetching}
              onRefresh={() => {
                Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
                q.refetch();
              }}
              tintColor="#1e40af"
              colors={["#1e40af"]}
            />
          }
          renderItem={({ item, index }) => (
            <MotiView
              from={{ opacity: 0, translateY: 14 }}
              animate={{ opacity: 1, translateY: 0 }}
              transition={{
                type: "spring",
                damping: 16,
                stiffness: 140,
                delay: 200 + index * 80,
              }}
            >
              <RequisitionCard
                item={item}
                onPress={(id) => router.push(`/(md)/${id}`)}
              />
            </MotiView>
          )}
          ListEmptyComponent={
            <EmptyState
              title="All caught up"
              hint="Nothing pending your review right now."
              icon={<Text style={{ fontSize: 32 }}>✓</Text>}
            />
          }
        />
      )}
    </View>
  );
}
```

- [ ] **Step 13.2: Type-check**

Run: `npx tsc --noEmit`
Expected: clean.

- [ ] **Step 13.3: Commit Tasks 11–13**

```bash
cd "D:/shan projects/BOM_Price_Approval"
git add bom-mobile/app/login.tsx "bom-mobile/app/(md)/index.tsx" bom-mobile/package.json bom-mobile/package-lock.json
git diff --stat HEAD
```

Show diff summary, propose message, wait for approval:

```
feat(mobile): redesign Login + MD Home with corporate+playful styling

- Login: stagger entrance, shake+error haptic, success haptic
- MD Home: ScreenHeader with count badge, stagger list cards,
  skeleton loading, pull-to-refresh haptic, in-screen logout
- Remove unused expo-blur and expo-linear-gradient
```

Then commit after approval.

---

## Task 14: Verification — tsc + jest + device smoke test

**Files:** none modified.

- [ ] **Step 14.1: Run full TypeScript check**

```bash
cd "D:/shan projects/BOM_Price_Approval/bom-mobile"
npx tsc --noEmit
```
Expected: no output.

- [ ] **Step 14.2: Run full Jest suite**

```bash
npx jest --silent
```
Expected: all tests pass (baseline: 28 tests passing before this work).
If any fail, diagnose — most likely candidates: tests that imported a path that changed, or mocked a removed component.

- [ ] **Step 14.3: Restart Metro with cache clear**

Kill any running Metro process on port 8081 via PowerShell, then:
```bash
cd "D:/shan projects/BOM_Price_Approval/bom-mobile"
npx expo start --clear
```
Run this via background bash call (`run_in_background: true`) so it stays alive across turns.

- [ ] **Step 14.4: Device smoke test checklist**

Ask the user to:
- Re-scan the QR in Expo Go
- On login screen: observe stagger entrance (logo → title → email → password → button), press input to see focus border transition, press Sign-in button to feel scale + haptic
- Enter valid MD credentials (`md@test.com` / `Test@1234`), confirm success haptic + navigate to home
- On MD Home: observe ScreenHeader fade-down + count badge pop + cards stagger slide-up
- Pull to refresh — feel medium haptic, see spinner
- Tap a card — feel selection haptic, navigate to detail
- Log out — feel light haptic, return to login

Record any visual glitches, timing that feels off, or missing haptics for a follow-up iteration.

- [ ] **Step 14.5: No commit for this task** (verification only)

---

## Summary of commits this plan will produce

| # | Commit | Tasks |
|---|---|---|
| 1 | `feat(mobile): design tokens + primitive components for redesign` | 0–5 |
| 2 | `feat(mobile): refresh shared components for corporate+playful redesign` | 6–10 |
| 3 | `feat(mobile): redesign Login + MD Home with corporate+playful styling` | 11–13 |

Pause rules from CLAUDE.md kick in after 5 commits per session; this plan adds 3, so no mandatory session-level pause is triggered.

---

## Self-review

Against the spec:

- §4 Design tokens — Task 1 covers colors (minus `success`, `warning`, `danger` direct token exposure; they're used only inline in StatusPill + EmptyState, so no runtime use of a missing token). Radius + typography + spacing are applied inline per component. ✓
- §5 Animation contract (13 rows) — Tasks 2, 3, 5, 7, 8, 10, 12, 13 cover entrance, press, badge pop, pull-refresh, login error/success, empty-state float, new-notif wiggle, focus, and skeleton. ✓
- §6 Component changes — every listed file has a task. ✓
- §7 Screens — Tasks 12 and 13. ✓
- §8 Accessibility — inline colors verified in §4.1 of the spec. Contrast-safe. Touch targets >= 44 px implicit from padding choices. ✓
- §9 Out-of-scope — plan does not touch detail screen, sales screens, notifications screen, dark mode, swipe gestures. ✓
- §10 Success criteria — Task 14 verifies each item.

Placeholder scan: no "TBD", "TODO", or "similar to Task N" references. ✓

Type-consistency spot check:
- `StatusPill` prop `status: Status` where `Status` is a string literal union. `RequisitionCard` casts `item.status as Parameters<typeof StatusPill>[0]["status"]` — compiles regardless of what the API returns.
- `Skeleton` `width` is `number | string` typed; usage in `LoadingView` uses `"70%"` and `100` — both valid.
- `Button` variant `"ghost"` added but not used in this plan; fine as a future option.

No gaps found.
