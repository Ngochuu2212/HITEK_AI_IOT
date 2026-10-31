// Khớp với DTO của Backend API (code/backend/src/Hitek.BackendApi/Dtos). Thời gian là chuỗi ISO 8601 UTC.

export type AlertLevel = "LOW" | "MEDIUM" | "HIGH" | "CRITICAL";
export type HandledStatus = "OPEN" | "ACKNOWLEDGED" | "CLOSED";
export type EventSource = "AI" | "PIR" | "DOOR" | "BEAM";

export interface Paged<T> {
  items: T[];
  page: number;
  pageSize: number;
  total: number;
}

export interface TokenResponse {
  accessToken: string;
  tokenType: string;
  expiresAt: string;
  tenantId: string;
  role: string;
  user: { id: string; email: string; fullName: string } | null;
}

export interface Alert {
  id: string;
  areaId: string;
  level: AlertLevel;
  peakLevel: AlertLevel;
  sourceCount: number;
  sources: EventSource[];
  eventTypes: string[];
  triggeredRules: string[];
  startedAt: string;
  endedAt: string | null;
  handledStatus: HandledStatus;
  note: string | null;
  handledBy: string | null;
  handledAt: string | null;
}

export interface AlertEvent {
  eventId: string;
  source: EventSource;
  eventType: string | null;
  occurredAt: string;
  confidence: number | null;
  sensorId: string | null;
  cameraId: string | null;
  levelAtLink: AlertLevel;
  linkedAt: string;
}

export interface Evidence {
  id: string;
  evidenceType: string;
  mimeType: string;
  fileSize: number | null;
  capturedAt: string;
  durationSeconds: number | null;
  cameraId: string;
  aiEventId: string;
  status: string | null;
  mediaUrl: string | null;
}

export interface DashboardSummary {
  cameras: { total: number; active: number; inactive: number; offline: number; disabled: number };
  sensors: { total: number; online: number; offline: number };
  eventsToday: { total: number; ai: number; pir: number; door: number; beam: number };
  alertsToday: { total: number; low: number; medium: number; high: number; critical: number };
  openAlerts: number;
  recentAlerts: Alert[];
}

export interface Device {
  id: string;
  kind: "CAMERA" | "SENSOR";
  name: string;
  type: string;
  areaId: string;
  status: string;
  lastSeenAt: string | null;
}

// Camera do quản trị viên quản lý (GET /api/v1/cameras). sourceUrl có thể chứa mật khẩu camera.
export type CameraSourceType = "RTSP" | "FILE";
export interface CameraDetail {
  id: string;
  name: string;
  areaId: string;
  sourceType: CameraSourceType;
  sourceUrl: string;
  status: string;
  aiImageSize: number;
  lastSeenAt: string | null;
  createdAt: string;
  updatedAt: string;
}
export interface CameraInput {
  name: string;
  areaId: string;
  sourceType: CameraSourceType;
  sourceUrl: string;
  aiImageSize: number;
}
export interface VideoFile {
  name: string;
  sizeBytes: number;
  usedByCamera: string | null;
}

export interface Area {
  id: string;
  code: string;
  name: string;
}

export interface EventRow {
  id: string;
  source: EventSource;
  eventType: string | null;
  areaId: string;
  occurredAt: string;
  confidence: number | null;
  sensorId: string | null;
  cameraId: string | null;
  aiEventId: string | null;
  sensorReadingId: string | null;
}

export interface SensorReading {
  id: string;
  sensorId: string;
  sensorType: string;
  areaId: string;
  value: boolean;
  recordedAt: string;
  receivedAt: string | null;
}

export interface SystemLog {
  id: string;
  occurredAt: string;
  level: "INFO" | "WARNING" | "ERROR";
  category: string;
  message: string;
  entityId: string | null;
  actor: string | null;
}

// Zone Service (nhóm AI)
export interface Camera {
  id: string;
  location_id: string;
  name: string;
  source_type: string;
  status: string;
}

export interface Zone {
  id: string;
  name: string;
  zone_type: string;
  enabled: boolean;
  frame_width: number;
  frame_height: number;
  polygon: { points: { x: number; y: number }[] };
}

export interface Detection {
  class_name: string;
  confidence: number;
  bbox_xyxy: [number, number, number, number];
  track_id?: number | null;
  zone_ids?: string[];
}

export interface DetectionFrame {
  camera_id: string;
  frame_id: string;
  frame_width: number;
  frame_height: number;
  captured_at: string;
  detections: Detection[];
  detection_count: number;
  inference_ms?: number;
  model?: string;
  rule_engine?: { violations?: unknown[]; zones_loaded?: number };
}
