import { useEffect, useState } from "react";
import { NavLink, Outlet, useNavigate } from "react-router-dom";
import { useAuth } from "../auth/AuthContext";
import { useAlertHub, useAlertUpdates } from "../realtime/AlertHub";
import { useLookups } from "./Lookups";
import { LevelBadge } from "./ui";
import { LEVEL_LABEL, ROLE_LABEL, SOURCE_LABEL, canHandleAlerts, formatTime } from "../lib/format";
import type { Alert, AlertLevel } from "../api/types";

const NAV = [
  { to: "/", label: "Tổng quan", end: true },
  { to: "/cameras", label: "Giám sát camera" },
  { to: "/alerts", label: "Cảnh báo" },
  { to: "/devices", label: "Thiết bị" },
  { to: "/history", label: "Tra cứu lịch sử" },
  { to: "/logs", label: "Nhật ký hệ thống", operatorOnly: true },
];

interface Toast {
  key: number;
  alert: Alert;
  title: string;
}

const RANK: Record<AlertLevel, number> = { LOW: 0, MEDIUM: 1, HIGH: 2, CRITICAL: 3 };

export function Layout() {
  const { user, logout } = useAuth();
  const { connected } = useAlertHub();
  const { areaName } = useLookups();
  const navigate = useNavigate();
  const [toasts, setToasts] = useState<Toast[]>([]);
  const [known] = useState(() => new Map<string, AlertLevel>());

  // Chỉ bật thông báo khi cảnh báo mới phát sinh hoặc lên mức cao hơn, không bật ở mỗi
  // lần cập nhật (đổi trạng thái xử lý, hạ mức, đóng).
  useAlertUpdates((alert) => {
    const previous = known.get(alert.id);
    known.set(alert.id, alert.level);
    if (alert.endedAt) return;
    // Cảnh báo đã có từ trước khi mở trang thì lần cập nhật đầu chỉ ghi nhận mức, không báo "mới".
    const isNew = previous === undefined && Date.now() - new Date(alert.startedAt).getTime() < 60_000;
    const escalated = previous !== undefined && RANK[alert.level] > RANK[previous];
    if (!isNew && !escalated) return;
    const title = isNew ? "Cảnh báo mới" : `Cảnh báo nâng lên mức ${LEVEL_LABEL[alert.level]}`;
    const key = Date.now() + Math.random();
    setToasts((t) => [{ key, alert, title }, ...t].slice(0, 4));
    setTimeout(() => setToasts((t) => t.filter((x) => x.key !== key)), 10000);
  });

  useEffect(() => {
    document.title = toasts.length ? `(${toasts.length}) Cảnh báo – HITEK AI-IoT` : "HITEK AI-IoT – Giám sát";
  }, [toasts.length]);

  return (
    <div className="shell">
      <aside className="sidebar">
        <div className="brand">
          <strong>HITEK</strong> AI-IoT
          <small>Giám sát & cảnh báo bất thường</small>
        </div>
        <nav>
          {NAV.filter((item) => !item.operatorOnly || canHandleAlerts(user?.role)).map((item) => (
            <NavLink key={item.to} to={item.to} end={item.end} className={({ isActive }) => (isActive ? "active" : "")}>
              {item.label}
            </NavLink>
          ))}
        </nav>
        <div className="sidebar-foot">
          <span className={`live ${connected ? "on" : "off"}`}>
            <i /> {connected ? "Đang nhận cảnh báo realtime" : "Mất kết nối realtime, đang thử lại"}
          </span>
        </div>
      </aside>

      <div className="main">
        <header className="topbar">
          <div />
          <div className="user">
            <span>
              {user?.name ?? user?.email}
              <small>{ROLE_LABEL[user?.role ?? ""] ?? user?.role}</small>
            </span>
            <button className="ghost" onClick={logout}>Đăng xuất</button>
          </div>
        </header>
        <main className="content">
          <Outlet />
        </main>
      </div>

      <div className="toasts" aria-live="polite">
        {toasts.map((t) => (
          <button key={t.key} className={`toast toast-${t.alert.level.toLowerCase()}`}
            onClick={() => { setToasts((x) => x.filter((y) => y.key !== t.key)); navigate(`/alerts/${t.alert.id}`); }}>
            <div className="toast-head">
              <strong>{t.title}</strong>
              <LevelBadge level={t.alert.level} />
            </div>
            <div>{areaName(t.alert.areaId)} · {t.alert.sources.map((s) => SOURCE_LABEL[s]).join(", ")}</div>
            <small>{formatTime(t.alert.startedAt)} · bấm để xem chi tiết</small>
          </button>
        ))}
      </div>
    </div>
  );
}
