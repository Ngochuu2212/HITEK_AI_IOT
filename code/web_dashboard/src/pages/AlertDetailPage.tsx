import { useCallback, useEffect, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { api, zoneUrl } from "../api/client";
import type { Alert, AlertEvent, Evidence, HandledStatus } from "../api/types";
import { useAuth } from "../auth/AuthContext";
import { useAlertUpdates } from "../realtime/AlertHub";
import { useLookups } from "../components/Lookups";
import { Card, Empty, ErrorBox, LevelBadge, StatusBadge } from "../components/ui";
import { SOURCE_LABEL, STATUS_LABEL, canHandleAlerts, duration, eventTypeLabel, formatTime } from "../lib/format";

export function AlertDetailPage() {
  const { id = "" } = useParams();
  const { user } = useAuth();
  const { areaName, deviceName } = useLookups();
  const [alert, setAlert] = useState<Alert | null>(null);
  const [trace, setTrace] = useState<AlertEvent[]>([]);
  const [evidence, setEvidence] = useState<Evidence[]>([]);
  const [error, setError] = useState<unknown>(null);
  const [status, setStatus] = useState<HandledStatus>("OPEN");
  const [note, setNote] = useState("");
  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState<unknown>(null);
  const [saved, setSaved] = useState(false);

  const load = useCallback(() => {
    api.alert(id).then((a) => { setAlert(a); setError(null); }).catch(setError);
    api.alertEvents(id).then(setTrace).catch(() => {});
    api.alertEvidence(id).then(setEvidence).catch(() => {});
  }, [id]);

  useEffect(load, [load]);
  // Chỉ nạp lại ô nhập khi trạng thái xử lý thật sự đổi, để cập nhật realtime (đổi mức,
  // thêm sự kiện) không xoá mất ghi chú người vận hành đang gõ dở.
  const handledKey = alert ? `${alert.id}|${alert.handledStatus}|${alert.handledAt}` : "";
  useEffect(() => {
    if (alert) { setStatus(alert.handledStatus); setNote(alert.note ?? ""); }
  }, [handledKey]);
  useAlertUpdates((a) => { if (a.id === id) load(); });

  if (!alert) return <><p><Link to="/alerts">‹ Danh sách cảnh báo</Link></p><ErrorBox error={error} />{!error && <Empty>Đang tải…</Empty>}</>;

  const closed = alert.handledStatus === "CLOSED";
  const canEdit = canHandleAlerts(user?.role) && !closed;

  const save = async () => {
    setSaving(true); setSaveError(null); setSaved(false);
    try {
      setAlert(await api.updateAlert(id, status, note.trim() || null));
      setSaved(true);
    } catch (e) {
      setSaveError(e);
    } finally {
      setSaving(false);
    }
  };

  return (
    <>
      <p><Link to="/alerts">‹ Danh sách cảnh báo</Link></p>
      <div className="page-head">
        <h1>Cảnh báo tại {areaName(alert.areaId)}</h1>
        <LevelBadge level={alert.peakLevel} />
        <StatusBadge status={alert.handledStatus} />
      </div>

      <div className="grid-2">
        <Card title="Thông tin">
          <dl className="facts">
            <dt>Bắt đầu</dt><dd>{formatTime(alert.startedAt)}</dd>
            <dt>Kết thúc</dt><dd>{alert.endedAt ? formatTime(alert.endedAt) : <span className="pill-live">đang mở</span>}</dd>
            <dt>Thời lượng</dt><dd>{duration(alert.startedAt, alert.endedAt)}</dd>
            <dt>Mức hiện tại</dt><dd>{alert.endedAt ? "—" : <LevelBadge level={alert.level} />} </dd>
            <dt>Mức cao nhất</dt><dd><LevelBadge level={alert.peakLevel} /></dd>
            <dt>Nguồn phát sinh</dt><dd>{alert.sources.map((s) => SOURCE_LABEL[s]).join(", ") || "—"}</dd>
            <dt>Loại sự kiện</dt><dd>{alert.eventTypes.map(eventTypeLabel).join(", ") || "—"}</dd>
            <dt>Luật quyết định mức</dt><dd>{alert.triggeredRules.join(", ") || "—"}</dd>
            <dt>Số nguồn đang hoạt động</dt><dd>{alert.sourceCount}</dd>
          </dl>
        </Card>

        <Card title="Xử lý cảnh báo">
          {alert.handledBy && (
            <p className="muted">Cập nhật gần nhất: {alert.handledBy} lúc {formatTime(alert.handledAt)}</p>
          )}
          <label className="block">
            Trạng thái xử lý
            <select value={status} disabled={!canEdit} onChange={(e) => setStatus(e.target.value as HandledStatus)}>
              {Object.entries(STATUS_LABEL).map(([v, t]) => <option key={v} value={v}>{t}</option>)}
            </select>
          </label>
          <label className="block">
            Ghi chú của người vận hành
            <textarea rows={4} maxLength={2000} value={note} disabled={!canEdit} onChange={(e) => setNote(e.target.value)}
              placeholder="Ví dụ: đã cử bảo vệ kiểm tra, là nhân viên kho làm thêm giờ…" />
          </label>
          {closed && <p className="muted">Cảnh báo đã đóng xử lý, không sửa thêm được.</p>}
          {!canHandleAlerts(user?.role) && <p className="muted">Tài khoản chỉ có quyền xem.</p>}
          <ErrorBox error={saveError} />
          {saved && <div className="ok-box">Đã lưu.</div>}
          {canEdit && <button className="primary" onClick={save} disabled={saving}>{saving ? "Đang lưu…" : "Lưu xử lý"}</button>}
        </Card>
      </div>

      <Card title={`Bằng chứng (${evidence.length})`}>
        {evidence.length === 0 ? <Empty>Chưa có ảnh hoặc clip bằng chứng. Bằng chứng do AI Service chụp khi camera phát hiện vi phạm.</Empty> : (
          <div className="evidence-grid">
            {evidence.map((e) => {
              const url = zoneUrl(`/api/evidence/${e.id}/media`);
              const isVideo = e.mimeType.startsWith("video/") || e.evidenceType === "video_clip";
              return (
                <figure key={e.id}>
                  {isVideo ? <video src={url} controls preload="metadata" /> : <a href={url} target="_blank" rel="noreferrer"><img src={url} alt="Bằng chứng" loading="lazy" /></a>}
                  <figcaption>
                    {isVideo ? `Clip${e.durationSeconds ? ` ${Math.round(e.durationSeconds)} giây` : ""}` : "Ảnh chụp"} · {deviceName(e.cameraId)}
                    <br /><span className="muted">{formatTime(e.capturedAt)}</span>
                  </figcaption>
                </figure>
              );
            })}
          </div>
        )}
      </Card>

      <Card title="Truy vết: sự kiện đã góp vào cảnh báo">
        {trace.length === 0 ? <Empty>Chưa có sự kiện.</Empty> : (
          <table>
            <thead><tr><th>Thời điểm</th><th>Nguồn</th><th>Loại sự kiện</th><th>Thiết bị</th><th>Độ tin cậy</th><th>Mức khi nối</th></tr></thead>
            <tbody>
              {trace.map((t) => (
                <tr key={t.eventId}>
                  <td>{formatTime(t.occurredAt)}</td>
                  <td>{SOURCE_LABEL[t.source]}</td>
                  <td>{eventTypeLabel(t.eventType)}</td>
                  <td>{deviceName(t.sensorId ?? t.cameraId)}</td>
                  <td>{t.confidence != null ? `${Math.round(t.confidence * 100)}%` : "—"}</td>
                  <td><LevelBadge level={t.levelAtLink} /></td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </Card>
    </>
  );
}
