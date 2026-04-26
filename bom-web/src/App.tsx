import { createBrowserRouter, Navigate, RouterProvider, useNavigate } from "react-router-dom";
import { useEffect } from "react";
import { Toaster } from "sonner";
import { setAppNavigate } from "@/lib/navigator";
import LoginPage from "@/features/auth/LoginPage";
import { AppShell } from "@/components/layout/AppShell";
import { ProtectedRoute } from "@/components/layout/ProtectedRoute";
import DashboardRouter from "@/features/dashboard/DashboardRouter";
import RequisitionListPage from "@/features/requisitions/RequisitionListPage";
import NewRequisitionPage from "@/features/requisitions/NewRequisitionPage";
import RequisitionDetailPage from "@/features/requisitions/RequisitionDetailPage";
import EditRequisitionPage from "@/features/requisitions/EditRequisitionPage";
import CustomerListPage from "@/features/customers/CustomerListPage";
import ItemListPage from "@/features/items/ItemListPage";
import BomEntryPage from "@/features/bom/BomEntryPage";
import CostingEntryPage from "@/features/costing/CostingEntryPage";
import MdReviewPage from "@/features/approvals/MdReviewPage";
import NotificationsPage from "@/features/notifications/NotificationsPage";
import ExchangeRatesPage from "@/features/exchange-rates/ExchangeRatesPage";
import UsersPage from "@/features/users/UsersPage";
import BranchesPage from "@/features/admin/branches/BranchesPage";
import GroupsPage from "@/features/admin/groups/GroupsPage";
import ChangePasswordPage from "@/features/auth/ChangePasswordPage";
import { ForceChangePasswordGuard } from "@/features/auth/ForceChangePasswordGuard";

function NavigatorBridge() {
  const n = useNavigate();
  useEffect(() => { setAppNavigate(n); }, [n]);
  return null;
}

const router = createBrowserRouter([
  { path: "/login", element: <LoginPage /> },
  {
    path: "/change-password",
    element: (
      <ProtectedRoute>
        <ChangePasswordPage />
      </ProtectedRoute>
    ),
  },
  {
    path: "/",
    element: (
      <ProtectedRoute>
        <ForceChangePasswordGuard>
          <NavigatorBridge />
          <AppShell />
        </ForceChangePasswordGuard>
      </ProtectedRoute>
    ),
    children: [
      { index: true, element: <Navigate to="/dashboard" replace /> },
      { path: "dashboard", element: <DashboardRouter /> },
      {
        path: "notifications",
        element: (
          <ProtectedRoute
            allow={["Admin", "SalesPerson", "BomCreator", "Accountant", "ManagingDirector"]}
          >
            <NotificationsPage />
          </ProtectedRoute>
        ),
      },
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
        path: "exchange-rates",
        element: (
          <ProtectedRoute
            allow={["Admin", "SalesPerson", "BomCreator", "Accountant", "ManagingDirector"]}
          >
            <ExchangeRatesPage />
          </ProtectedRoute>
        ),
      },
      {
        path: "admin/users",
        element: (
          <ProtectedRoute allow={["Admin"]}>
            <UsersPage />
          </ProtectedRoute>
        ),
      },
      {
        path: "admin/branches",
        element: (
          <ProtectedRoute allow={["Admin"]}>
            <BranchesPage />
          </ProtectedRoute>
        ),
      },
      {
        path: "admin/groups",
        element: (
          <ProtectedRoute allow={["Admin", "Accountant"]}>
            <GroupsPage />
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
          <ProtectedRoute allow={["Admin", "SalesPerson", "Accountant"]}>
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
        path: "requisitions/:id/edit",
        element: (
          <ProtectedRoute allow={["SalesPerson"]}>
            <EditRequisitionPage />
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
      {
        path: "requisitions/:id/approval",
        element: (
          <ProtectedRoute allow={["ManagingDirector"]}>
            <MdReviewPage />
          </ProtectedRoute>
        ),
      },
    ],
  },
  { path: "*", element: <Navigate to="/dashboard" replace /> },
]);

export default function App() {
  return (
    <>
      <RouterProvider router={router} />
      <Toaster position="top-right" richColors closeButton />
    </>
  );
}
