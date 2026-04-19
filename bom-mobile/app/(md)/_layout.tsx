import { Redirect, Stack } from "expo-router";
import { useRoleGuard } from "@/auth/AuthContext";
import { LoadingView } from "@/components/LoadingView";

export default function MdLayout() {
  const { status } = useRoleGuard(["ManagingDirector"]);
  if (status === "loading") return <LoadingView />;
  if (status !== "allowed") return <Redirect href="/login" />;
  return <Stack screenOptions={{ headerShown: true }} />;
}
