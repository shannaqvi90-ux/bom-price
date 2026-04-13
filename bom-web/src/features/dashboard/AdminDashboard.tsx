import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/Card";

export default function AdminDashboard() {
  return (
    <Card>
      <CardHeader>
        <CardTitle>Admin Dashboard</CardTitle>
      </CardHeader>
      <CardContent>
        <p className="text-muted-foreground">
          User, branch, and master data management will appear here.
        </p>
      </CardContent>
    </Card>
  );
}
