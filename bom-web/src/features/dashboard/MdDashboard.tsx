import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/Card";

export default function MdDashboard() {
  return (
    <Card>
      <CardHeader>
        <CardTitle>Managing Director Dashboard</CardTitle>
      </CardHeader>
      <CardContent>
        <p className="text-muted-foreground">
          Requisitions awaiting approval will appear here.
        </p>
      </CardContent>
    </Card>
  );
}
