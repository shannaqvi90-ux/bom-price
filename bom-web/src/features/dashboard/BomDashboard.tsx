import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/Card";

export default function BomDashboard() {
  return (
    <Card>
      <CardHeader>
        <CardTitle>BOM Creator Dashboard</CardTitle>
      </CardHeader>
      <CardContent>
        <p className="text-muted-foreground">
          Requisitions awaiting BOM will appear here.
        </p>
      </CardContent>
    </Card>
  );
}
