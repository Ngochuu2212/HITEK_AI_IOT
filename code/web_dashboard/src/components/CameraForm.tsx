import { useEffect, useRef, useState, type ChangeEvent, type FormEvent } from "react";
import { api } from "../api/client";
import type { CameraDetail, CameraSourceType, VideoFile } from "../api/types";
import { useLookups } from "./Lookups";
import { Card, ErrorBox } from "./ui";

// Thêm hoặc sửa camera. Lưu xong, Backend ghi bảng camera; Edge Gateway thấy camera mới
// trong vài giây và mở luồng video, AI bắt đầu phân tích, trang Giám sát camera tự hiện.
export function CameraForm({ cameraId, onSaved, onCancel }: {
  cameraId: string | null;
  onSaved: (camera: CameraDetail, created: boolean) => void;
  onCancel: () => void;
}) {
  const { areas } = useLookups();
  const [name, setName] = useState("");
  const [areaId, setAreaId] = useState("");
  const [sourceType, setSourceType] = useState<CameraSourceType>("FILE");
  const [videoFile, setVideoFile] = useState("");
  const [streamUrl, setStreamUrl] = useState("");
  const [videos, setVideos] = useState<VideoFile[]>([]);
  const [aiImageSize, setAiImageSize] = useState(640);
  const [uploading, setUploading] = useState<number | null>(null);
  const fileInput = useRef<HTMLInputElement>(null);
  const [original, setOriginal] = useState<CameraDetail | null>(null);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<unknown>(null);

  useEffect(() => {
    loadVideos();
    if (!cameraId) return;
    api.camera(cameraId).then((c) => {
      setOriginal(c);
      setName(c.name);
      setAreaId(c.areaId);
      setSourceType(c.sourceType);
      setAiImageSize(c.aiImageSize);
      if (c.sourceType === "FILE") setVideoFile(c.sourceUrl); else setStreamUrl(c.sourceUrl);
    }).catch(setError);
  }, [cameraId]);

  useEffect(() => {
    if (!areaId && areas.length) setAreaId(areas[0].id);
  }, [areas, areaId]);

  function loadVideos() {
    return api.videoFiles().then(setVideos).catch(() => setVideos([]));
  }

  // Tải xong thì chọn luôn video vừa tải làm nguồn của camera đang thêm.
  const upload = async (e: ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    e.target.value = "";
    if (!file) return;
    setError(null);
    setUploading(0);
    try {
      const saved = await api.uploadVideo(file, setUploading);
      await loadVideos();
      setVideoFile(saved.name);
    } catch (err) {
      setError(err);
    } finally {
      setUploading(null);
    }
  };

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setSaving(true);
    setError(null);
    const input = { name, areaId, sourceType, sourceUrl: sourceType === "FILE" ? videoFile : streamUrl, aiImageSize };
    try {
      const saved = cameraId ? await api.updateCamera(cameraId, input) : await api.createCamera(input);
      onSaved(saved, !cameraId);
    } catch (err) {
      setError(err);
    } finally {
      setSaving(false);
    }
  };

  // Video đang gắn với camera khác thì không chọn được (mỗi nguồn một camera).
  const freeVideo = (v: VideoFile) => !v.usedByCamera || v.usedByCamera === original?.name;

  return (
    <Card title={cameraId ? `Sửa camera ${original?.name ?? ""}` : "Thêm camera"}>
      <form className="camera-form" onSubmit={submit}>
        <label>
          Tên camera
          <input value={name} onChange={(e) => setName(e.target.value)} maxLength={255} required placeholder="Ví dụ: Camera 03 - Bãi xe" />
        </label>
        <label>
          Khu vực
          <select value={areaId} onChange={(e) => setAreaId(e.target.value)} required>
            {areas.map((a) => <option key={a.id} value={a.id}>{a.name}</option>)}
          </select>
        </label>
        <fieldset>
          <legend>Nguồn hình ảnh</legend>
          <label className="radio">
            <input type="radio" checked={sourceType === "FILE"} onChange={() => setSourceType("FILE")} />
            Video mô phỏng
          </label>
          <label className="radio">
            <input type="radio" checked={sourceType === "RTSP"} onChange={() => setSourceType("RTSP")} />
            Camera IP (RTSP)
          </label>
        </fieldset>
        {sourceType === "FILE" ? (
          <label>
            Video
            <div className="input-row">
            <select value={videoFile} onChange={(e) => setVideoFile(e.target.value)} required>
              <option value="">Chọn video…</option>
              {videos.map((v) => (
                <option key={v.name} value={v.name} disabled={!freeVideo(v)}>
                  {v.name} ({(v.sizeBytes / 1048576).toFixed(1)} MB){freeVideo(v) ? "" : ` · đang dùng cho ${v.usedByCamera}`}
                </option>
              ))}
            </select>
              <button type="button" onClick={() => fileInput.current?.click()} disabled={uploading !== null}>
                {uploading !== null ? `Đang tải lên ${uploading}%` : "Tải video lên…"}
              </button>
              <input ref={fileInput} type="file" accept=".mp4,.avi,.mkv,.mov,.ts,video/*" hidden onChange={upload} />
            </div>
            {uploading !== null && <progress max={100} value={uploading} />}
            <span className="hint">Video lưu ở thư mục <code>code/data/videos</code>, phát lặp lại như camera thật. Nên dùng .mp4 (H.264), tối đa 300 MB.</span>
          </label>
        ) : (
          <label>
            Địa chỉ luồng
            <input value={streamUrl} onChange={(e) => setStreamUrl(e.target.value)} required
              placeholder="rtsp://tài-khoản:mật-khẩu@192.168.1.64:554/Streaming/Channels/101" />
            <span className="hint">Máy chạy Edge Gateway phải truy cập được địa chỉ này.</span>
          </label>
        )}
        <label className="wide">
          Độ chi tiết khi AI phân tích
          <select value={aiImageSize} onChange={(e) => setAiImageSize(Number(e.target.value))}>
            <option value={640}>Thường (640): nhanh, đủ cho người ở gần camera</option>
            <option value={960}>Vừa (960)</option>
            <option value={1280}>Cao (1280): người nhỏ, đám đông, quay từ xa; tốn CPU khoảng 4 lần</option>
          </select>
        </label>
        <ErrorBox error={error} />
        <div className="form-actions">
          <button type="button" className="ghost" onClick={onCancel}>Huỷ</button>
          <button className="primary" disabled={saving}>{saving ? "Đang lưu…" : cameraId ? "Lưu thay đổi" : "Thêm camera"}</button>
        </div>
      </form>
    </Card>
  );
}
