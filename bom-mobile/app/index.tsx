import { Redirect } from "expo-router";
import { useAuth } from "@/auth/AuthContext";
import { LoadingView } from "@/components/LoadingView";

export default function Index() {
  const { user, loading } = useAuth();
  if (loading) return <LoadingView />;
  if (!user) return <Redirect href="/login" />;
  if (user.role === "SalesPerson") return <Redirect href="/(sales)" />;
  if (user.role === "ManagingDirector") return <Redirect href="/(md)" />;
  if (user.role === "Accountant") return <Redirect href="/(accountant)" />;
  if (user.role === "Admin") return <Redirect href="/(admin)" />;
  return <Redirect href="/login" />;
}
