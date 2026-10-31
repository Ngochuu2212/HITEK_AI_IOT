import type {
  Alert, AlertEvent, Area, Camera, CameraDetail, CameraInput, DashboardSummary, Device, EventRow, Evidence,
  HandledStatus, Paged, SensorReading, SystemLog, TokenResponse, VideoFile, Zone,
} from "./types";

const TOKEN_KEY = "hitek.token";

export function getToken(): string | null {
  try {
    return localStorage.getItem(TOKEN_KEY);
  } catch {
    return null;
  }
}

export function setToken(token: string | null) {
  try {
    if (token) localStorage.setItem(TOKEN_KEY, token);
    else localStorage.removeItem(TOKEN_KEY);
  } catch {
    // Trình duyệt chặn localStorage: phiên đăng nhập chỉ còn trong bộ nhớ.
  }
}

export class ApiError extends Error {
  constructor(public status: number, public code: string, message: string) {
    super(message);
  }
}

// Hết hạn đăng nhập thì báo cho AuthContext để đưa về màn hình đăng nhập.
let onUnauthorized: (() => void) | null = null;
export function setUnauthorizedHandler(handler: () => void) {
  onUnauthorized = handler;
}

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const headers = new Headers(init.headers);
  const token = getToken();
  if (token) headers.set("Authorization", `Bearer ${token}`);
  if (init.body && !headers.has("Content-Type")) headers.set("Content-Type", "application/json");

  const response = await fetch(path, { ...init, headers });
  if (response.status === 204) return undefined as T;

  const text = await response.text();
  const body = text ? safeJson(text) : null;
  if (!response.ok) {
    if (response.status === 401 && token) onUnauthorized?.();
    // Backend trả { error: { code, message } }; Zone Service trả { detail }.
    const code = body?.error?.code ?? `HTTP_${response.status}`;
    const message = body?.error?.message ?? body?.detail ?? `Lỗi ${response.status}`;
    throw new ApiError(response.status, code, typeof message === "string" ? message : JSON.stringify(message));
  }
  return body as T;
}

function safeJson(text: string) {
  try {
    return JSON.parse(text);
  } catch {
    return { detail: text };
  }
}

function query(params: Record<string, string | number | boolean | undefined | null>) {
  const search = new URLSearchParams();
  Object.entries(params).forEach(([k, v]) => {
    if (v !== undefined && v !== null && v !== "") search.set(k, String(v));
  });
  const s = search.toString();
  return s ? `?${s}` : "";
}

// Ảnh, luồng MJPEG và EventSource không gửi được header Authorization, nên Zone Service
// nhận token qua tham số access_token.
export function zoneUrl(path: string) {
  const token = getToken();
  const sep = path.includes("?") ? "&" : "?";
  return `/zone${path}${token ? `${sep}access_token=${encodeURIComponent(token)}` : ""}`;
}

// Tải video lên bằng XMLHttpRequest vì fetch chưa báo được tiến độ gửi lên.
function uploadVideo(file: File, onProgress: (percent: number) => void): Promise<VideoFile> {
  return new Promise((resolve, reject) => {
    const xhr = new XMLHttpRequest();
    xhr.open("POST", "/api/v1/cameras/video-files");
    const token = getToken();
    if (token) xhr.setRequestHeader("Authorization", `Bearer ${token}`);
    xhr.upload.onprogress = (e) => { if (e.lengthComputable) onProgress(Math.round((e.loaded / e.total) * 100)); };
    xhr.onload = () => {
      const body = xhr.responseText ? safeJson(xhr.responseText) : null;
      if (xhr.status >= 200 && xhr.status < 300) return resolve(body as VideoFile);
      if (xhr.status === 401) onUnauthorized?.();
      const message = xhr.status === 413
        ? "Video quá lớn (tối đa 300 MB)."
        : body?.error?.message ?? body?.detail ?? `Lỗi ${xhr.status}`;
      reject(new ApiError(xhr.status, body?.error?.code ?? `HTTP_${xhr.status}`, message));
    };
    xhr.onerror = () => reject(new ApiError(0, "NETWORK", "Mất kết nối khi tải video lên."));
    const form = new FormData();
    form.append("file", file);
    xhr.send(form);
  });
}

export type AlertFilter = {
  level?: string; status?: string; areaId?: string; source?: string; eventType?: string;
  deviceId?: string; open?: string; from?: string; to?: string; page?: number; pageSize?: number;
};
export type EventFilter = {
  source?: string; eventType?: string; areaId?: string; deviceId?: string; from?: string; to?: string;
  page?: number; pageSize?: number;
};

export const api = {
  login: (email: string, password: string) =>
    request<TokenResponse>("/api/v1/auth/login", { method: "POST", body: JSON.stringify({ email, password }) }),
  me: () => request<{ subject: string; role: string; name: string | null; email: string | null; tenantId: string }>("/api/v1/auth/me"),

  summary: () => request<DashboardSummary>("/api/v1/dashboard/summary"),
  areas: () => request<Area[]>("/api/v1/areas"),
  devices: (params: { kind?: string; areaId?: string } = {}) =>
    request<Paged<Device>>(`/api/v1/devices${query({ ...params, pageSize: 100 })}`),
  sensorReadings: (id: string, page = 1) =>
    request<Paged<SensorReading>>(`/api/v1/sensors/${id}/readings${query({ page, pageSize: 20 })}`),

  alerts: (filter: AlertFilter) => request<Paged<Alert>>(`/api/v1/alerts${query(filter)}`),
  alert: (id: string) => request<Alert>(`/api/v1/alerts/${id}`),
  alertEvents: (id: string) => request<AlertEvent[]>(`/api/v1/alerts/${id}/events`),
  alertEvidence: (id: string) => request<Evidence[]>(`/api/v1/alerts/${id}/evidence`),
  updateAlert: (id: string, handledStatus: HandledStatus, note: string | null) =>
    request<Alert>(`/api/v1/alerts/${id}`, { method: "PATCH", body: JSON.stringify({ handledStatus, note }) }),

  events: (filter: EventFilter) => request<Paged<EventRow>>(`/api/v1/events${query(filter)}`),
  systemLogs: (params: { category?: string; level?: string; page?: number }) =>
    request<Paged<SystemLog>>(`/api/v1/system-logs${query({ ...params, pageSize: 30 })}`),

  // Quản lý camera (quản trị viên). Edge Gateway tự mở/đóng luồng video theo bảng camera.
  camera: (id: string) => request<CameraDetail>(`/api/v1/cameras/${id}`),
  videoFiles: () => request<VideoFile[]>("/api/v1/cameras/video-files"),
  createCamera: (input: CameraInput) =>
    request<CameraDetail>("/api/v1/cameras", { method: "POST", body: JSON.stringify(input) }),
  updateCamera: (id: string, input: CameraInput) =>
    request<CameraDetail>(`/api/v1/cameras/${id}`, { method: "PUT", body: JSON.stringify(input) }),
  uploadVideo,
  setCameraEnabled: (id: string, enabled: boolean) =>
    request<CameraDetail>(`/api/v1/cameras/${id}/status`, { method: "PATCH", body: JSON.stringify({ enabled }) }),

  cameras: () => request<Camera[]>("/zone/api/cameras"),
  zones: (cameraId: string) => request<Zone[]>(`/zone/api/cameras/${cameraId}/zones`),
  aiHealth: () => request<Record<string, unknown>>("/ai/health"),
};
