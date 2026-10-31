import { useCallback, useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { api } from "../api/client";
import type { Device, Paged, SensorReading } from "../api/types";
import { useAuth } from "../auth/AuthContext";
import { CameraForm } from "../components/CameraForm";
import { useLookups } from "../components/Lookups";
import { Card, DeviceStatus, Empty, ErrorBox, Pager } from "../components/ui";
import { formatTime, isAdmin, timeAgo } from "../lib/format";
import { Select } from "./AlertsPage";

const TYPE_LABEL: Record<string, string> = {
  PIR: "Cảm biến chuyển động (PIR)", DOOR: "Cảm biến cửa", BEAM: "Tia hồng ngoại", RTSP: "Camera IP (RTSP)", FILE: "Camera (video mô phỏng)",
};

export function DevicesPage() {
  const { user } = useAuth();
  const { areas, areaName, reload: reloadLookups } = useLookups();
  const admin = isAdmin(user?.role);
  const [devices, setDevices] = useState<Device[]>([]);
  const [kind, setKind] = useState("");
  const [areaId, setAreaId] = useState("");
  const [error, setError] = useState<unknown>(null);
  const [selected, setSelected] = useState<Device | null>(null);
  // null: không mở form; "new": thêm camera; id: sửa camera đó.
  const [editing, setEditing] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const load = useCallback(() => {
    api.devices({ kind: kind || undefined, areaId: areaId || undefined })
      .then((r) => { setDevices(r.items); setError(null); })
      .catch(setError);
  }, [kind, areaId]);

  useEffect(() => {
    load();
    const timer = setInterval(load, 10000);
    return () => clearInterval(timer);
  }, [load]);

  const afterChange = (message: string) => {
    setNotice(message);
    load();
    reloadLookups();
  };

  const toggleCamera = async (d: Device) => {
    const enable = d.status === "INACTIVE" || d.status === "DISABLED";
    if (!enable && !window.confirm(`Ngưng camera "${d.name}"? Hệ thống dừng đọc video và phân tích AI; lịch sử cảnh báo vẫn giữ.`)) return;
    try {
      await api.setCameraEnabled(d.id, enable);
      afterChange(enable ? `Đã bật lại camera ${d.name}.` : `Đã ngưng camera ${d.name}.`);
    } catch (err) {
      setError(err);
    }
  };

  const online = devices.filter((d) => d.status === "ONLINE" || d.status === "ACTIVE").length;

  return (
    <>
      <div className="page-head">
        <h1>Thiết bị</h1>
        <span className="muted">{online}/{devices.length} thiết bị đang hoạt động · tự làm mới mỗi 10 giây</span>
        {admin && !editing && <button className="primary" onClick={() => { setEditing("new"); setNotice(null); }}>+ Thêm camera</button>}
      </div>

      {editing && (
        <CameraForm
          cameraId={editing === "new" ? null : editing}
          onCancel={() => setEditing(null)}
          onSaved={(camera, created) => {
            setEditing(null);
            afterChange(created
              ? `Đã thêm camera ${camera.name}. Edge Gateway mở luồng video trong vài giây, sau đó camera hiện ở trang Giám sát camera. Vẽ vùng cấm ở Zone Editor để AI bắt đầu sinh cảnh báo.`
              : `Đã lưu camera ${camera.name}.`);
          }}
        />
      )}
      {notice && <div className="ok-box">{notice} {notice.startsWith("Đã thêm") && <Link to="/cameras">Mở Giám sát camera ›</Link>}</div>}

      <Card>
        <div className="filters">
          <Select label="Loại" value={kind} onChange={setKind} options={[["", "Tất cả"], ["CAMERA", "Camera"], ["SENSOR", "Cảm biến"]]} />
          <Select label="Khu vực" value={areaId} onChange={setAreaId} options={[["", "Tất cả"], ...areas.map((a) => [a.id, a.name] as [string, string])]} />
        </div>
      </Card>
      <ErrorBox error={error} />
      <div className={selected ? "grid-2" : ""}>
        <Card>
          {devices.length === 0 ? <Empty>Không có thiết bị.</Empty> : (
            <table>
              <thead><tr><th>Tên</th><th>Loại</th><th>Vị trí</th><th>Trạng thái</th><th>Nhận dữ liệu cuối</th>{admin && <th />}</tr></thead>
              <tbody>
                {devices.map((d) => (
                  <tr key={d.id} className={selected?.id === d.id ? "selected" : ""}>
                    <td>
                      {d.kind === "SENSOR"
                        ? <button className="link" onClick={() => setSelected(d)}>{d.name}</button>
                        : d.name}
                    </td>
                    <td>{d.kind === "CAMERA" ? TYPE_LABEL[d.type] ?? "Camera" : TYPE_LABEL[d.type] ?? d.type}</td>
                    <td>{areaName(d.areaId)}</td>
                    <td><DeviceStatus status={d.status} /></td>
                    <td title={formatTime(d.lastSeenAt)}>{d.lastSeenAt ? timeAgo(d.lastSeenAt) : "—"}</td>
                    {admin && (
                      <td className="row-actions">
                        {d.kind === "CAMERA" && (
                          <>
                            <button className="link" onClick={() => { setEditing(d.id); setNotice(null); window.scrollTo({ top: 0, behavior: "smooth" }); }}>Sửa</button>
                            <button className="link" onClick={() => toggleCamera(d)}>
                              {d.status === "INACTIVE" || d.status === "DISABLED" ? "Bật lại" : "Ngưng"}
                            </button>
                          </>
                        )}
                      </td>
                    )}
                  </tr>
                ))}
              </tbody>
            </table>
          )}
          <p className="muted small">Bấm tên cảm biến để xem lịch sử đọc. Cảm biến quá 30 giây không gửi dữ liệu sẽ chuyển sang "Mất kết nối".</p>
        </Card>
        {selected && <ReadingsPanel device={selected} onClose={() => setSelected(null)} />}
      </div>
    </>
  );
}

function ReadingsPanel({ device, onClose }: { device: Device; onClose: () => void }) {
  const [page, setPage] = useState(1);
  const [data, setData] = useState<Paged<SensorReading> | null>(null);
  const [error, setError] = useState<unknown>(null);

  useEffect(() => {
    api.sensorReadings(device.id, page).then(setData).catch(setError);
  }, [device.id, page]);

  return (
    <Card title={`Lịch sử đọc: ${device.name}`} actions={<><Link to={`/history?deviceId=${device.id}`}>Sự kiện</Link><button className="ghost" onClick={onClose}>Đóng</button></>}>
      <ErrorBox error={error} />
      {!data ? <Empty>Đang tải…</Empty> : data.items.length === 0 ? <Empty>Chưa có lượt đọc nào.</Empty> : (
        <>
          <table>
            <thead><tr><th>Thiết bị ghi nhận</th><th>Giá trị</th><th>Độ trễ</th></tr></thead>
            <tbody>
              {data.items.map((r) => (
                <tr key={r.id}>
                  <td>{formatTime(r.recordedAt)}</td>
                  <td>{r.value ? <span className="badge level-high">Kích hoạt</span> : <span className="badge muted-badge">Bình thường</span>}</td>
                  <td>{r.receivedAt ? `${Math.max(0, new Date(r.receivedAt).getTime() - new Date(r.recordedAt).getTime())} ms` : "—"}</td>
                </tr>
              ))}
            </tbody>
          </table>
          <Pager page={data.page} pageSize={data.pageSize} total={data.total} onPage={setPage} />
        </>
      )}
    </Card>
  );
}
