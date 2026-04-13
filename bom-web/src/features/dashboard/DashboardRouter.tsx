import { useAuthStore } from "@/store/authStore";
import SalesDashboard from "./SalesDashboard";
import BomDashboard from "./BomDashboard";
import AccountantDashboard from "./AccountantDashboard";
import MdDashboard from "./MdDashboard";
import AdminDashboard from "./AdminDashboard";

export default function DashboardRouter() {
  const role = useAuthStore((s) => s.user?.role);

  switch (role) {
    case "SalesPerson":
      return <SalesDashboard />;
    case "BomCreator":
      return <BomDashboard />;
    case "Accountant":
      return <AccountantDashboard />;
    case "ManagingDirector":
      return <MdDashboard />;
    case "Admin":
      return <AdminDashboard />;
    default:
      return null;
  }
}
