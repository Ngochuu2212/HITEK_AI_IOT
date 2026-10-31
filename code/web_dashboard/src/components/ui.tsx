import type { ReactNode } from "react";
import type { AlertLevel, HandledStatus } from "../api/types";
import { DEVICE_STATUS_LABEL, LEVEL_LABEL, STATUS_LABEL } from "../lib/format";

export function LevelBadge({ level }: { level: AlertLevel }) {
  return <span className={`badge level-${level.toLowerCase()}`}>{LEVEL_LABEL[level]}</span>;
}

export function StatusBadge({ status }: { status: HandledStatus }) {
  return <span className={`badge status-${status.toLowerCase()}`}>{STATUS_LABEL[status]}</span>;
}

export function DeviceStatus({ status }: { status: string }) {
  const online = status === "ONLINE" || status === "ACTIVE";
  return (
    <span className={`dot-label ${online ? "ok" : "bad"}`}>
      <i />
      {DEVICE_STATUS_LABEL[status] ?? status}
    </span>
  );
}

export function Card({ title, actions, children, className = "" }: { title?: ReactNode; actions?: ReactNode; children: ReactNode; className?: string }) {
  return (
    <section className={`card ${className}`}>
      {(title || actions) && (
        <header className="card-head">
          <h2>{title}</h2>
          <div className="card-actions">{actions}</div>
        </header>
      )}
      {children}
    </section>
  );
}

export function Stat({ label, value, hint, tone }: { label: string; value: ReactNode; hint?: ReactNode; tone?: "ok" | "warn" | "bad" }) {
  return (
    <div className={`stat ${tone ? `stat-${tone}` : ""}`}>
      <div className="stat-label">{label}</div>
      <div className="stat-value">{value}</div>
      {hint && <div className="stat-hint">{hint}</div>}
    </div>
  );
}

export function Pager({ page, pageSize, total, onPage }: { page: number; pageSize: number; total: number; onPage: (p: number) => void }) {
  const pages = Math.max(1, Math.ceil(total / pageSize));
  return (
    <div className="pager">
      <span>{total} bản ghi</span>
      <button disabled={page <= 1} onClick={() => onPage(page - 1)}>‹ Trước</button>
      <span>Trang {page}/{pages}</span>
      <button disabled={page >= pages} onClick={() => onPage(page + 1)}>Sau ›</button>
    </div>
  );
}

export function Empty({ children }: { children: ReactNode }) {
  return <div className="empty">{children}</div>;
}

export function ErrorBox({ error }: { error: unknown }) {
  if (!error) return null;
  const message = error instanceof Error ? error.message : String(error);
  return <div className="error-box">{message}</div>;
}
