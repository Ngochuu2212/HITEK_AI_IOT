import { useEffect, useRef, useState } from "react";
import { api, zoneUrl } from "../api/client";
import type { Camera, DetectionFrame, Zone } from "../api/types";
import { useLookups } from "../components/Lookups";
import { Card, DeviceStatus, Empty, ErrorBox } from "../components/ui";
import { timeAgo } from "../lib/format";

const COLORS: Record<string, string> = { person: "#0ea5e9", car: "#a855f7", motorcycle: "#ec4899" };
const LABELS: Record<string, string> = { person: "Người", car: "Ô tô", motorcycle: "Xe máy" };
// Camera bị ngưng trên trang Thiết bị: Edge Gateway đã đóng luồng nên không hiện ở đây.
const HIDDEN_STATUSES = ["inactive", "disabled"];
// Zone Editor nằm trong giao diện riêng của Zone Service (nhóm AI), mặc định cổng 8010 cùng máy.
const ZONE_EDITOR_URL = import.meta.env.VITE_ZONE_EDITOR_URL || `${window.location.protocol}//${window.location.hostname}:8010`;

export function CamerasPage() {
  const [cameras, setCameras] = useState<Camera[]>([]);
  const [error, setError] = useState<unknown>(null);
  const [ai, setAi] = useState<Record<string, unknown> | null>(null);
  const [aiError, setAiError] = useState(false);

  useEffect(() => {
    // Nạp lại danh sách định kỳ: camera vừa thêm ở trang Thiết bị tự hiện, camera bị ngưng tự ẩn.
    const loadCameras = () => api.cameras()
      .then((list) => { setCameras(list.filter((c) => !HIDDEN_STATUSES.includes(c.status.toLowerCase()))); setError(null); })
      .catch(setError);
    loadCameras();
    const cameraTimer = setInterval(loadCameras, 15000);
    const loadAi = () => api.aiHealth().then((h) => { setAi(h); setAiError(false); }).catch(() => setAiError(true));
    loadAi();
    const timer = setInterval(loadAi, 10000);
    return () => { clearInterval(timer); clearInterval(cameraTimer); };
  }, []);

  const runtime = (ai?.runtime ?? {}) as { device_name?: string; accelerator?: string };
  return (
    <>
      <div className="page-head">
        <h1>Giám sát camera</h1>
        <span className={`dot-label ${ai && !aiError ? "ok" : "bad"}`}>
          <i />
          {ai && !aiError
            ? `AI đang chạy · model ${String(ai.model ?? "").split("/").pop()} · ${runtime.device_name ?? runtime.accelerator ?? "CPU"}`
            : "Không liên lạc được AI Service"}
        </span>
      </div>
      <ErrorBox error={error} />
      {cameras.length === 0 && !error && <Empty>Chưa có camera nào hoặc tài khoản không được giao camera.</Empty>}
      <div className="camera-grid">
        {cameras.map((c) => <CameraTile key={c.id} camera={c} />)}
      </div>
    </>
  );
}

function CameraTile({ camera }: { camera: Camera }) {
  const { areaName } = useLookups();
  const [frame, setFrame] = useState<DetectionFrame | null>(null);
  const [src, setSrc] = useState<string | null>(null);
  const [zones, setZones] = useState<Zone[]>([]);
  const [mode, setMode] = useState<"ai" | "snapshot">("ai");
  const [lastAt, setLastAt] = useState<string | null>(null);
  const canvas = useRef<HTMLCanvasElement>(null);
  const loading = useRef(false);

  // Vùng cấm vẽ ở Zone Editor; nạp lại định kỳ để vùng mới vẽ hiện lên mà không cần tải lại trang.
  useEffect(() => {
    const loadZones = () => api.zones(camera.id).then(setZones).catch(() => setZones([]));
    loadZones();
    const timer = setInterval(loadZones, 30000);
    return () => clearInterval(timer);
  }, [camera.id]);

  // Kết quả AI tới qua Server-Sent Events. Ảnh tải theo đúng frame_id của kết quả để khung
  // vẽ khớp với hình, thay vì vẽ khung của khung hình này lên ảnh của khung hình khác.
  useEffect(() => {
    const source = new EventSource(zoneUrl(`/api/cameras/${camera.id}/detections/stream`));
    let lastMessage = Date.now();
    source.onmessage = (e) => {
      let data: DetectionFrame;
      try { data = JSON.parse(e.data); } catch { return; }
      lastMessage = Date.now();
      setMode("ai");
      if (loading.current) return;
      loading.current = true;
      const url = zoneUrl(`/api/cameras/${camera.id}/frames/${encodeURIComponent(data.frame_id)}.jpg`);
      const img = new Image();
      img.onload = () => { setSrc(url); setFrame(data); setLastAt(data.captured_at); loading.current = false; };
      img.onerror = () => { loading.current = false; };
      img.src = url;
    };

    // Không có kết quả AI (AI tắt hoặc chưa khởi động): hiện ảnh gần nhất của camera.
    const fallback = setInterval(() => {
      if (Date.now() - lastMessage < 4000) return;
      setMode("snapshot");
      setFrame(null);
      setSrc(zoneUrl(`/api/cameras/${camera.id}/latest.jpg?t=${Date.now()}`));
      setLastAt(new Date().toISOString());
    }, 2000);

    return () => { source.close(); clearInterval(fallback); };
  }, [camera.id]);

  useEffect(() => {
    const el = canvas.current;
    if (!el) return;
    const rect = el.getBoundingClientRect();
    el.width = Math.round(rect.width);
    el.height = Math.round(rect.height);
    const ctx = el.getContext("2d");
    if (!ctx) return;
    ctx.clearRect(0, 0, el.width, el.height);
    const fw = frame?.frame_width || zones[0]?.frame_width || 640;
    const fh = frame?.frame_height || zones[0]?.frame_height || 360;

    zones.filter((z) => z.enabled).forEach((z) => {
      const sx = el.width / z.frame_width;
      const sy = el.height / z.frame_height;
      const pts = z.polygon.points;
      if (pts.length < 3) return;
      ctx.beginPath();
      pts.forEach((p, i) => (i ? ctx.lineTo(p.x * sx, p.y * sy) : ctx.moveTo(p.x * sx, p.y * sy)));
      ctx.closePath();
      ctx.fillStyle = "rgba(239, 68, 68, 0.12)";
      ctx.fill();
      ctx.setLineDash([6, 4]);
      ctx.strokeStyle = "#ef4444";
      ctx.lineWidth = 2;
      ctx.stroke();
      ctx.setLineDash([]);
      label(ctx, z.name, pts[0].x * sx + 4, pts[0].y * sy + 20, "#ef4444");
    });

    if (!frame) return;
    const sx = el.width / fw;
    const sy = el.height / fh;
    frame.detections.forEach((d) => {
      const [x1, y1, x2, y2] = d.bbox_xyxy;
      const color = COLORS[d.class_name] ?? "#22c55e";
      const inZone = (d.zone_ids?.length ?? 0) > 0;
      ctx.strokeStyle = inZone ? "#ef4444" : color;
      ctx.lineWidth = inZone ? 3 : 2;
      ctx.strokeRect(x1 * sx, y1 * sy, (x2 - x1) * sx, (y2 - y1) * sy);
      const track = d.track_id != null ? ` #${d.track_id}` : "";
      label(ctx, `${LABELS[d.class_name] ?? d.class_name}${track} ${Math.round(d.confidence * 100)}%`, x1 * sx, y1 * sy, inZone ? "#ef4444" : color);
    });
  }, [frame, zones, src]);

  const counts = frame?.detections.reduce<Record<string, number>>((acc, d) => {
    acc[d.class_name] = (acc[d.class_name] ?? 0) + 1;
    return acc;
  }, {}) ?? {};
  const inZone = frame?.detections.filter((d) => (d.zone_ids?.length ?? 0) > 0).length ?? 0;
  const aspect = `${frame?.frame_width || 16} / ${frame?.frame_height || 9}`;

  return (
    <Card title={camera.name} actions={<DeviceStatus status={camera.status.toUpperCase()} />} className="camera-card">
      <div className="video" style={{ aspectRatio: aspect }}>
        {src ? <img src={src} alt={camera.name} onError={() => setSrc(null)} /> : <div className="video-empty">Đang chờ hình ảnh…</div>}
        <canvas ref={canvas} />
        {inZone > 0 && <div className="video-alert">{inZone} đối tượng trong vùng cấm</div>}
      </div>
      <div className="camera-meta">
        <span>{areaName(camera.location_id)}</span>
        {mode === "ai" && frame ? (
          <span>
            AI: {frame.detection_count} đối tượng
            {Object.keys(counts).length > 0 && ` (${Object.entries(counts).map(([k, v]) => `${v} ${(LABELS[k] ?? k).toLowerCase()}`).join(", ")})`}
            {frame.inference_ms != null && ` · ${Math.round(frame.inference_ms)} ms/khung`}
          </span>
        ) : (
          <span className="muted">Không có kết quả AI, đang hiện ảnh gần nhất</span>
        )}
        <span className="muted">{zones.filter((z) => z.enabled).length} vùng cấm · {timeAgo(lastAt)}</span>
      </div>
      {zones.length === 0 && (
        <p className="muted small">
          Camera chưa có vùng cấm nên AI chưa sinh cảnh báo. Vẽ vùng ở <a href={ZONE_EDITOR_URL} target="_blank" rel="noreferrer">Zone Editor</a>.
        </p>
      )}
    </Card>
  );
}

function label(ctx: CanvasRenderingContext2D, text: string, x: number, y: number, color: string) {
  ctx.font = "600 12px system-ui, sans-serif";
  const w = ctx.measureText(text).width + 8;
  // Giữ nhãn trong khung khi đối tượng sát mép phải.
  x = Math.max(0, Math.min(x, ctx.canvas.width - w));
  const top = Math.max(0, y - 17);
  ctx.fillStyle = color;
  ctx.fillRect(x, top, w, 17);
  ctx.fillStyle = "#fff";
  ctx.fillText(text, x + 4, top + 12.5);
}
