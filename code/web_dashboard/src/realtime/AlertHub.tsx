import { createContext, useCallback, useContext, useEffect, useRef, useState, type ReactNode } from "react";
import { HubConnectionBuilder, HubConnectionState, LogLevel, type HubConnection } from "@microsoft/signalr";
import { getToken } from "../api/client";
import type { Alert } from "../api/types";

type Listener = (alert: Alert) => void;

interface HubState {
  connected: boolean;
  subscribe: (listener: Listener) => () => void;
}

const HubContext = createContext<HubState>({ connected: false, subscribe: () => () => {} });

// Một kết nối SignalR dùng chung cho cả ứng dụng. Backend chỉ gửi "AlertUpdated" khi cảnh
// báo của đúng tenant trong token được tạo, đổi mức, đổi trạng thái xử lý hoặc đóng.
export function AlertHubProvider({ children }: { children: ReactNode }) {
  const [connected, setConnected] = useState(false);
  const listeners = useRef(new Set<Listener>());

  useEffect(() => {
    const connection: HubConnection = new HubConnectionBuilder()
      .withUrl("/hubs/alerts", { accessTokenFactory: () => getToken() ?? "" })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(LogLevel.Warning)
      .build();

    connection.on("AlertUpdated", (alert: Alert) => listeners.current.forEach((l) => l(alert)));
    connection.onreconnecting(() => setConnected(false));
    connection.onreconnected(() => setConnected(true));
    connection.onclose(() => setConnected(false));

    let stopped = false;
    const start = async () => {
      while (!stopped && connection.state === HubConnectionState.Disconnected) {
        try {
          await connection.start();
          setConnected(true);
        } catch {
          await new Promise((r) => setTimeout(r, 5000));
        }
      }
    };
    start();
    return () => {
      stopped = true;
      connection.stop();
    };
  }, []);

  const subscribe = useCallback((listener: Listener) => {
    listeners.current.add(listener);
    return () => {
      listeners.current.delete(listener);
    };
  }, []);

  return <HubContext.Provider value={{ connected, subscribe }}>{children}</HubContext.Provider>;
}

export function useAlertHub() {
  return useContext(HubContext);
}

// Gọi lại hàm mỗi khi có cảnh báo cập nhật — dùng để tải lại danh sách.
export function useAlertUpdates(handler: Listener) {
  const { subscribe } = useAlertHub();
  const ref = useRef(handler);
  ref.current = handler;
  useEffect(() => subscribe((a) => ref.current(a)), [subscribe]);
}
