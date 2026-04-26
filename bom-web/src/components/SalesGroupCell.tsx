import { useUserGroup } from "@/api/userGroup";

interface Props {
  userId: number;
  role: string;
}

export function SalesGroupCell({ userId, role }: Props) {
  const { data } = useUserGroup(userId, role === "SalesPerson");
  if (role !== "SalesPerson") return <span className="text-muted-foreground">—</span>;
  return (
    <span className="text-sm">
      {data?.groupName ?? <span className="text-muted-foreground">—</span>}
    </span>
  );
}
