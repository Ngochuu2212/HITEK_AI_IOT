"""Dong bo danh sach camera khi Edge Gateway dang chay.

Camera them, sua hoac ngung tren Web Dashboard duoc ghi vao bang camera. Edge Gateway hoi
Zone Service danh sach camera dinh ky, mo luong doc cho camera moi, mo lai camera doi
nguon, dong camera bi ngung. Khong can sua file cau hinh hay build lai image.
"""

import json
import logging
import threading
import urllib.request
from dataclasses import replace
from typing import Callable, Dict, List, Optional, Tuple

from .config import CameraCatalog, CameraIngestConfig, VideoIngestConfig, camera_from_catalog
from .video_ingest import VideoIngestWorker


LOGGER = logging.getLogger(__name__)

# Trang thai trong bang camera: active, inactive (bi ngung tren dashboard), offline, disabled.
# Camera offline van doc tiep de tu noi lai khi nguon co hinh tro lai.
RUNNING_STATUSES = {"active", "offline"}


class CameraManager:
    """Giu moi camera mot VideoIngestWorker chay trong luong rieng."""

    def __init__(
        self,
        ingest_config: VideoIngestConfig,
        sink,
        catalog: CameraCatalog,
        on_camera_started: Optional[Callable[[CameraIngestConfig], None]] = None,
        fps_override: float = 0,
        worker_factory=VideoIngestWorker,
    ) -> None:
        self.ingest_config = ingest_config
        self.sink = sink
        self.catalog = catalog
        self.on_camera_started = on_camera_started
        self.fps_override = fps_override
        self.worker_factory = worker_factory
        self._workers: Dict[str, Tuple[CameraIngestConfig, object, threading.Thread]] = {}
        self._lock = threading.Lock()

    def apply(self, cameras: List[CameraIngestConfig]) -> None:
        """Dua tap camera dang chay ve dung danh sach mong muon."""
        if self.fps_override > 0:
            cameras = [replace(camera, target_fps=self.fps_override) for camera in cameras]
        desired = {camera.camera_id: camera for camera in cameras}
        with self._lock:
            for camera_id in list(self._workers):
                running, worker, thread = self._workers[camera_id]
                wanted = desired.get(camera_id)
                if wanted is None or _source_key(wanted) != _source_key(running):
                    self._stop(camera_id, "removed" if wanted is None else "source changed")
                elif wanted != running:
                    # Doi ten, khu vuc, do chi tiet AI: ap dung cho khung hinh tiep theo, khong
                    # can dong mo lai luong video.
                    worker.camera = wanted
                    self._workers[camera_id] = (wanted, worker, thread)
                    self.catalog.put(wanted)
                    if self.on_camera_started:
                        self.on_camera_started(wanted)
                    LOGGER.info("Camera updated id=%s name=%s ai_image_size=%s", camera_id, wanted.name, wanted.ai_image_size)
            for camera_id, camera in desired.items():
                if camera_id not in self._workers:
                    self._start(camera)

    def camera_ids(self) -> List[str]:
        with self._lock:
            return sorted(self._workers)

    def stop_all(self) -> None:
        with self._lock:
            for camera_id in list(self._workers):
                self._stop(camera_id, "shutdown")

    def workers(self):
        with self._lock:
            return [worker for _, worker, _ in self._workers.values()]

    def _start(self, camera: CameraIngestConfig) -> None:
        worker = self.worker_factory(camera, self.ingest_config, self.sink, threading.Event())
        thread = threading.Thread(target=worker.run, name="edge-camera-{}".format(camera.camera_id), daemon=True)
        self._workers[camera.camera_id] = (camera, worker, thread)
        self.catalog.put(camera)
        if self.on_camera_started:
            self.on_camera_started(camera)
        thread.start()
        LOGGER.info("Camera started id=%s name=%s source=%s %s", camera.camera_id, camera.name, camera.source_type, camera.source_url)

    def _stop(self, camera_id: str, reason: str) -> None:
        camera, worker, thread = self._workers.pop(camera_id)
        self.catalog.remove(camera_id)
        worker.stop()
        thread.join(timeout=6.0)
        LOGGER.info("Camera stopped id=%s name=%s reason=%s", camera_id, camera.name, reason)


def _source_key(camera: CameraIngestConfig):
    # Chi nhung thay doi nay moi phai mo lai luong video.
    return (camera.source_type, camera.source_url, camera.target_fps)


def fetch_catalog_cameras(url: str, service_api_key: Optional[str], ingest: VideoIngestConfig, timeout: float = 5) -> List[CameraIngestConfig]:
    headers = {"Accept": "application/json"}
    if service_api_key:
        headers["X-Service-Key"] = service_api_key
    request = urllib.request.Request(url, headers=headers)
    with urllib.request.urlopen(request, timeout=timeout) as response:
        items = json.loads(response.read().decode("utf-8"))
    return [
        camera_from_catalog(item, ingest)
        for item in items
        if str(item.get("status", "active")).lower() in RUNNING_STATUSES and item.get("source_url")
    ]


def start_camera_sync(
    manager: CameraManager,
    url: str,
    service_api_key: Optional[str],
    interval_seconds: float,
    stop_event: threading.Event,
) -> threading.Thread:
    """Moi interval_seconds lay danh sach camera tu Zone Service. Loi mang thi giu nguyen camera
    dang chay, lan sau thu lai."""

    def loop() -> None:
        failed = False
        while not stop_event.is_set():
            try:
                manager.apply(fetch_catalog_cameras(url, service_api_key, manager.ingest_config))
                if failed:
                    LOGGER.info("Camera sync recovered from %s", url)
                failed = False
            except Exception as exc:  # noqa: BLE001 - chi ghi log, giu camera hien tai
                if not failed:
                    LOGGER.warning("Camera sync from %s failed: %s (keeping current cameras)", url, exc)
                failed = True
            stop_event.wait(interval_seconds)

    thread = threading.Thread(target=loop, name="edge-camera-sync", daemon=True)
    thread.start()
    LOGGER.info("Camera sync enabled: %s every %ss", url, interval_seconds)
    return thread
