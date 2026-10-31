import type { AlertLevel, EventSource, HandledStatus } from "../api/types";

export const LEVEL_LABEL: Record<AlertLevel, string> = {
  LOW: "Thấp",
  MEDIUM: "Trung bình",
  HIGH: "Cao",
  CRITICAL: "Khẩn cấp",
};

export const STATUS_LABEL: Record<HandledStatus, string> = {
  OPEN: "Chưa xử lý",
  ACKNOWLEDGED: "Đang xử lý",
  CLOSED: "Đã đóng",
};

export const SOURCE_LABEL: Record<EventSource, string> = {
  AI: "Camera AI",
  PIR: "Cảm biến chuyển động",
  DOOR: "Cảm biến cửa",
  BEAM: "Tia hồng ngoại",
};

export const EVENT_TYPE_LABEL: Record<string, string> = {
  person_intrusion: "Người xâm nhập vùng cấm",
  vehicle_intrusion: "Phương tiện xâm nhập",
  loitering: "Lảng vảng quá lâu",
  crowd_limit: "Vượt số người cho phép",
  vehicle_stopping: "Phương tiện dừng đỗ",
  motion_detected: "Phát hiện chuyển động",
  door_opened: "Cửa mở",
  beam_broken: "Tia hồng ngoại bị cắt",
};

export const DEVICE_STATUS_LABEL: Record<string, string> = {
  ONLINE: "Trực tuyến",
  OFFLINE: "Mất kết nối",
  ACTIVE: "Hoạt động",
  INACTIVE: "Tạm ngưng",
  DISABLED: "Vô hiệu",
};

export const eventTypeLabel = (t: string | null | undefined) => (t ? EVENT_TYPE_LABEL[t] ?? t : "—");

// Mọi mốc thời gian từ API là UTC; hiển thị theo giờ máy người xem (giờ Việt Nam).
export function formatTime(iso: string | null | undefined, withDate = true) {
  if (!iso) return "—";
  const d = new Date(iso);
  return d.toLocaleString("vi-VN", {
    hour: "2-digit", minute: "2-digit", second: "2-digit",
    ...(withDate ? { day: "2-digit", month: "2-digit", year: "numeric" } : {}),
  });
}

export function timeAgo(iso: string | null | undefined) {
  if (!iso) return "chưa có dữ liệu";
  const seconds = Math.round((Date.now() - new Date(iso).getTime()) / 1000);
  if (seconds < 5) return "vừa xong";
  if (seconds < 60) return `${seconds} giây trước`;
  if (seconds < 3600) return `${Math.floor(seconds / 60)} phút trước`;
  if (seconds < 86400) return `${Math.floor(seconds / 3600)} giờ trước`;
  return `${Math.floor(seconds / 86400)} ngày trước`;
}

export function duration(fromIso: string, toIso: string | null) {
  const ms = (toIso ? new Date(toIso).getTime() : Date.now()) - new Date(fromIso).getTime();
  const m = Math.floor(ms / 60000);
  const s = Math.floor((ms % 60000) / 1000);
  return m > 0 ? `${m} phút ${s} giây` : `${s} giây`;
}

// <input type="datetime-local"> dùng giờ địa phương; API cần UTC.
export const localInputToIso = (v: string) => (v ? new Date(v).toISOString() : undefined);

export const ROLE_LABEL: Record<string, string> = {
  owner: "Chủ sở hữu",
  admin: "Quản trị",
  operator: "Vận hành",
  viewer: "Chỉ xem",
};

export const canHandleAlerts = (role: string | undefined) => ["operator", "admin", "owner"].includes(role ?? "");
export const isAdmin = (role: string | undefined) => ["admin", "owner"].includes(role ?? "");
