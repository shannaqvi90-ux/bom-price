import { File, Paths } from "expo-file-system";
import * as Sharing from "expo-sharing";
import { getAccess } from "@/auth/secureStore";
import Constants from "expo-constants";

const baseURL =
  (Constants.expoConfig?.extra?.apiBaseUrl as string) ?? "http://localhost:7300";

export async function downloadRequisitionPdf(requisitionId: number, refNo: string) {
  const token = await getAccess();
  if (!token) throw new Error("Not authenticated");

  const url = `${baseURL}/api/approvals/${requisitionId}/pdf`;
  const target = new File(Paths.cache, `${refNo}-Quotation.pdf`);

  const downloaded = await File.downloadFileAsync(url, target, {
    headers: { Authorization: `Bearer ${token}` },
    idempotent: true,
  });

  const canShare = await Sharing.isAvailableAsync();
  if (!canShare) throw new Error("Sharing not supported on this device");

  await Sharing.shareAsync(downloaded.uri, {
    mimeType: "application/pdf",
    dialogTitle: `${refNo} Quotation`,
    UTI: "com.adobe.pdf",
  });
}
