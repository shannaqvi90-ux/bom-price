import { createBrowserRouter, Navigate, RouterProvider } from "react-router-dom";
import LoginPage from "@/features/auth/LoginPage";
import { AppShell } from "@/components/layout/AppShell";
import { ProtectedRoute } from "@/components/layout/ProtectedRoute";
import DashboardRouter from "@/features/dashboard/DashboardRouter";
import RequisitionListPage from "@/features/requisitions/RequisitionListPage";
import NewRequisitionPage from "@/features/requisitions/NewRequisitionPage";
import RequisitionDetailPage from "@/features/requisitions/RequisitionDetailPage";
import CustomerListPage from "@/features/customers/CustomerListPage";
import ItemListPage from "@/features/items/ItemListPage";
import BomEntryPage from "@/features/bom/BomEntryPage";
import CostingEntryPage from "@/features/costing/CostingEntryPage";

const router = createBrowserRouter([
  { path: "/login", element: <LoginPage /> },
  {
    path: "/",
    element: (
      <ProtectedRoute>
        <AppShell />
      </ProtectedRoute>
    ),
    children: [
      { index: true, element: <Navigate to="/dashboard" replace /> },
      { path: "dashboard", element: <DashboardRouter /> },
      {
        path: "customers",
        element: (
          <ProtectedRoute allow={["Admin", "SalesPerson"]}>
            <CustomerListPage />
          </ProtectedRoute>
        ),
      },
      {
        path: "items",
        element: (
          <ProtectedRoute
            allow={["Admin", "SalesPerson", "BomCreator", "Accountant", "ManagingDirector"]}
          >
            <ItemListPage />
          </ProtectedRoute>
        ),
      },
      {
        path: "requisitions",
        element: (
          <ProtectedRoute
            allow={["Admin", "SalesPerson", "BomCreator", "Accountant", "ManagingDirector"]}
          >
            <RequisitionListPage />
          </ProtectedRoute>
        ),
      },
      {
        path: "requisitions/new",
        element: (
          <ProtectedRoute allow={["Admin", "SalesPerson"]}>
            <NewRequisitionPage />
          </ProtectedRoute>
        ),
      },
      {
        path: "requisitions/:id",
        element: (
          <ProtectedRoute
            allow={["Admin", "SalesPerson", "BomCreator", "Accountant", "ManagingDirector"]}
          >
            <RequisitionDetailPage />
          </ProtectedRoute>
        ),
      },
      {
        path: "requisitions/:id/bom",
        element: (
          <ProtectedRoute allow={["BomCreator"]}>
            <BomEntryPage />
          </ProtectedRoute>
        ),
      },
      {
        path: "requisitions/:id/costing",
        element: (
          <ProtectedRoute allow={["Accountant"]}>
            <CostingEntryPage />
          </ProtectedRoute>
        ),
      },
    ],
  },
  { path: "*", element: <Navigate to="/dashboard" replace /> },
]);

export default function App() {
  return <RouterProvider router={router} />;
}
