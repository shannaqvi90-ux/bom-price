import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/Card";

export default function SalesDashboard() {
  return (
    <Card>
      <CardHeader>
        <CardTitle>Sales Dashboard</CardTitle>
      </CardHeader>
      <CardContent>
        <p className="text-muted-foreground">
          Your requisitions and quick actions will appear here.
        </p>
      </CardContent>
    </Card>
  );
}
