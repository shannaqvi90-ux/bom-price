// bom-mobile/src/features/sales/create/CombinedCreateScreen.tsx
import { useState, useMemo } from "react";
import { ScrollView, View } from "react-native";
import { useRouter } from "expo-router";
import { HeaderSection } from "./HeaderSection";
import { FgListMain } from "./FgListMain";
import { SubmitFooter } from "./SubmitFooter";
import { validateRequisition } from "./validate";
import { useCreateRequisition } from "../../../api/requisitions";
import type { V3FinishedGoodDraft } from "../../../types/v3";
import type { CustomerLite } from "../../../api/customers";

interface Props { mode: "new" | "edit"; reqId?: number }

export function CombinedCreateScreen({ mode }: Props) {
  const router = useRouter();
  const [customer, setCustomer] = useState<CustomerLite | null>(null);
  const [currency, setCurrency] = useState("AED");
  const [reference, setReference] = useState("");
  const [notes, setNotes] = useState("");
  const [fgs, setFgs] = useState<V3FinishedGoodDraft[]>([]);
  const create = useCreateRequisition();

  const validation = useMemo(() =>
    validateRequisition(customer, currency, fgs), [customer, currency, fgs]);

  const onSubmit = async () => {
    if (!customer || !validation.ok) return;
    const created = await create.mutateAsync({
      customerId: customer.id,
      quotationCurrency: currency,
      referenceNumber: reference || undefined,
      notes: notes || undefined,
      finishedGoods: fgs.map((fg) => ({
        itemId: fg.itemId,
        expectedQtyKg: fg.expectedQty,
        printing: false,
        bomLines: fg.bomLines.map((l) => ({
          processId: l.processId,
          itemId: l.rawMaterialItemId,
          qtyPerKg: l.qtyPerKg,
          // micron not collected by current UI; backend accepts undefined
        })),
      })),
    });
    router.replace(`/(sales)/${created.id}`);
  };

  return (
    <View style={{ flex: 1, backgroundColor: "#f8fafc" }}>
      <ScrollView contentContainerStyle={{ paddingBottom: 16 }} keyboardShouldPersistTaps="handled">
        <HeaderSection
          customer={customer} setCustomer={setCustomer}
          currency={currency} setCurrency={setCurrency}
          reference={reference} setReference={setReference}
          notes={notes} setNotes={setNotes}
        />
        <FgListMain fgs={fgs} setFgs={setFgs} />
      </ScrollView>
      <SubmitFooter
        validation={validation}
        onSubmit={onSubmit}
        loading={create.isPending}
        label={mode === "new" ? "Submit to Costing" : "Save changes"}
      />
    </View>
  );
}
