import { useCallback, useEffect, useState } from "react";
import { api } from "../api/client";
import type { Paged, SystemLog } from "../api/types";
import { useAuth } from "../auth/AuthContext";
import { useAlertUpdates } from "../realtime/AlertHub";
import { Card, Empty, ErrorBox, Pager } from "../components/ui";
import { canHandleAlerts, formatTime } from "../lib/format";
import { Select } from "./AlertsPage";

const CATEGORY: Record<string, string> = { SENSOR: "Cảm biến", CAMERA: "Camera", ALERT: "Cảnh báo", RISK_RULE: "Luật cảnh báo", USER: "Người dùng" };
const LEVEL: Record<string, string> = { INFO: "Thông tin", WARNING: "Cảnh báo", ERROR: "Lỗi" };

export function LogsPage() {
  const { user } = useAuth();
  const [category, setCategory] = useState("");
  const [level, setLevel] = useState("");
  const [page, setPage] = useState(1);
  const [data, setData] = useState<Paged<SystemLog> | null>(null);
  const [error, setError] = useState<unknown>(null);

  const load = useCallback(() => {
    api.systemLogs({ category: category || undefined, level: level || undefined, page })
      .then((d) => { setData(d); setError(null); })
      .catch(setError);
  }, [category, level, page]);

  useEffect(load, [load]);
  useAlertUpdates(load);

  if (!canHandleAlerts(user?.role)) {
    return <><h1>Nhật ký hệ thống</h1><Empty>Nhật ký chỉ dành cho tài khoản vận hành hoặc quản trị.</Empty></>;
  }

  return (
    <>
      <div className="page-head"><h1>Nhật ký hệ thống</h1></div>
      <Card>
        <div className="filters">
          <Select label="Nhóm" value={category} onChange={(v) => { setCategory(v); setPage(1); }} options={[["", "Tất cả"], ...Object.entries(CATEGORY)]} />
          <Select label="Mức" value={level} onChange={(v) => { setLevel(v); setPage(1); }} options={[["", "Tất cả"], ...Object.entries(LEVEL)]} />
        </div>
      </Card>
      <ErrorBox error={error} />
      <Card>
        {!data ? <Empty>Đang tải…</Empty> : data.items.length === 0 ? <Empty>Chưa có nhật ký.</Empty> : (
          <>
            <table>
              <thead><tr><th>Thời điểm</th><th>Nhóm</th><th>Mức</th><th>Nội dung</th><th>Người thực hiện</th></tr></thead>
              <tbody>
                {data.items.map((l) => (
                  <tr key={l.id}>
                    <td className="nowrap">{formatTime(l.occurredAt)}</td>
                    <td className="nowrap">{CATEGORY[l.category] ?? l.category}</td>
                    <td><span className={`badge log-${l.level.toLowerCase()}`}>{LEVEL[l.level] ?? l.level}</span></td>
                    <td>{l.message}</td>
                    <td>{l.actor === "system" ? "Hệ thống" : l.actor ?? "—"}</td>
                  </tr>
                ))}
              </tbody>
            </table>
            <Pager page={data.page} pageSize={data.pageSize} total={data.total} onPage={setPage} />
          </>
        )}
      </Card>
    </>
  );
}
