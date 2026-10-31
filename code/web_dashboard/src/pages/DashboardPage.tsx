import { useCallback, useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { api } from "../api/client";
import type { DashboardSummary } from "../api/types";
import { useAlertUpdates } from "../realtime/AlertHub";
import { useLookups } from "../components/Lookups";
import { Card, Empty, ErrorBox, LevelBadge, Stat, StatusBadge } from "../components/ui";
import { SOURCE_LABEL, eventTypeLabel, formatTime, timeAgo } from "../lib/format";

export function DashboardPage() {
  const [data, setData] = useState<DashboardSummary | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [updatedAt, setUpdatedAt] = useState<string | null>(null);
  const { areaName } = useLookups();

  const load = useCallback(() => {
    api.summary()
      .then((d) => { setData(d); setError(null); setUpdatedAt(new Date().toISOString()); })
      .catch(setError);
  }, []);

  useEffect(() => {
    load();
    // Trạng thái thiết bị đổi theo thời gian dù không có cảnh báo nào, nên vẫn làm mới định kỳ.
    const timer = setInterval(load, 15000);
    return () => clearInterval(timer);
  }, [load]);
  useAlertUpdates(load);

  if (!data) return <><h1>Tổng quan</h1><ErrorBox error={error} />{!error && <Empty>Đang tải…</Empty>}</>;

  const { cameras, sensors, eventsToday, alertsToday } = data;
  const levels = [
    { key: "critical", label: "Khẩn cấp", value: alertsToday.critical },
    { key: "high", label: "Cao", value: alertsToday.high },
    { key: "medium", label: "Trung bình", value: alertsToday.medium },
    { key: "low", label: "Thấp", value: alertsToday.low },
  ];
  const maxLevel = Math.max(1, ...levels.map((l) => l.value));

  return (
    <>
      <div className="page-head">
        <h1>Tổng quan</h1>
        <span className="muted">Cập nhật {timeAgo(updatedAt)}</span>
      </div>
      <ErrorBox error={error} />

      <div className="stats">
        <Stat label="Cảnh báo đang mở" value={data.openAlerts} tone={data.openAlerts ? "bad" : "ok"}
          hint={<Link to="/alerts?open=true">Xem danh sách</Link>} />
        <Stat label="Camera" value={`${cameras.active}/${cameras.total}`} tone={cameras.active < cameras.total ? "warn" : "ok"}
          hint={`${cameras.offline} mất kết nối · ${cameras.inactive + cameras.disabled} tạm ngưng`} />
        <Stat label="Cảm biến trực tuyến" value={`${sensors.online}/${sensors.total}`} tone={sensors.offline ? "warn" : "ok"}
          hint={`${sensors.offline} mất kết nối`} />
        <Stat label="Sự kiện hôm nay" value={eventsToday.total}
          hint={`AI ${eventsToday.ai} · PIR ${eventsToday.pir} · Cửa ${eventsToday.door} · Tia ${eventsToday.beam}`} />
      </div>

      <div className="grid-2">
        <Card title="Cảnh báo hôm nay theo mức độ">
          <div className="bars">
            {levels.map((l) => (
              <div className="bar-row" key={l.key}>
                <span>{l.label}</span>
                <div className="bar-track"><div className={`bar-fill level-${l.key}`} style={{ width: `${(l.value / maxLevel) * 100}%` }} /></div>
                <strong>{l.value}</strong>
              </div>
            ))}
          </div>
          <p className="muted small">Tính theo mức cao nhất mỗi cảnh báo từng đạt. Tổng: {alertsToday.total}.</p>
        </Card>

        <Card title="Sự kiện hôm nay theo nguồn">
          <div className="bars">
            {[
              { k: "AI", v: eventsToday.ai }, { k: "PIR", v: eventsToday.pir },
              { k: "DOOR", v: eventsToday.door }, { k: "BEAM", v: eventsToday.beam },
            ].map((s) => (
              <div className="bar-row" key={s.k}>
                <span>{SOURCE_LABEL[s.k as keyof typeof SOURCE_LABEL]}</span>
                <div className="bar-track"><div className="bar-fill neutral" style={{ width: `${(s.v / Math.max(1, eventsToday.total)) * 100}%` }} /></div>
                <strong>{s.v}</strong>
              </div>
            ))}
          </div>
        </Card>
      </div>

      <Card title="Cảnh báo gần nhất" actions={<Link to="/alerts">Tất cả cảnh báo</Link>}>
        {data.recentAlerts.length === 0 ? <Empty>Chưa có cảnh báo nào.</Empty> : (
          <table>
            <thead><tr><th>Thời gian</th><th>Khu vực</th><th>Mức</th><th>Nguồn</th><th>Loại sự kiện</th><th>Xử lý</th></tr></thead>
            <tbody>
              {data.recentAlerts.map((a) => (
                <tr key={a.id}>
                  <td><Link to={`/alerts/${a.id}`}>{formatTime(a.startedAt)}</Link>{!a.endedAt && <span className="pill-live">đang mở</span>}</td>
                  <td>{areaName(a.areaId)}</td>
                  <td><LevelBadge level={a.peakLevel} /></td>
                  <td>{a.sources.map((s) => SOURCE_LABEL[s]).join(", ")}</td>
                  <td>{a.eventTypes.map(eventTypeLabel).join(", ") || "—"}</td>
                  <td><StatusBadge status={a.handledStatus} /></td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </Card>
    </>
  );
}
