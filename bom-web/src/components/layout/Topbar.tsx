import { useNavigate } from "react-router-dom";
import { Moon, Sun, LogOut, PenLine } from "lucide-react";
import { useAuthStore } from "@/store/authStore";
import { useThemeStore } from "@/store/themeStore";
import { useLogout } from "@/features/auth/authApi";
import { Button } from "@/components/ui/Button";

export function Topbar() {
  const user = useAuthStore((s) => s.user);
  const theme = useThemeStore((s) => s.theme);
  const toggleTheme = useThemeStore((s) => s.toggle);
  const logout = useLogout();
  const navigate = useNavigate();

  const onLogout = async () => {
    await logout.mutateAsync();
    navigate("/login", { replace: true });
  };

  return (
    <header className="flex h-14 items-center justify-between border-b border-border px-6 bg-background">
      <div className="text-sm text-muted-foreground">
        {user && (
          <>
            Signed in as <span className="text-foreground">{user.name}</span>
            <span className="mx-2">·</span>
            <span>{user.role}</span>
          </>
        )}
      </div>
      <div className="flex items-center gap-2">
        {user?.role === "ManagingDirector" && (
          <Button
            variant="ghost"
            onClick={() => navigate("/profile/signature")}
            aria-label="My signature"
            title="Manage your signature"
          >
            <PenLine className="h-4 w-4" />
            <span className="ml-1 text-sm">Signature</span>
          </Button>
        )}
        <Button
          variant="ghost"
          onClick={toggleTheme}
          aria-label="Toggle theme"
        >
          {theme === "dark" ? (
            <Sun className="h-4 w-4" />
          ) : (
            <Moon className="h-4 w-4" />
          )}
        </Button>
        <Button variant="ghost" onClick={onLogout} aria-label="Log out">
          <LogOut className="h-4 w-4" />
          <span className="ml-1 text-sm">Log out</span>
        </Button>
      </div>
    </header>
  );
}
