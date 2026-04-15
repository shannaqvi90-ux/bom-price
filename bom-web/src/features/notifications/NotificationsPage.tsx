import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { Button } from "@/components/ui/Button";
import { Card, CardContent } from "@/components/ui/Card";
import { notificationsStore } from "@/store/notificationsStore";
import { useNotifications, useMarkRead, useMarkAllRead } from "./notificationsApi";
import type { Notification } from "@/types/api";
import { cn } from "@/lib/cn";

function relativeTime(iso: string): string {
  const diff = Date.now() - new Date(iso).getTime();
  const mins = Math.floor(diff / 60000);
  if (mins < 1) return "just now";
  if (mins < 60) return `${mins}m ago`;
  const hrs = Math.floor(mins / 60);
  if (hrs < 24) return `${hrs}h ago`;
  return `${Math.floor(hrs / 24)}d ago`;
}

export default function NotificationsPage() {
  const navigate = useNavigate();
  const { notifications, unreadCount } = notificationsStore();
  const [activeTab, setActiveTab] = useState<"unread" | "all">("unread");

  useNotifications();

  const markRead = useMarkRead();
  const markAllRead = useMarkAllRead();

  const filteredItems =
    activeTab === "unread" ? notifications.filter((n) => !n.isRead) : notifications;

  const handleClick = (n: Notification) => {
    markRead.mutate(n.id, {
      onSuccess: () => navigate(`/requisitions/${n.referenceId}`),
    });
  };

  return (
    <div className="p-6 max-w-2xl mx-auto">
      <div className="flex items-center justify-between mb-4">
        <h1 className="text-xl font-semibold">Notifications</h1>
        {activeTab === "unread" && unreadCount > 0 && (
          <Button variant="ghost" size="sm" onClick={() => markAllRead.mutate()}>
            Mark all read
          </Button>
        )}
      </div>

      <div className="flex border-b border-border mb-4">
        <button
          type="button"
          className={cn(
            "px-4 py-2 text-sm font-medium border-b-2 -mb-px transition-colors",
            activeTab === "unread"
              ? "border-primary text-primary"
              : "border-transparent text-muted-foreground hover:text-foreground",
          )}
          onClick={() => setActiveTab("unread")}
        >
          Unread ({unreadCount})
        </button>
        <button
          type="button"
          className={cn(
            "px-4 py-2 text-sm font-medium border-b-2 -mb-px transition-colors",
            activeTab === "all"
              ? "border-primary text-primary"
              : "border-transparent text-muted-foreground hover:text-foreground",
          )}
          onClick={() => setActiveTab("all")}
        >
          All
        </button>
      </div>

      <Card>
        <CardContent className="p-0">
          {filteredItems.length === 0 ? (
            <p className="p-6 text-center text-muted-foreground text-sm">
              {activeTab === "unread" ? "You're all caught up." : "No notifications yet."}
            </p>
          ) : (
            filteredItems.map((n) => (
              <button
                key={n.id}
                type="button"
                className={cn(
                  "w-full flex items-start justify-between gap-4 px-4 py-3 border-b border-border last:border-0 text-left hover:bg-muted/60 transition-colors",
                  !n.isRead && "bg-muted/40",
                )}
                onClick={() => handleClick(n)}
              >
                <div className="flex items-start gap-3 min-w-0">
                  {!n.isRead && (
                    <span className="mt-1.5 h-2 w-2 shrink-0 rounded-full bg-primary" />
                  )}
                  <span
                    className={cn(
                      "text-sm truncate",
                      n.isRead ? "text-muted-foreground pl-5" : "",
                    )}
                  >
                    {n.message}
                  </span>
                </div>
                <span className="text-xs text-muted-foreground shrink-0 whitespace-nowrap">
                  {relativeTime(n.createdAt)}
                </span>
              </button>
            ))
          )}
        </CardContent>
      </Card>
    </div>
  );
}
