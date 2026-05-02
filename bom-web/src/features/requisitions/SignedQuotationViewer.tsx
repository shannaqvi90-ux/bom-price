import { useEffect } from "react";
import { useQuery } from "@tanstack/react-query";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/Card";
import { Button } from "@/components/ui/Button";
import { api } from "@/api/axios";

interface Props {
  requisitionId: number;
  refNo: string;
}

export function SignedQuotationViewer({ requisitionId, refNo }: Props) {
  const pdf = useQuery({
    queryKey: ["pdf", "signed", requisitionId] as const,
    queryFn: async () => {
      const r = await api.get<Blob>(`/approvals/${requisitionId}/pdf`, {
        responseType: "blob",
      });
      return URL.createObjectURL(r.data);
    },
    staleTime: Infinity,
    retry: false,
  });

  useEffect(() => {
    const url = pdf.data;
    return () => {
      if (url) URL.revokeObjectURL(url);
    };
  }, [pdf.data]);

  const onDownload = () => {
    if (!pdf.data) return;
    const a = document.createElement("a");
    a.href = pdf.data;
    a.download = `${refNo}-Quotation.pdf`;
    a.click();
  };

  return (
    <Card>
      <CardHeader className="flex-row items-center justify-between space-y-0">
        <CardTitle>Signed Quotation</CardTitle>
        <Button variant="outline" onClick={onDownload} disabled={!pdf.data}>
          Download PDF
        </Button>
      </CardHeader>
      <CardContent>
        {pdf.isLoading ? (
          <div className="flex h-[60vh] items-center justify-center text-sm text-muted-foreground">
            Loading PDF…
          </div>
        ) : pdf.isError ? (
          <div className="flex h-[60vh] items-center justify-center text-sm text-red-700">
            Failed to load PDF.
          </div>
        ) : pdf.data ? (
          <iframe
            src={pdf.data}
            title={`${refNo} signed quotation`}
            className="h-[70vh] w-full rounded border border-gray-200"
          />
        ) : null}
      </CardContent>
    </Card>
  );
}
