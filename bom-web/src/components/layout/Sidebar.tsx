import { useEffect, useState } from "react";
import { NavLink } from "react-router-dom";
import { motion } from "framer-motion";
import {
  LayoutDashboard,
  FileText,
  Bell,
  Users,
  Building2,
  Coins,
  Package,
  Contact,
  ChevronLeft,
  ChevronRight,
} from "lucide-react";
import { useAuthStore } from "@/store/authStore";
import { notificationsStore } from "@/store/notificationsStore";
import type { UserRole } from "@/types/api";
import { cn } from "@/lib/cn";

interface NavItem {
  to: string;
  label: string;
  icon: React.ComponentType<{ className?: string }>;
  roles?: UserRole[];
}

const NAV_ITEMS: NavItem[] = [
  { to: "/dashboard", label: "Dashboard", icon: LayoutDashboard },
  {
    to: "/requisitions",
    label: "Requisitions",
    icon: FileText,
    roles: ["Admin", "SalesPerson", "BomCreator", "Accountant", "ManagingDirector"],
  },
  { to: "/notifications", label: "Notifications", icon: Bell },
  {
    to: "/customers",
    label: "Customers",
    icon: Contact,
    roles: ["Admin", "SalesPerson", "Accountant"],
  },
  {
    to: "/items",
    label: "Items",
    icon: Package,
    roles: ["Admin", "SalesPerson", "BomCreator", "Accountant", "ManagingDirector"],
  },
  { to: "/admin/users", label: "Users", icon: Users, roles: ["Admin"] },
  {
    to: "/admin/branches",
    label: "Branches",
    icon: Building2,
    roles: ["Admin"],
  },
  { to: "/exchange-rates", label: "Exchange Rates", icon: Coins },
];

const STORAGE_KEY = "bom-sidebar-collapsed";
const NARROW_BREAKPOINT = 1024;

export function Sidebar() {
  const user = useAuthStore((s) => s.user);
  const accessToken = useAuthStore((s) => s.accessToken);
  const connect = notificationsStore((s) => s.connect);
  const disconnect = notificationsStore((s) => s.disconnect);
  const unreadCount = notificationsStore((s) => s.unreadCount);

  const [userCollapsed, setUserCollapsed] = useState<boolean>(() => {
    return localStorage.getItem(STORAGE_KEY) === "true";
  });

  const [isNarrow, setIsNarrow] = useState<boolean>(
    () => window.innerWidth < NARROW_BREAKPOINT,
  );

  useEffect(() => {
    const onResize = () => setIsNarrow(window.innerWidth < NARROW_BREAKPOINT);
    window.addEventListener("resize", onResize);
    return () => window.removeEventListener("resize", onResize);
  }, []);

  useEffect(() => {
    localStorage.setItem(STORAGE_KEY, String(userCollapsed));
  }, [userCollapsed]);

  useEffect(() => {
    if (accessToken) {
      connect(accessToken);
    } else {
      disconnect();
    }
  }, [accessToken, connect, disconnect]);

  // Narrow viewport always collapses; on wide, honour the user preference.
  const collapsed = isNarrow || userCollapsed;

  const badgeText = unreadCount > 99 ? "99+" : String(unreadCount);

  const visible = NAV_ITEMS.filter(
    (item) => !item.roles || (user && item.roles.includes(user.role)),
  );

  return (
    <motion.aside
      initial={false}
      animate={{ width: collapsed ? 60 : 220 }}
      transition={{ type: "spring", stiffness: 260, damping: 30 }}
      className="flex flex-col border-r border-border bg-sidebar text-sidebar-foreground"
    >
      <div className="flex h-14 items-center px-4 border-b border-border">
        {!collapsed && (
          <span className="text-sm font-semibold tracking-tight">
            BOM & Price
          </span>
        )}
      </div>
      <nav className="flex-1 overflow-y-auto py-2">
        {visible.map(({ to, label, icon: Icon }) => (
          <NavLink
            key={to}
            to={to}
            className={({ isActive }) =>
              cn(
                "flex items-center gap-3 px-4 py-2 mx-2 rounded-md text-sm transition-colors",
                isActive
                  ? "bg-primary text-primary-foreground"
                  : "hover:bg-muted",
              )
            }
            title={collapsed ? label : undefined}
          >
            <span className="relative shrink-0">
              <Icon className="h-4 w-4" />
              {to === "/notifications" && unreadCount > 0 && (
                <span className="absolute -top-1.5 -right-1.5 flex h-4 min-w-[1rem] items-center justify-center rounded-full bg-destructive px-0.5 text-[10px] font-bold text-destructive-foreground leading-none">
                  {badgeText}
                </span>
              )}
            </span>
            {!collapsed && <span>{label}</span>}
          </NavLink>
        ))}
      </nav>
      <button
        type="button"
        onClick={() => setUserCollapsed((c) => !c)}
        disabled={isNarrow}
        className="flex h-10 items-center justify-center border-t border-border hover:bg-muted disabled:opacity-40 disabled:cursor-not-allowed"
        aria-label={collapsed ? "Expand sidebar" : "Collapse sidebar"}
        title={isNarrow ? "Sidebar auto-collapses on narrow screens" : undefined}
      >
        {collapsed ? (
          <ChevronRight className="h-4 w-4" />
        ) : (
          <ChevronLeft className="h-4 w-4" />
        )}
      </button>
    </motion.aside>
  );
}
