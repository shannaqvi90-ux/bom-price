import { useAuthStore } from "@/store/authStore";
import SalesDashboard from "./SalesDashboard";
import AccountantDashboard from "./AccountantDashboard";
import MdDashboard from "./MdDashboard";
import AdminDashboard from "./AdminDashboard";

export default function DashboardRouter() {
  const role = useAuthStore((s) => s.user?.role);

  switch (role) {
    case "SalesPerson":
      return <SalesDashboard />;
    case "Accountant":
      return <AccountantDashboard />;
    case "ManagingDirector":
      return <MdDashboard />;
    case "Admin":
      return <AdminDashboard />;
    default:
      // BomCreator role deprecated in V3.
      return null;
  }
}
