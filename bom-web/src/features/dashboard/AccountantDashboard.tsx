import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/Card";

export default function AccountantDashboard() {
  return (
    <Card>
      <CardHeader>
        <CardTitle>Accountant Dashboard</CardTitle>
      </CardHeader>
      <CardContent>
        <p className="text-muted-foreground">
          Requisitions awaiting costing will appear here.
        </p>
      </CardContent>
    </Card>
  );
}
