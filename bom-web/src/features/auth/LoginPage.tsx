import { useEffect } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { useNavigate, Navigate } from "react-router-dom";
import { LockKeyhole } from "lucide-react";
import { useLogin } from "./authApi";
import { useAuthStore } from "@/store/authStore";
import { useLockoutCountdown } from "./useLockoutCountdown";
import { Button } from "@/components/ui/Button";
import { Input } from "@/components/ui/Input";
import { Label } from "@/components/ui/Label";
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
} from "@/components/ui/Card";

const schema = z.object({
  email: z.string().email("Enter a valid email"),
  password: z.string().min(1, "Password is required"),
});

type FormValues = z.infer<typeof schema>;

type LoginError =
  | { kind: "credentials"; message: string; attemptsRemaining?: number }
  | { kind: "locked"; message: string; secondsRemaining: number }
  | { kind: "generic"; message: string };

function parseLoginError(error: unknown): LoginError {
  const resp = (error as {
    response?: { status?: number; data?: Record<string, unknown> };
  })?.response;

  if (!resp) {
    return {
      kind: "generic",
      message: "Login failed. Please check your connection and try again.",
    };
  }

  const data = resp.data ?? {};

  if (resp.status === 400 && typeof data.lockoutSecondsRemaining === "number") {
    return {
      kind: "locked",
      message:
        typeof data.detail === "string" ? data.detail : "Account temporarily locked.",
      secondsRemaining: data.lockoutSecondsRemaining as number,
    };
  }

  if (resp.status === 401 && typeof data.message === "string") {
    return {
      kind: "credentials",
      message: data.message as string,
      attemptsRemaining:
        typeof data.attemptsRemaining === "number"
          ? (data.attemptsRemaining as number)
          : undefined,
    };
  }

  return { kind: "generic", message: "Login failed. Please try again." };
}

export default function LoginPage() {
  const isAuthed = useAuthStore((s) => s.isAuthenticated());
  const navigate = useNavigate();
  const login = useLogin();

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { email: "", password: "" },
  });

  const error: LoginError | null = login.error ? parseLoginError(login.error) : null;
  const lockoutSeconds = error?.kind === "locked" ? error.secondsRemaining : null;
  const countdown = useLockoutCountdown(lockoutSeconds);
  const isLocked = error?.kind === "locked" && !countdown.isExpired;

  // Drop the stale lockout error once the countdown reaches 0 so the banner
  // unmounts and the Sign In button re-enables.
  useEffect(() => {
    if (error?.kind === "locked" && countdown.isExpired) {
      login.reset();
    }
  }, [error?.kind, countdown.isExpired, login]);

  if (isAuthed) return <Navigate to="/dashboard" replace />;

  const onSubmit = handleSubmit(async (values) => {
    try {
      await login.mutateAsync(values);
      navigate("/dashboard", { replace: true });
    } catch {
      // error surfaced via login.error below
    }
  });

  return (
    <div className="flex min-h-screen items-center justify-center bg-background px-4">
      <Card className="w-full max-w-sm">
        <CardHeader>
          <CardTitle>Sign in</CardTitle>
          <p className="text-sm text-muted-foreground mt-1">
            BOM &amp; Price Approval
          </p>
        </CardHeader>
        <CardContent>
          {error?.kind === "locked" && !countdown.isExpired && (
            <div
              className="mb-4 rounded-md border border-red-300 bg-red-50 px-4 py-3 dark:border-red-900 dark:bg-red-950/30"
              role="alert"
              aria-live="polite"
            >
              <div className="flex items-center gap-2 font-medium text-red-900 dark:text-red-200">
                <LockKeyhole className="size-4" aria-hidden="true" />
                Account locked
              </div>
              <p className="mt-1 text-sm text-red-800 dark:text-red-300">
                Too many failed login attempts.
              </p>
              <p
                className="mt-2 font-mono text-2xl tabular-nums text-red-900 dark:text-red-200"
                aria-label={`Try again in ${countdown.formatted}`}
              >
                Try again in {countdown.formatted}
              </p>
              <p className="mt-2 text-xs text-red-700 dark:text-red-300">
                If you forgot your password, contact your administrator.
              </p>
            </div>
          )}

          <form onSubmit={onSubmit} className="space-y-4" noValidate>
            <div className="space-y-2">
              <Label htmlFor="email">Email</Label>
              <Input
                id="email"
                type="email"
                autoComplete="email"
                {...register("email")}
              />
              {errors.email && (
                <p className="text-xs text-destructive">{errors.email.message}</p>
              )}
            </div>
            <div className="space-y-2">
              <Label htmlFor="password">Password</Label>
              <Input
                id="password"
                type="password"
                autoComplete="current-password"
                {...register("password")}
              />
              {errors.password && (
                <p className="text-xs text-destructive">{errors.password.message}</p>
              )}
            </div>

            {error?.kind === "credentials" &&
              (typeof error.attemptsRemaining === "number" &&
              error.attemptsRemaining <= 2 ? (
                <div
                  className="rounded-md border border-amber-300 bg-amber-50 px-3 py-2 text-sm text-amber-900 dark:border-amber-800 dark:bg-amber-950/30 dark:text-amber-200"
                  role="alert"
                >
                  Invalid credentials.{" "}
                  <strong>
                    {error.attemptsRemaining}{" "}
                    {error.attemptsRemaining === 1 ? "attempt" : "attempts"} remaining
                  </strong>{" "}
                  before lockout.
                </div>
              ) : (
                <p className="text-sm text-destructive">{error.message}</p>
              ))}

            {error?.kind === "generic" && (
              <p className="text-sm text-destructive">{error.message}</p>
            )}

            <Button
              type="submit"
              className="w-full"
              disabled={isSubmitting || login.isPending || isLocked}
            >
              {login.isPending ? "Signing in…" : "Sign in"}
            </Button>
          </form>
        </CardContent>
      </Card>
    </div>
  );
}
