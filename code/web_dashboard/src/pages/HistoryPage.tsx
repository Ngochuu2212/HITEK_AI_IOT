import { useCallback, useEffect, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { api, type EventFilter } from "../api/client";
import type { EventRow, Paged } from "../api/types";
import { useLookups } from "../components/Lookups";
import { Card, Empty, ErrorBox, Pager } from "../components/ui";
import { EVENT_TYPE_LABEL, SOURCE_LABEL, eventTypeLabel, formatTime, localInputToIso } from "../lib/format";
import { Select } from "./AlertsPage";

const KEYS = ["source", "eventType", "areaId", "deviceId", "from", "to"] as const;

// Tra cứu lịch sử sự kiện của cả camera AI lẫn cảm biến (đề án mục 4.5). Muốn lọc theo mức
// độ hoặc xem ảnh, clip bằng chứng thì dùng trang Cảnh báo — mức và bằng chứng gắn với cảnh báo.
export function HistoryPage() {
  const [params, setParams] = useSearchParams();
  const [data, setData] = useState<Paged<EventRow> | null>(null);
  const [error, setError] = useState<unknown>(null);
  const { areas, devices, areaName, deviceName } = useLookups();
  const page = Number(params.get("page") ?? 1);

  const load = useCallback(() => {
    const filter: EventFilter = { page, pageSize: 25 };
    KEYS.forEach((k) => {
      const v = params.get(k);
      if (v) (filter as Record<string, string>)[k] = k === "from" || k === "to" ? localInputToIso(v)! : v;
    });
    api.events(filter).then((d) => { setData(d); setError(null); }).catch(setError);
  }, [params, page]);

  useEffect(load, [load]);

  const set = (key: string, value: string) => {
    const next = new URLSearchParams(params);
    if (value) next.set(key, value); else next.delete(key);
    next.delete("page");
    setParams(next);
  };

  return (
    <>
      <div className="page-head">
        <h1>Tra cứu lịch sử sự kiện</h1>
        <Link to="/alerts">Lọc cảnh báo theo mức độ, xem bằng chứng ›</Link>
      </div>
      <Card>
        <div className="filters">
          <Select label="Nguồn" value={params.get("source") ?? ""} onChange={(v) => set("source", v)} options={[["", "Tất cả"], ...Object.entries(SOURCE_LABEL)]} />
          <Select label="Loại sự kiện" value={params.get("eventType") ?? ""} onChange={(v) => set("eventType", v)} options={[["", "Tất cả"], ...Object.entries(EVENT_TYPE_LABEL)]} />
          <Select label="Khu vực" value={params.get("areaId") ?? ""} onChange={(v) => set("areaId", v)} options={[["", "Tất cả"], ...areas.map((a) => [a.id, a.name] as [string, string])]} />
          <Select label="Thiết bị" value={params.get("deviceId") ?? ""} onChange={(v) => set("deviceId", v)} options={[["", "Tất cả"], ...devices.map((d) => [d.id, d.name] as [string, string])]} />
          <label>Từ<input type="datetime-local" value={params.get("from") ?? ""} onChange={(e) => set("from", e.target.value)} /></label>
          <label>Đến<input type="datetime-local" value={params.get("to") ?? ""} onChange={(e) => set("to", e.target.value)} /></label>
          <button className="ghost" onClick={() => setParams(new URLSearchParams())}>Xoá lọc</button>
        </div>
      </Card>
      <ErrorBox error={error} />
      <Card>
        {!data ? <Empty>Đang tải…</Empty> : data.items.length === 0 ? <Empty>Không có sự kiện phù hợp.</Empty> : (
          <>
            <table>
              <thead><tr><th>Thời điểm</th><th>Nguồn</th><th>Loại sự kiện</th><th>Thiết bị</th><th>Khu vực</th><th>Độ tin cậy</th></tr></thead>
              <tbody>
                {data.items.map((e) => (
                  <tr key={e.id}>
                    <td>{formatTime(e.occurredAt)}</td>
                    <td>{SOURCE_LABEL[e.source]}</td>
                    <td>{eventTypeLabel(e.eventType)}</td>
                    <td>{deviceName(e.sensorId ?? e.cameraId)}</td>
                    <td>{areaName(e.areaId)}</td>
                    <td>{e.confidence != null ? `${Math.round(e.confidence * 100)}%` : "—"}</td>
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
