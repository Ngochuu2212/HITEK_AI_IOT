import { useCallback, useEffect, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { api, type AlertFilter } from "../api/client";
import type { Alert, Paged } from "../api/types";
import { useAlertUpdates } from "../realtime/AlertHub";
import { useLookups } from "../components/Lookups";
import { Card, Empty, ErrorBox, LevelBadge, Pager, StatusBadge } from "../components/ui";
import { EVENT_TYPE_LABEL, LEVEL_LABEL, SOURCE_LABEL, STATUS_LABEL, duration, eventTypeLabel, formatTime, localInputToIso } from "../lib/format";

const FILTER_KEYS = ["open", "level", "status", "areaId", "source", "eventType", "deviceId", "from", "to"] as const;

export function AlertsPage() {
  const [params, setParams] = useSearchParams();
  const [data, setData] = useState<Paged<Alert> | null>(null);
  const [error, setError] = useState<unknown>(null);
  const { areas, devices, areaName } = useLookups();
  const page = Number(params.get("page") ?? 1);

  const load = useCallback(() => {
    const filter: AlertFilter = { page, pageSize: 20 };
    FILTER_KEYS.forEach((k) => {
      const v = params.get(k);
      if (!v) return;
      (filter as Record<string, string>)[k] = k === "from" || k === "to" ? localInputToIso(v)! : v;
    });
    api.alerts(filter).then((d) => { setData(d); setError(null); }).catch(setError);
  }, [params, page]);

  useEffect(load, [load]);
  useAlertUpdates(load);

  const set = (key: string, value: string) => {
    const next = new URLSearchParams(params);
    if (value) next.set(key, value); else next.delete(key);
    next.delete("page");
    setParams(next);
  };

  return (
    <>
      <div className="page-head"><h1>Cảnh báo</h1></div>
      <Card>
        <div className="filters">
          <Select label="Trạng thái" value={params.get("open") ?? ""} onChange={(v) => set("open", v)}
            options={[["", "Tất cả"], ["true", "Đang mở"], ["false", "Đã kết thúc"]]} />
          <Select label="Mức độ (cao nhất)" value={params.get("level") ?? ""} onChange={(v) => set("level", v)}
            options={[["", "Tất cả"], ...Object.entries(LEVEL_LABEL)]} />
          <Select label="Xử lý" value={params.get("status") ?? ""} onChange={(v) => set("status", v)}
            options={[["", "Tất cả"], ...Object.entries(STATUS_LABEL)]} />
          <Select label="Khu vực" value={params.get("areaId") ?? ""} onChange={(v) => set("areaId", v)}
            options={[["", "Tất cả"], ...areas.map((a) => [a.id, a.name] as [string, string])]} />
          <Select label="Nguồn" value={params.get("source") ?? ""} onChange={(v) => set("source", v)}
            options={[["", "Tất cả"], ...Object.entries(SOURCE_LABEL)]} />
          <Select label="Loại sự kiện" value={params.get("eventType") ?? ""} onChange={(v) => set("eventType", v)}
            options={[["", "Tất cả"], ...Object.entries(EVENT_TYPE_LABEL)]} />
          <Select label="Thiết bị" value={params.get("deviceId") ?? ""} onChange={(v) => set("deviceId", v)}
            options={[["", "Tất cả"], ...devices.map((d) => [d.id, d.name] as [string, string])]} />
          <label>Từ<input type="datetime-local" value={params.get("from") ?? ""} onChange={(e) => set("from", e.target.value)} /></label>
          <label>Đến<input type="datetime-local" value={params.get("to") ?? ""} onChange={(e) => set("to", e.target.value)} /></label>
          <button className="ghost" onClick={() => setParams(new URLSearchParams())}>Xoá lọc</button>
        </div>
      </Card>

      <ErrorBox error={error} />
      <Card>
        {!data ? <Empty>Đang tải…</Empty> : data.items.length === 0 ? <Empty>Không có cảnh báo phù hợp.</Empty> : (
          <>
            <table>
              <thead>
                <tr><th>Bắt đầu</th><th>Khu vực</th><th>Mức hiện tại</th><th>Cao nhất</th><th>Nguồn</th><th>Loại sự kiện</th><th>Thời lượng</th><th>Xử lý</th></tr>
              </thead>
              <tbody>
                {data.items.map((a) => (
                  <tr key={a.id}>
                    <td><Link to={`/alerts/${a.id}`}>{formatTime(a.startedAt)}</Link></td>
                    <td>{areaName(a.areaId)}</td>
                    <td>{a.endedAt ? <span className="muted">Đã kết thúc</span> : <LevelBadge level={a.level} />}</td>
                    <td><LevelBadge level={a.peakLevel} /></td>
                    <td>{a.sources.map((s) => SOURCE_LABEL[s]).join(", ")}</td>
                    <td>{a.eventTypes.map(eventTypeLabel).join(", ") || "—"}</td>
                    <td>{duration(a.startedAt, a.endedAt)}{!a.endedAt && <span className="pill-live">đang mở</span>}</td>
                    <td><StatusBadge status={a.handledStatus} />{a.handledBy && <div className="small muted">{a.handledBy}</div>}</td>
                  </tr>
                ))}
              </tbody>
            </table>
            <Pager page={data.page} pageSize={data.pageSize} total={data.total} onPage={(p) => { const n = new URLSearchParams(params); n.set("page", String(p)); setParams(n); }} />
          </>
        )}
      </Card>
    </>
  );
}

export function Select({ label, value, onChange, options }: { label: string; value: string; onChange: (v: string) => void; options: [string, string][] }) {
  return (
    <label>
      {label}
      <select value={value} onChange={(e) => onChange(e.target.value)}>
        {options.map(([v, t]) => <option key={v} value={v}>{t}</option>)}
      </select>
    </label>
  );
}
