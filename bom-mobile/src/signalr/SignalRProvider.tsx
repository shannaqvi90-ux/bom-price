import { useEffect, useRef, type ReactNode } from "react";
import * as signalR from "@microsoft/signalr";
import Constants from "expo-constants";
import { useQueryClient } from "@tanstack/react-query";
import { useAuth } from "@/auth/AuthContext";
import { getAccess } from "@/auth/secureStore";
import { requisitionKeys } from "@/api/requisitions";
import { notificationKeys } from "@/api/notifications";
import { approvalKeys } from "@/api/approvals";
import type { Notification } from "@/types/api";

const baseURL =
  (Constants.expoConfig?.extra?.apiBaseUrl as string) ?? "http://localhost:7300";

export function SignalRProvider({ children }: { children: ReactNode }) {
  const { user } = useAuth();
  const qc = useQueryClient();
  const connRef = useRef<signalR.HubConnection | null>(null);

  useEffect(() => {
    if (!user) {
      // logged out — tear down
      const c = connRef.current;
      if (c) {
        c.stop().catch(() => undefined);
        connRef.current = null;
      }
      return;
    }

    let cancelled = false;

    (async () => {
      const token = await getAccess();
      if (!token || cancelled) return;

      const connection = new signalR.HubConnectionBuilder()
        .withUrl(`${baseURL}/hubs/notifications?access_token=${token}`)
        .withAutomaticReconnect()
        .build();

      connection.on("ReceiveNotification", (n: Notification) => {
        qc.setQueryData<Notification[]>(notificationKeys.list(), (prev) =>
          prev ? [n, ...prev] : [n]
        );
        qc.setQueryData<number>(notificationKeys.unread(), (prev) =>
          typeof prev === "number" ? prev + 1 : 1
        );
        // Opportunistic invalidations based on the notification's referenceType
        if (n.referenceType === "QuotationRequest") {
          qc.invalidateQueries({ queryKey: requisitionKeys.list() });
          qc.invalidateQueries({ queryKey: requisitionKeys.detail(n.referenceId) });
          qc.invalidateQueries({ queryKey: approvalKeys.review(n.referenceId) });
        }
      });

      try {
        await connection.start();
        if (cancelled) {
          await connection.stop();
          return;
        }
        connRef.current = connection;
      } catch {
        // SignalR library retries via withAutomaticReconnect; ignore transient start failures.
      }
    })();

    return () => {
      cancelled = true;
      const c = connRef.current;
      if (c) {
        c.stop().catch(() => undefined);
        connRef.current = null;
      }
    };
  }, [user, qc]);

  return <>{children}</>;
}
