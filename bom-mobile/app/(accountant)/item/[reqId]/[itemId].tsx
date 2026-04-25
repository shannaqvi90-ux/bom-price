import { useEffect, useMemo, useReducer, useRef, useState } from "react";
import { Alert, ScrollView, Text, View } from "react-native";
import { Stack, useLocalSearchParams, useRouter } from "expo-router";
import { useExchangeRates } from "@/api/lookups";
import {
  useCostingReview,
  useSaveCostingItemDraft,
  useSubmitCostingItem,
} from "@/api/costing";
import type {
  CostingDraftLine,
  LandedCostType,
  RawMaterialCostInput,
  SaveCostingDraftRequest,
  SubmitCostingRequest,
} from "@/types/api";
import { extractFieldErrors } from "@/utils/apiError";
import { ScreenHeader } from "@/components/ScreenHeader";
import { LoadingView } from "@/components/LoadingView";
import { ErrorBanner } from "@/components/ErrorBanner";
import { Button } from "@/components/Button";
import { CostLineCard } from "@/components/CostLineCard";
import { LandedCostSection } from "@/components/LandedCostSection";
import { FohSection } from "@/components/FohSection";
import { SaveStatusBadge, type SaveStatus } from "@/components/SaveStatusBadge";

type FormState = {
  lines: Map<number, CostingDraftLine>;
  landedCostType: LandedCostType;
  landedCostValue: number;
  fohAmount: number;
};

type FormAction =
  | { type: "hydrate"; payload: FormState }
  | { type: "set-line"; bomLineId: number; line: CostingDraftLine }
  | { type: "set-landed"; landedCostType: LandedCostType; landedCostValue: number }
  | { type: "set-foh"; fohAmount: number };

function reducer(state: FormState, action: FormAction): FormState {
  switch (action.type) {
    case "hydrate": return action.payload;
    case "set-line": {
      const lines = new Map(state.lines);
      lines.set(action.bomLineId, action.line);
      return { ...state, lines };
    }
    case "set-landed":
      return { ...state, landedCostType: action.landedCostType, landedCostValue: action.landedCostValue };
    case "set-foh":
      return { ...state, fohAmount: action.fohAmount };
  }
}

const EMPTY_STATE: FormState = {
  lines: new Map(),
  landedCostType: "Percentage",
  landedCostValue: 0,
  fohAmount: 0,
};

const buildDraft = (s: FormState): SaveCostingDraftRequest => ({
  lines: Array.from(s.lines.values()),
  landedCostType: s.landedCostType,
  landedCostValue: s.landedCostValue,
  fohAmount: s.fohAmount,
});

const buildSubmit = (s: FormState): SubmitCostingRequest => ({
  rawMaterialCosts: Array.from(s.lines.values()).map<RawMaterialCostInput>((l) => ({
    bomLineId: l.bomLineId,
    costPerKg: l.costPerKg,
    currencyCode: l.currencyCode,
  })),
  landedCostType: s.landedCostType,
  landedCostValue: s.landedCostValue,
  fohAmount: s.fohAmount,
});

const DEBOUNCE_MS = 2000;

export default function CostingForm() {
  const router = useRouter();
  const params = useLocalSearchParams<{ reqId: string; itemId: string }>();
  const reqId = Number(params.reqId);
  const itemId = Number(params.itemId);

  const reviewQ = useCostingReview(reqId);
  const ratesQ = useExchangeRates();
  const saveDraft = useSaveCostingItemDraft(reqId, itemId);
  const submit = useSubmitCostingItem(reqId, itemId);

  const item = reviewQ.data?.items.find((i) => i.requisitionItemId === itemId);
  // Quote currency comes from the parent requisition; not present on the costing review
  // payload, so we read it from the requisition detail cache via api/requisitions if needed.
  // For Phase 1, default to AED — the per-line currency picker still works regardless.
  const quoteCurrency = "AED";

  const currencyOptions = useMemo(() => {
    const codes = new Set((ratesQ.data ?? []).map((r) => r.currencyCode));
    codes.add("AED");
    return Array.from(codes).sort();
  }, [ratesQ.data]);

  const [form, dispatch] = useReducer(reducer, EMPTY_STATE);
  const [hydrated, setHydrated] = useState(false);
  const [saveStatus, setSaveStatus] = useState<SaveStatus>("idle");
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const formRef = useRef<FormState>(form);

  useEffect(() => { formRef.current = form; }, [form]);

  // Hydrate from server (item.draft if present; else seed with last-cost defaults).
  useEffect(() => {
    if (!item || hydrated) return;
    const seedLines = new Map<number, CostingDraftLine>();
    const draftByLine = new Map((item.draft?.lines ?? []).map((l) => [l.bomLineId, l]));
    for (const bl of item.bomLines) {
      const d = draftByLine.get(bl.bomLineId);
      seedLines.set(bl.bomLineId, {
        bomLineId: bl.bomLineId,
        costPerKg: d?.costPerKg ?? bl.lastCost?.costPerKg ?? 0,
        currencyCode: d?.currencyCode ?? bl.lastCost?.currencyCode ?? quoteCurrency,
      });
    }
    dispatch({
      type: "hydrate",
      payload: {
        lines: seedLines,
        landedCostType: item.draft?.landedCostType ?? "Percentage",
        landedCostValue: item.draft?.landedCostValue ?? 0,
        fohAmount: item.draft?.fohAmount ?? 0,
      },
    });
    setHydrated(true);
  }, [item, hydrated, quoteCurrency]);

  const fireSave = (s: FormState) => {
    if (debounceRef.current) {
      clearTimeout(debounceRef.current);
      debounceRef.current = null;
    }
    setSaveStatus("saving");
    saveDraft.mutate(buildDraft(s), {
      onSuccess: () => {
        setSaveStatus("saved");
        setTimeout(() => setSaveStatus((cur) => (cur === "saved" ? "idle" : cur)), 5000);
      },
      onError: () => setSaveStatus("error"),
    });
  };

  const scheduleDebouncedSave = () => {
    if (debounceRef.current) clearTimeout(debounceRef.current);
    debounceRef.current = setTimeout(() => fireSave(formRef.current), DEBOUNCE_MS);
  };

  // Screen-exit save (best-effort fire-and-forget — RN cannot await before unmount).
  useEffect(() => {
    return () => {
      if (debounceRef.current) {
        clearTimeout(debounceRef.current);
        if (hydrated) {
          saveDraft.mutate(buildDraft(formRef.current));
        }
      }
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [hydrated]);

  const onSubmit = () => {
    if (!item) return;
    Alert.alert(
      "Submit costing?",
      `Item "${item.itemDescription}" will be submitted to MD.`,
      [
        { text: "Cancel", style: "cancel" },
        {
          text: "Submit",
          onPress: () => {
            if (debounceRef.current) {
              clearTimeout(debounceRef.current);
              debounceRef.current = null;
            }
            submit.mutate(buildSubmit(formRef.current), {
              onSuccess: () => router.back(),
              onError: (err) => {
                const errs = extractFieldErrors(err);
                if (Object.keys(errs).length > 0) {
                  setFieldErrors(errs);
                } else {
                  Alert.alert("Submit failed", "Please retry. If it persists, contact admin.");
                }
              },
            });
          },
        },
      ],
    );
  };

  if (reviewQ.isPending || ratesQ.isPending) return <LoadingView />;
  if (reviewQ.isError) {
    return (
      <View style={{ flex: 1, backgroundColor: "#f8fafc" }}>
        <Stack.Screen options={{ headerShown: false }} />
        <ScreenHeader title="Costing" />
        <ErrorBanner message="Failed to load costing data" onRetry={() => reviewQ.refetch()} />
      </View>
    );
  }
  if (!item) {
    return (
      <View style={{ flex: 1, backgroundColor: "#f8fafc" }}>
        <Stack.Screen options={{ headerShown: false }} />
        <ScreenHeader title="Costing" />
        <ErrorBanner message="Item not found in this requisition" onRetry={() => router.back()} />
      </View>
    );
  }
  if (!hydrated) return <LoadingView />;

  return (
    <View style={{ flex: 1, backgroundColor: "#f8fafc" }}>
      <Stack.Screen options={{ headerShown: false }} />
      <ScreenHeader
        title={item.itemDescription}
        right={<SaveStatusBadge status={saveStatus} onRetry={() => fireSave(formRef.current)} />}
      />
      <ScrollView contentContainerStyle={{ padding: 16, paddingBottom: 96 }}>
        <Text style={{ fontSize: 13, color: "#64748b", marginBottom: 12 }}>
          Expected qty: {item.expectedQty} kg
        </Text>

        <Text
          style={{
            fontSize: 11,
            letterSpacing: 0.6,
            color: "#64748b",
            fontWeight: "700",
            marginBottom: 6,
          }}
        >
          BOM LINES ({item.bomLines.length})
        </Text>
        {item.bomLines.map((bl) => {
          const cur = form.lines.get(bl.bomLineId) ?? {
            bomLineId: bl.bomLineId,
            costPerKg: 0,
            currencyCode: quoteCurrency,
          };
          return (
            <CostLineCard
              key={bl.bomLineId}
              line={bl}
              value={{ costPerKg: cur.costPerKg, currencyCode: cur.currencyCode }}
              currencyOptions={currencyOptions}
              fieldError={
                fieldErrors[`rawMaterialCosts.${bl.bomLineId}.costPerKg`] ??
                fieldErrors[`lines.${bl.bomLineId}.costPerKg`]
              }
              onChange={(v) => {
                dispatch({
                  type: "set-line",
                  bomLineId: bl.bomLineId,
                  line: { bomLineId: bl.bomLineId, ...v },
                });
                scheduleDebouncedSave();
              }}
              onBlur={() => fireSave(formRef.current)}
            />
          );
        })}

        <LandedCostSection
          type={form.landedCostType}
          value={form.landedCostValue}
          fieldError={fieldErrors["landedCostValue"]}
          onChange={(v) => {
            dispatch({ type: "set-landed", landedCostType: v.type, landedCostValue: v.value });
            scheduleDebouncedSave();
          }}
          onBlur={() => fireSave(formRef.current)}
        />
        <FohSection
          amount={form.fohAmount}
          fieldError={fieldErrors["fohAmount"]}
          onChange={(v) => {
            dispatch({ type: "set-foh", fohAmount: v });
            scheduleDebouncedSave();
          }}
          onBlur={() => fireSave(formRef.current)}
        />
      </ScrollView>

      <View style={{ position: "absolute", left: 16, right: 16, bottom: 16 }}>
        <Button
          title={submit.isPending ? "Submitting…" : "Submit"}
          onPress={onSubmit}
          disabled={submit.isPending || saveStatus === "saving" || item.costStatus !== "InProgress"}
        />
      </View>
    </View>
  );
}
