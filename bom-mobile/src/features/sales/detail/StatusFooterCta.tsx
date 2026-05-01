import { View, Pressable, Text, ActivityIndicator } from "react-native";
import type { V3Requisition } from "../../../types/v3";
import { useSubmitToCosting } from "../../../api/requisitions";
import { theme } from "../../../theme";

interface Props {
  req: V3Requisition;
  onCustomerConfirm: () => void;
  onDownloadPdf: () => void;
}

export function StatusFooterCta({ req, onCustomerConfirm, onDownloadPdf }: Props) {
  const submit = useSubmitToCosting();

  if (req.status === "Draft") {
    return (
      <Footer>
        <PrimaryButton
          loading={submit.isPending}
          onPress={() => submit.mutate(req.id)}
          label="Submit to Costing"
        />
      </Footer>
    );
  }
  if (req.status === "CustomerConfirm") {
    return (
      <Footer>
        <PrimaryButton onPress={onCustomerConfirm} label="Customer response" />
      </Footer>
    );
  }
  if (req.status === "Signed") {
    return (
      <Footer>
        <PrimaryButton onPress={onDownloadPdf} label="Download PDF" />
      </Footer>
    );
  }
  if (req.status === "Costing" || req.status === "MdPricing" || req.status === "MdFinalSign") {
    return (
      <Footer>
        <Text style={{ textAlign: "center", color: "#64748b" }}>
          Waiting on {req.status === "Costing" ? "Accountant" : req.status === "MdPricing" ? "MD pricing" : "MD final sign"}
        </Text>
      </Footer>
    );
  }
  return null; // Rejected + Cancelled = no footer
}

function Footer({ children }: { children: React.ReactNode }) {
  return (
    <View style={{
      padding: 16, paddingBottom: 32, backgroundColor: "white",
      borderTopWidth: 1, borderColor: "#e5e7eb",
    }}>
      {children}
    </View>
  );
}

function PrimaryButton({ onPress, label, loading }: { onPress: () => void; label: string; loading?: boolean }) {
  return (
    <Pressable
      onPress={onPress}
      disabled={loading}
      style={{
        backgroundColor: theme.colors.primary, padding: 14, borderRadius: 10,
        alignItems: "center", opacity: loading ? 0.6 : 1,
      }}
    >
      {loading ? <ActivityIndicator color="white" /> : (
        <Text style={{ color: "white", fontWeight: "600", fontSize: 15 }}>{label}</Text>
      )}
    </Pressable>
  );
}
