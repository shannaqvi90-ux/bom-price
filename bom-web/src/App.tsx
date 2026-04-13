import { createBrowserRouter, Navigate, RouterProvider } from "react-router-dom";
import LoginPage from "@/features/auth/LoginPage";
import { AppShell } from "@/components/layout/AppShell";
import { ProtectedRoute } from "@/components/layout/ProtectedRoute";
import DashboardRouter from "@/features/dashboard/DashboardRouter";
import RequisitionListPage from "@/features/requisitions/RequisitionListPage";
import NewRequisitionPage from "@/features/requisitions/NewRequisitionPage";
import RequisitionDetailPage from "@/features/requisitions/RequisitionDetailPage";

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
        path: "requisitions",
        element: (
          <ProtectedRoute
            allow={["SalesPerson", "BomCreator", "Accountant", "ManagingDirector"]}
          >
            <RequisitionListPage />
          </ProtectedRoute>
        ),
      },
      {
        path: "requisitions/new",
        element: (
          <ProtectedRoute allow={["SalesPerson"]}>
            <NewRequisitionPage />
          </ProtectedRoute>
        ),
      },
      {
        path: "requisitions/:id",
        element: (
          <ProtectedRoute
            allow={["SalesPerson", "BomCreator", "Accountant", "ManagingDirector"]}
          >
            <RequisitionDetailPage />
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
