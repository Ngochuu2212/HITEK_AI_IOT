import threading
from dataclasses import dataclass
from pathlib import Path
from typing import Dict, Iterable, List, Optional

import yaml


@dataclass(frozen=True)
class CameraIngestConfig:
    camera_id: str
    location_id: str
    name: str
    source_type: str
    source_url: str
    target_fps: float
    frame_width: int
    frame_height: int
    encoding: str
    enabled: bool = True
    ai_image_size: Optional[int] = None


@dataclass(frozen=True)
class VideoIngestConfig:
    reconnect_interval_seconds: float
    read_retry_count: int
    log_every_n_frames: int
    jpeg_quality: int
    cameras: List[CameraIngestConfig]
    # Gia tri mac dinh cho camera them tu Web Dashboard (khong co trong file cau hinh).
    default_target_fps: float = 10
    default_frame_width: int = 1280
    default_frame_height: int = 720
    default_encoding: str = "JPEG"

def load_video_ingest_config(path: str = "config.yaml") -> VideoIngestConfig:
    config_path = Path(path).resolve()
    raw = yaml.safe_load(config_path.read_text(encoding="utf-8"))
    ingest = raw.get("video_ingest", {})
    defaults = ingest.get("defaults", {})
    cameras = []

    for item in raw.get("cameras", []):
        enabled = bool(item.get("enabled", True))
        cameras.append(
            CameraIngestConfig(
                camera_id=item["camera_id"],
                location_id=item["location_id"],
                name=item["name"],
                source_type=item.get("source_type", defaults.get("source_type", "RTSP")).upper(),
                source_url=item["source_url"],
                target_fps=float(item.get("target_fps", defaults.get("target_fps", 10))),
                frame_width=int(item.get("frame_width", defaults.get("frame_width", 1280))),
                frame_height=int(item.get("frame_height", defaults.get("frame_height", 720))),
                encoding=item.get("encoding", defaults.get("encoding", "JPEG")).upper(),
                enabled=enabled,
                ai_image_size=int(item["ai_image_size"]) if item.get("ai_image_size") else None,
            )
        )

    return VideoIngestConfig(
        reconnect_interval_seconds=float(ingest.get("reconnect_interval_seconds", 3)),
        read_retry_count=int(ingest.get("read_retry_count", 3)),
        log_every_n_frames=int(ingest.get("log_every_n_frames", 30)),
        jpeg_quality=int(defaults.get("jpeg_quality", 85)),
        cameras=[camera for camera in cameras if camera.enabled],
        default_target_fps=float(defaults.get("target_fps", 10)),
        default_frame_width=int(defaults.get("frame_width", 1280)),
        default_frame_height=int(defaults.get("frame_height", 720)),
        default_encoding=str(defaults.get("encoding", "JPEG")).upper(),
    )


def camera_from_catalog(item: dict, ingest: VideoIngestConfig) -> CameraIngestConfig:
    """Camera lay tu danh sach camera cua Zone Service (bang camera trong database)."""
    return CameraIngestConfig(
        camera_id=str(item["id"]),
        location_id=str(item["location_id"]),
        name=item["name"],
        source_type=str(item.get("source_type") or "RTSP").upper(),
        source_url=item["source_url"],
        target_fps=ingest.default_target_fps,
        frame_width=ingest.default_frame_width,
        frame_height=ingest.default_frame_height,
        encoding=ingest.default_encoding,
        enabled=True,
        ai_image_size=int(item["ai_image_size"]) if item.get("ai_image_size") else None,
    )


class CameraCatalog:
    """Danh sach camera dang chay, dung chung giua luong dong bo camera va viewer HTTP."""

    def __init__(self, cameras: Iterable[CameraIngestConfig] = ()) -> None:
        self._lock = threading.Lock()
        self._cameras: Dict[str, CameraIngestConfig] = {camera.camera_id: camera for camera in cameras}

    def put(self, camera: CameraIngestConfig) -> None:
        with self._lock:
            self._cameras[camera.camera_id] = camera

    def remove(self, camera_id: str) -> None:
        with self._lock:
            self._cameras.pop(camera_id, None)

    def get(self, camera_id: str) -> Optional[CameraIngestConfig]:
        with self._lock:
            return self._cameras.get(camera_id)

    def list(self) -> List[CameraIngestConfig]:
        with self._lock:
            return sorted(self._cameras.values(), key=lambda camera: (camera.name, camera.camera_id))

    def __contains__(self, camera_id: object) -> bool:
        with self._lock:
            return camera_id in self._cameras

    def __len__(self) -> int:
        with self._lock:
            return len(self._cameras)
