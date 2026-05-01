import { useState } from "react";
import { Alert, ScrollView, Text, TextInput, View } from "react-native";
import { useRouter } from "expo-router";
import type { V3Requisition } from "@/types/v3";
import { useFinalSign } from "../api/approvals";
import { useOwnSignature } from "@/features/profile/api/signature";
import { FinalSignSummary } from "./FinalSignSummary";
import { SignaturePreview } from "./SignaturePreview";
import { Button } from "@/components/Button";
import { LoadingView } from "@/components/LoadingView";

interface Props {
  req: V3Requisition;
}

export function ActiveMdFinalSignView({ req }: Props) {
  const router = useRouter();
  const sigQ = useOwnSignature();
  const sign = useFinalSign(req.id);
  const [token, setToken] = useState("");
  const [notes, setNotes] = useState("");

  if (sigQ.isPending) return <LoadingView />;

  if (!sigQ.data?.exists) {
    return (
      <View style={{ flex: 1, padding: 24, justifyContent: "center" }}>
        <Text
          style={{
            fontSize: 18,
            fontWeight: "700",
            color: "#92400e",
            textAlign: "center",
          }}
        >
          ⚠️ No signature uploaded
        </Text>
        <Text
          style={{
            fontSize: 14,
            color: "#475569",
            textAlign: "center",
            marginTop: 12,
          }}
        >
          Please upload your signature in Profile before signing this quotation.
        </Text>
        <View style={{ marginTop: 24 }}>
          <Button title="Open Profile" onPress={() => router.push("/profile")} />
        </View>
      </View>
    );
  }

  const handleSign = async () => {
    if (token !== "SIGN") return;
    try {
      await sign.mutateAsync({
        confirmationToken: token,
        notes: notes.trim() || undefined,
      });
    } catch (e) {
      Alert.alert("Error", e instanceof Error ? e.message : "Final sign failed");
    }
  };

  return (
    <View style={{ flex: 1 }}>
      <ScrollView contentContainerStyle={{ paddingBottom: 16 }}>
        <View
          style={{
            marginHorizontal: 12,
            marginVertical: 8,
            padding: 14,
            backgroundColor: "#fed7aa",
            borderRadius: 10,
          }}
        >
          <Text style={{ color: "#9a3412", fontSize: 14, fontWeight: "700" }}>
            ⚠️ Sign & Lock — irreversible
          </Text>
          <Text style={{ color: "#9a3412", fontSize: 13, marginTop: 4 }}>
            After signing, no changes can be made. PDF will be generated.
          </Text>
        </View>

        <View style={{ paddingHorizontal: 12, marginBottom: 8 }}>
          <Text
            style={{
              fontSize: 13,
              color: "#64748b",
              fontWeight: "600",
              letterSpacing: 0.5,
            }}
          >
            CUSTOMER
          </Text>
          <Text
            style={{
              fontSize: 16,
              fontWeight: "700",
              color: "#0f172a",
              marginTop: 4,
            }}
          >
            {req.customer.name}
          </Text>
        </View>

        {req.finalPrice ? <FinalSignSummary finalPrice={req.finalPrice} /> : null}

        <View style={{ marginHorizontal: 12, marginTop: 16 }}>
          <Text
            style={{
              fontSize: 13,
              color: "#64748b",
              fontWeight: "700",
              letterSpacing: 0.5,
            }}
          >
            YOUR SIGNATURE
          </Text>
          <View
            style={{
              marginTop: 8,
              padding: 12,
              borderWidth: 1,
              borderColor: "#e2e8f0",
              borderRadius: 10,
              alignItems: "center",
            }}
          >
            <SignaturePreview width={240} height={100} />
          </View>
        </View>

        <View style={{ paddingHorizontal: 12, marginTop: 16 }}>
          <Text
            style={{
              fontSize: 12,
              color: "#64748b",
              fontWeight: "600",
              letterSpacing: 0.5,
              marginBottom: 6,
            }}
          >
            NOTES (optional)
          </Text>
          <TextInput
            value={notes}
            onChangeText={setNotes}
            placeholder="Optional notes"
            placeholderTextColor="#94a3b8"
            multiline
            style={{
              borderWidth: 1,
              borderColor: "#cbd5e1",
              borderRadius: 10,
              padding: 10,
              fontSize: 14,
              minHeight: 60,
              textAlignVertical: "top",
              color: "#0f172a",
              backgroundColor: "white",
            }}
          />
        </View>

        <View style={{ paddingHorizontal: 12, marginTop: 16 }}>
          <Text
            style={{
              fontSize: 12,
              color: "#64748b",
              fontWeight: "600",
              letterSpacing: 0.5,
              marginBottom: 6,
            }}
          >
            TYPE SIGN TO CONFIRM
          </Text>
          <TextInput
            value={token}
            onChangeText={setToken}
            placeholder="SIGN"
            autoCapitalize="characters"
            style={{
              borderWidth: 2,
              borderColor: token === "SIGN" ? "#10b981" : "#cbd5e1",
              borderRadius: 10,
              padding: 12,
              fontSize: 18,
              fontWeight: "700",
              letterSpacing: 4,
              textAlign: "center",
              color: "#0f172a",
              backgroundColor: "white",
            }}
          />
        </View>
      </ScrollView>

      <View
        style={{
          flexDirection: "row",
          gap: 10,
          padding: 12,
          borderTopWidth: 1,
          borderTopColor: "#e2e8f0",
          backgroundColor: "white",
        }}
      >
        <View style={{ flex: 1 }}>
          <Button title="Cancel" variant="secondary" onPress={() => router.back()} />
        </View>
        <View style={{ flex: 2 }}>
          <Button
            title="Sign & Lock"
            variant="danger"
            onPress={handleSign}
            loading={sign.isPending}
            disabled={token !== "SIGN"}
          />
        </View>
      </View>
    </View>
  );
}
