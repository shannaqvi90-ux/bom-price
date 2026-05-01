import { useState } from "react";
import { Alert, Text, View } from "react-native";
import * as ImagePicker from "expo-image-picker";
import * as Haptics from "expo-haptics";
import { useUploadSignature, useOwnSignature } from "./api/signature";
import { SignaturePreview } from "@/features/md/detail/SignaturePreview";
import { Button } from "@/components/Button";
import { ErrorBanner } from "@/components/ErrorBanner";

const MAX_BYTES = 500 * 1024;

export function ProfileSignatureSection() {
  const ownSignatureQ = useOwnSignature();
  const upload = useUploadSignature();
  const [error, setError] = useState<string | null>(null);

  const handleUpload = async (source: "gallery" | "camera") => {
    setError(null);
    const perm =
      source === "gallery"
        ? await ImagePicker.requestMediaLibraryPermissionsAsync()
        : await ImagePicker.requestCameraPermissionsAsync();
    if (!perm.granted) {
      Alert.alert(
        "Permission denied",
        "Permission required to upload signature.",
      );
      return;
    }

    const result =
      source === "gallery"
        ? await ImagePicker.launchImageLibraryAsync({
            mediaTypes: ["images"],
            quality: 0.9,
          })
        : await ImagePicker.launchCameraAsync({
            mediaTypes: ["images"],
            quality: 0.9,
          });

    if (result.canceled) return;

    const asset = result.assets[0];
    if (asset.fileSize && asset.fileSize > MAX_BYTES) {
      setError(
        `File too large (${Math.round(asset.fileSize / 1024)}KB). Max 500KB.`,
      );
      return;
    }

    try {
      await upload.mutateAsync({
        uri: asset.uri,
        mime: asset.mimeType ?? "image/png",
      });
      Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Upload failed");
      Haptics.notificationAsync(Haptics.NotificationFeedbackType.Error);
    }
  };

  return (
    <View style={{ paddingHorizontal: 16, paddingVertical: 12 }}>
      <Text
        style={{
          fontSize: 13,
          color: "#64748b",
          fontWeight: "700",
          letterSpacing: 0.5,
        }}
      >
        SIGNATURE (MD)
      </Text>
      <Text style={{ fontSize: 12, color: "#94a3b8", marginTop: 4 }}>
        PNG/JPG · max 500KB · used on signed quotation PDFs
      </Text>

      {ownSignatureQ.data?.exists ? (
        <View
          style={{
            marginTop: 12,
            padding: 12,
            borderWidth: 1,
            borderColor: "#e2e8f0",
            borderRadius: 10,
          }}
        >
          <SignaturePreview width={240} height={100} />
        </View>
      ) : (
        <View
          style={{
            marginTop: 12,
            padding: 16,
            backgroundColor: "#fef3c7",
            borderRadius: 10,
          }}
        >
          <Text style={{ color: "#92400e", fontSize: 13 }}>
            ⚠️ No signature uploaded yet. Final-sign will be blocked until you
            upload one.
          </Text>
        </View>
      )}

      {error ? (
        <View style={{ marginTop: 12 }}>
          <ErrorBanner message={error} />
        </View>
      ) : null}

      <View style={{ flexDirection: "row", gap: 10, marginTop: 16 }}>
        <View style={{ flex: 1 }}>
          <Button
            title="Upload from gallery"
            onPress={() => handleUpload("gallery")}
            loading={upload.isPending}
          />
        </View>
        <View style={{ flex: 1 }}>
          <Button
            title="Take photo"
            variant="secondary"
            onPress={() => handleUpload("camera")}
            loading={upload.isPending}
          />
        </View>
      </View>
    </View>
  );
}
